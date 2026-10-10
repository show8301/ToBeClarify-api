# 指名人員營業工作台 API

更新日期：2026-10-10。變更基於正式 API `f41d034`，沿用既有後台 Cookie 驗證、訂單與履約交易。

## 本人營業顧客

`GET /api/admin/designated-order-sessions?businessDate=YYYY-MM-DD` 回傳既有 `AdminOrderSessionDto` 清單。營業日省略時使用 API 的目前營業日。

範圍由登入者的店員綁定決定，不接受客戶端指定其他店員。未綁定店員回傳 `DESIGNATED_STAFF_REQUIRED`。只包含該營業日與本人指名／加購相關的入場紀錄，以及轉入該營業期的本人履約項目；不透過顧客 UID 歸戶帶入其他跨日歷史。

## 本人承接回應

`POST /api/admin/orders/{orderId}/nominees/{nomineeId}/response` 接收：

```json
{
  "operationId": "唯一的 UUID",
  "expectedVersion": 1,
  "decision": "decline",
  "reason": "無法承接的原因"
}
```

`decision` 為 `accept` 或 `decline`。拒絕須有原因，最多 500 字；只有該筆指名的本人能回應，即使登入者兼具經理／開發者角色，也不能代替其他指名人員回應。店家尚未承接的協調單不能直接進入本人確認。

成功回傳更新後的既有 `OrderDto`。Flow v2 使用既有履約交易、版本檢查與 operation ID；同一操作的重試沿用原 operation ID。Flow v1 沿用原確認邏輯，拒絕則鎖定訂單及指名資料後更新，已拒絕的重試不重複寫入。

拒絕將該筆指名／履約設為 `needs_coordination`，交回經理。沒有其他待服務或服務中項目的訂單顯示 `needs_reschedule`；Flow v2 若有其他服務則維持對應聚合進度。拒絕不取消整張顧客訂單、不改變購買數量、金額或折抵，也不退款。舊版等待確認自動失效排除已交回協調的訂單，避免之後取消其他服務。

交回後，指名人員不能自行重新承接或開始。經理可依既有履約端點重新協調時間、保留／跨期或取消未服務項目；改期後才再次開放本人確認。

## 經理建立承接請求

既有代客報價及送單 request 增加選用的 `isManagerTransfer`，預設 `false`。

設為 `true` 時，只允許經理／開發者透過後台建立新的指名訂單；請求只能包含指名，不能混入餐點、包廂或小費。顧客端及非管理角色不能使用此旗標。經理已決定店家承接，因此跳過店家再次確認，但仍等待指定人員本人回應。

訂單歷史使用 `manager_transfer` 標記來源。此流程建立新請求，不把既有指名從 A 改派給 B。

## 相容性與發布

- 未傳 `isManagerTransfer` 的既有報價、顧客送單與代客訂購維持原流程；只有轉單報價才增加此旗標至報價內容，保留既有正常報價相容性。
- 原端點及回傳資料結構保留；新工作台需要本次新增端點，舊版 Web 不需同步上版。
- 使用既有資料表與可容納新狀態的字串欄位，本次沒有 schema 或 migration 變更。
- API 由 `main` 的 CI/CD 部署至唯一正式主機；`dev` 僅做建置。部署腳本保留正式設定、媒體與日誌，健康檢查失敗時回復舊檔案。
- 部署後確認 API 健康、新端點出現在 Admin Swagger，且匿名存取資料端點回傳 401。這些檢查不等同於已驗證登入後的完整交易流程。
