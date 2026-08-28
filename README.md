# ToBeClarify API

ToBeClarify 公開網站與後台管理系統共用的 ASP.NET Core API。

- 公開 API：`/api/client/*`
- 後台 API：`/api/admin/*`
- Swagger UI：<https://api.marchgroup.net/swagger/index.html>
- Client Swagger：<https://api.marchgroup.net/swagger/client/swagger.json>
- Admin Swagger：<https://api.marchgroup.net/swagger/admin/swagger.json>

開發環境、架構、端點、權限、資料表、媒體處理、CI/CD、已知風險與多人協作規範，請閱讀：

- [API 開發說明](docs/API-DEVELOPMENT-GUIDE.md)

## Quick start

需求：.NET 10 SDK、MySQL，以及未提交到 Git 的 `appsettings.Local.json`。

```powershell
dotnet restore .\ToBeClarify.Api.csproj
dotnet run --project .\ToBeClarify.Api.csproj
```

Development 環境可由 `/swagger` 開啟 Swagger UI。正式機密、連線字串、JWT signing key 與一次性驗證碼不得提交到 repository。
