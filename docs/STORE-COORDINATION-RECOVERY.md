# 協調接單無法承接的修正

2026-10-18。適用既有 `POST /api/admin/orders/{orderId}/store-confirmation`，沒有新端點或資料庫 migration。

## 問題與復原順序

Flow v2 指名履約在店家尚未承接時回覆 `STORE_CONFIRMATION_REQUIRED`；原本店家承接端點卻先執行舊版履約防護，回覆 `ORDER_REQUIRES_FULFILLMENT`，形成無法承接的迴圈。僅新增 Web 按鈕無法修復 API。

若店家誤用協調接單，先在經理工作台的營運控制確認預計關店時間，再切回一般接單。已超過預計關店時間時，API 仍會將一般接單視為協調接單，必須先將預計關店時間調整至實際未來時間。切換模式不會自動承接既有協調單。

API 修正發布後，使用既有經理／服務工作台的「接受協調單」，再分別處理指名、餐點等履約。不需要批次直接修改資料庫，也不需要取消後重建顧客訂單。

## 交易範圍

- 店家承接是兩種流程共用的訂單前置步驟；其他舊版整單履約端點仍保留原有防護。
- Flow v2 決定依顧客帳戶、訂單、履約項目的順序鎖定，與分項履約操作同步。承接不改變各項數量、開始時間或實收。
- 拒絕仍須原因；已有實際服務開始或完成的項目不能以整單拒絕抹去。小費的自動分配完成不等同實際服務。
- 拒絕時於同一交易取消各履約項目、增加版本、同步指名／加購／包廂狀態、釋放忙碌區段、失效預約通知，僅退回尚未退回的餐點折抵。原訂單報價保留作歷史紀錄；與舊流程相同，`rejected` 訂單不計入應收。
- 拒絕不建立現金退款或實收；已存在款項仍由既有帳款流程核對。訂單鎖與待承接條件避免重複退回折抵。
- 每項拒絕事件使用獨立操作編號，符合事件表 `(OPERATION_ID, EVENT_TYPE)` 的唯一限制；操作原因保留原本最多 500 字。

## 本地驗證

```powershell
dotnet build ToBeClarify.Api.csproj --configuration Release
dotnet run --project tests/StoreConfirmationContracts/StoreConfirmationContracts.csproj --configuration Release
dotnet publish ToBeClarify.Api.csproj --configuration Release --no-build --output bin/store-confirmation-publish
```

回歸檢查只在記憶體核對實際使用的聚合與拒絕規則，不使用資料庫、HTTP 或憑證；不加入 `dev` 部署預設測試關卡。尚未在真實資料庫執行承接／拒絕交易，不能將這些檢查稱為正式營運操作驗收。

API 只有 `main` 的正式主機。發布必須使用正常 API PR／合併與部署流程；Web 的本地改動仍依 Web `dev` → 使用者確認 → `main` 流程處理。
