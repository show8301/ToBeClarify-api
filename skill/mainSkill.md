# API 開發技能（C# / MySQL / RESTful）

## 使用時機
當任務涉及本專案（客戶端／後台管理端共用之 Web API，C# + MySQL，部署於 IIS）的
API 開發、路由規劃、Middleware、驗證機制或 Logger 設計時，套用本文件規範。
**目前階段只需實作「客戶端 API」，後台端 API 先建好骨架、預留擴充點即可，不用完整實作。**

---

## 1. 技術棧
- 語言：C#（SDK-Style API，.NET 10）
- 資料庫：MySQL（優先使用 `Dapper`, 再者透過 `MySql.Data`）
- 部署：IIS（需搭配 `ASP.NET Core Module V2`，記得確認 `web.config` 有正確設定 `hostingModel`）

---

## 2. 專案分層與資料夾結構（以「功能」為導向，而非「頁面」）

```
/src
  /Controllers
    /Client            <-- 客戶端 API（無 JWT）
      ProductsController.cs
      OrdersController.cs
      CartController.cs
    /Admin             <-- 後台 API（需 JWT，先建骨架）
      ProductsController.cs
      OrdersController.cs
  /Middlewares
    ExceptionHandlingMiddleware.cs
    RequestInterceptorMiddleware.cs
  /Services
    /Logging
      IApiLogService.cs
      ApiLogService.cs        <-- 寫 DB，失敗 fallback 寫檔
  /Auth
    JwtAuthOptions.cs
    JwtTokenService.cs
  /Models
    /Entities
    /Dtos
  /Infrastructure
    AppDbContext.cs
Program.cs
appsettings.json
```

> 路由切分原則：以「業務功能」分組（Products、Orders、Cart、Members…），
> 不要以「畫面／頁面」分組（例如不要用 HomePage、ProductListPage 這種命名）。

---

## 3. RESTful 路由規範

- 客戶端統一前綴：`/api/client/{resource}`
- 後台端統一前綴：`/api/admin/{resource}`
- 資源用複數名詞，動作用 HTTP Method 表達，不要把動詞放進路由：
  - `GET /api/client/products` 取得清單
  - `GET /api/client/products/{id}` 取得單筆
  - `POST /api/client/orders` 建立訂單
  - `PUT /api/client/orders/{id}` 更新整筆
  - `PATCH /api/client/orders/{id}/status` 局部更新（有明確子資源動作時可用巢狀路由）
  - `DELETE /api/admin/products/{id}` 刪除
- 巢狀資源以真實從屬關係為限，避免超過兩層：
  `/api/client/orders/{orderId}/items`
- 版本化預留：`/api/v1/client/products`（若目前不需要，至少在 Program.cs 保留路由前綴變數方便未來加版本號）

---

## 4. Middleware 設計

### 4.1 執行順序（Program.cs 中的順序很重要）
```
1. ExceptionHandlingMiddleware   // 最外層，攔截所有例外
2. RequestInterceptorMiddleware  // 記錄 IP / 裝置 / 呼叫紀錄
3. UseAuthentication / UseAuthorization
4. MapControllers
```

### 4.2 ExceptionHandlingMiddleware（全域例外處理）
- 攔截所有未處理例外，統一轉換成一致的錯誤回應格式：
```json
{
  "success": false,
  "errorCode": "SERVER_ERROR",
  "message": "系統發生錯誤，請稍後再試",
  "traceId": "xxxxx"
}
```
- 依例外型別分類回傳對應的 HTTP Status（例：自訂 `BusinessException` → 400，
  `UnauthorizedException` → 401，其餘未知例外 → 500）。
- 例外內容（含 StackTrace）要傳給 Logger 記錄，但**不要**回傳給前端，避免洩漏內部資訊。

### 4.3 RequestInterceptorMiddleware（請求攔截器）
每一支 API 被呼叫時，不論客戶端或後台端，都要記錄：
- 來源 IP（注意透過 IIS / 反向代理時要讀 `X-Forwarded-For`，讀不到才 fallback 用
  `HttpContext.Connection.RemoteIpAddress`）
- 裝置資訊（從 `User-Agent` 解析，可用簡易字串記錄，不強求裝置指紋）
- 呼叫的路由、Method、狀態碼、耗時（用 `Stopwatch` 包住 `await _next(context)`）
- 若有登入者（後台端），一併記錄使用者識別

實作建議：在這個 Middleware 裡組出 `ApiLogEntry` 物件，呼叫 `IApiLogService.LogAsync(entry)`，
不要直接在 Middleware 裡寫資料庫邏輯，保持職責分離。

---

## 5. 驗證機制（Client 不需要 JWT，Admin 需要）

- 客戶端 Controller 上不加 `[Authorize]`。
- 後台端 Controller 統一加 `[Authorize(Policy = "AdminOnly")]`，並在 `Program.cs` 設定：
```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => { /* 從 appsettings 讀 Key/Issuer/Audience */ });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireAuthenticatedUser().RequireClaim("role", "admin"));
});
```
- 因為目前只做客戶端，後台端 Controller 可以先建立空殼＋標好 `[Authorize]`，
  但**不需要**急著實作登入發 Token 的完整流程，只要架構上預留即可。
- **重要**：不論有沒有 JWT，`RequestInterceptorMiddleware` 都要在 `UseAuthentication` **之前**
  執行，確保匿名的客戶端請求也會被記錄到 IP / 裝置。

---

## 6. Logger 設計（寫 DB，失敗 fallback 寫實體 txt）

### 6.1 資料表建議（MySQL）
```sql
CREATE TABLE api_logs (
  id BIGINT AUTO_INCREMENT PRIMARY KEY,
  request_time DATETIME NOT NULL,
  ip_address VARCHAR(45) NOT NULL,
  device_info VARCHAR(255),
  api_type ENUM('client','admin') NOT NULL,
  method VARCHAR(10) NOT NULL,
  path VARCHAR(255) NOT NULL,
  status_code INT,
  duration_ms INT,
  exception_message TEXT NULL,
  created_at DATETIME DEFAULT CURRENT_TIMESTAMP
);
```

### 6.2 ApiLogService 行為
```csharp
public class ApiLogService : IApiLogService
{
    public async Task LogAsync(ApiLogEntry entry)
    {
        try
        {
            // 寫入 MySQL api_logs 資料表
        }
        catch
        {
            // DB 寫入失敗，fallback 寫入本機文字檔
            await FallbackWriteToFileAsync(entry);
        }
    }

    private async Task FallbackWriteToFileAsync(ApiLogEntry entry)
    {
        var line = $"{entry.RequestTime:o}\t{entry.IpAddress}\t{entry.Method}\t{entry.Path}\t{entry.StatusCode}\t{entry.ExceptionMessage}";
        var filePath = Path.Combine(AppContext.BaseDirectory, "Logs", $"fallback_{DateTime.Now:yyyyMMdd}.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.AppendAllTextAsync(filePath, line + Environment.NewLine);
    }
}
```
- fallback 檔案務必用 `AppendAllTextAsync` 而非覆寫，並依日期分檔避免單檔過大。
- 寫檔過程也要包 try-catch，避免 Logger 本身把整個請求搞掛（Logger 失敗不能拋例外往上冒）。
- IIS 部署時要確認應用程式集區的執行帳號對 `Logs` 資料夾有寫入權限。

---

## 7. Swagger 文件設定

### 7.1 套件與註冊
- 使用 `Swashbuckle.AspNetCore`。
- 在 `Program.cs` 註冊，並分別替 Client / Admin API 建立獨立的 Swagger 文件（避免兩邊路由混在同一份文件裡）：
```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("client", new OpenApiInfo { Title = "Client API", Version = "v1" });
    options.SwaggerDoc("admin", new OpenApiInfo { Title = "Admin API", Version = "v1" });

    // 依 Controller 所在的路由前綴分派到對應文件
    options.DocInclusionPredicate((docName, apiDesc) =>
    {
        var route = apiDesc.RelativePath ?? string.Empty;
        if (docName == "client") return route.StartsWith("api/client");
        if (docName == "admin") return route.StartsWith("api/admin");
        return false;
    });

    // 讓 Admin 文件支援輸入 JWT 測試
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "請輸入格式：Bearer {token}"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});
```

### 7.2 Middleware 掛載
```csharp
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwagger"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/client/swagger.json", "Client API");
        options.SwaggerEndpoint("/swagger/admin/swagger.json", "Admin API");
        options.RoutePrefix = "swagger";
    });
}
```
- 建議用 `appsettings.json` 的 `EnableSwagger` 開關控制正式環境是否開放 Swagger UI，
  正式站（IIS 上線環境）預設應關閉，避免對外曝露完整 API 文件與路由結構。
- 若正式環境仍需保留 Swagger 供內部測試，務必加上簡單的存取限制（例如僅限內網 IP、
  或額外加一層 Basic Auth），不要直接對外公開。

### 7.3 文件品質建議
- Controller / Action 上加上 XML 註解或 `[ProducesResponseType]`，讓 Swagger 產出的
  回應範例與統一回應格式（`ApiResponse<T>`）一致，方便前端或測試人員直接照著文件串接。
- 若專案啟用 XML 註解，記得在 `.csproj` 開啟 `GenerateDocumentationFile`，
  並在 `AddSwaggerGen` 中用 `options.IncludeXmlComments(xmlPath)` 引入。

---

## 8. 統一回應格式（建議）
```json
// 成功
{ "success": true, "data": { ... } }

// 失敗
{ "success": false, "errorCode": "...", "message": "..." }
```
所有 Controller 回傳都透過共用的 `ApiResponse<T>` 包裝，方便前端統一解析。

---

## 9. 開發時的檢查清單
- [ ] 新增的路由是否放在正確的 `Client` / `Admin` 分組資料夾與路由前綴下？
- [ ] 路由命名是否以功能／資源為主，而非頁面？
- [ ] 是否經過 `ExceptionHandlingMiddleware`（不會讓例外直接噴到前端）？
- [ ] 是否有被 `RequestInterceptorMiddleware` 記錄到 IP / 裝置？
- [ ] 後台 API 是否掛上 `[Authorize(Policy = "AdminOnly")]`？客戶端 API 是否維持匿名？
- [ ] Logger 寫 DB 失敗時是否有成功 fallback 到 txt，且不會讓請求本身失敗？
- [ ] 新增的 Controller / Action 是否會正確歸類到對應的 Swagger 文件（`client` 或 `admin`）？
- [ ] 正式環境（IIS）的 Swagger 是否已依 `EnableSwagger` 設定關閉或加上存取限制？
