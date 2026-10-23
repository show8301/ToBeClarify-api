# 薪資派發標記

## 範圍

這是經理／開發者的人工「已給／未給」紀錄，不是付款帳本。不建立
`SETTLEMENT_PAYMENT_EVENTS`、`SETTLEMENT_CORRECTION_EVENTS`，也不改薪資公式、
出勤或結算狀態。舊的支付／追回紀錄不會自動轉成派發標記。

## 部署前置

1. 備份並依正式 DB 變更流程套用
   `db/migrations/20261024_01_settlement_salary_dispatch.sql`。
   此腳本只建立新表，可重複執行；不要重建或清空原有表。
2. 確認 API 執行帳號可 SELECT／INSERT／UPDATE 新表，以及寫入既有稽核表。
3. 部署 API，再發布使用新端點的 Web。未部署 Web 的舊前端不受新增契約影響。

開發期間不應為驗證此功能，在共用正式 DB 套表或切換真實薪資標記。

## API

- `GET /api/admin/settlement/salary-dispatch?businessDate=2026-10-24&sessionNo=1`
- `PUT /api/admin/settlement/salary-dispatch`：成功回傳 204，無 body。
- 兩個端點均套用 `AdminManager`，只允許 manager／developer。
- 更新 body：`businessDate`、`sessionNo`、`settlementId`、`staffId`、`isPaid`、
  `expectedVersion`、`expectedCorrectionVersion`、`expectedInputVersion`、`operationId`。
  所有欄位必填；`operationId` 為 UUID，同一請求重送必須沿用相同 ID。
- 沒有結算或尚未正式結算：讀取回傳 `finalized=false`、空清單，且不建立結算。
  更新回傳業務錯誤。

## 資料與一致性

- 新表主鍵是 `(SETTLEMENT_ID, STAFF_ID)`，以保存的結算 ID 隔離日期／時段，
  不根據操作當下的日曆日或目前開店時段保存。
- 清單合併該次已保存計薪輸入、薪資結果及既有標記的人員；不讀全體在職名單。
  薪資結果、人員來源及更新資格均排除 `ANOMALY_STATUS='stale'` 的失效結果，
  與個人帳目採相同條件。同一人的有效薪資結果以 `SUM(AFTER_ROUNDING)` 合併；停用人員仍可顯示。
  重新結算移除的人員若已有標記，仍保留列及標記，最新薪資顯示 0。
- 未曾登記者預設未給、版本 0；不批次寫入預設資料。
- 狀態可雙向切換；每次接受的操作增加版本並保存操作人、時間。
  稽核表保存前後值，action 為 `salary_dispatch_changed`，不刪除歷史。
- 重新結算不重設派發標記，不建立補發、追回或差額案件。
- GET 用一致性快照讀取結算、薪資與標記。
- PUT 鎖定對應結算列，與重新結算序列化，確認正式狀態及結算版本、個人標記版本。
  任一版本不符回傳 `SETTLEMENT_DISPATCH_VERSION_CONFLICT`（409），要求重新讀取。
- `operationId` 以稽核表主鍵去重；僅相同結算、操作者與原始請求可視為重送成功。
  改變請求內容但沿用 ID 回傳 `SETTLEMENT_OPERATION_CONFLICT`。

## 人工驗收方向

請使用隔離的驗收 DB／資料，或由業務方明確授權的正式派發操作；不要對他人薪資試點。

- 未結算不提供標記；純讀取不新增結算資料。
- 多角色薪資合併、零薪資、停用員工，以及空清單。
- 同一人同時有有效及 stale 結果時，只加總有效結果；僅有 stale 結果且無計薪輸入／
  既有標記者，不出現在清單，也不能登記派發。
- 標記已給／改回未給後重整，檢查操作者、時間與稽核紀錄。
- 不同營業日、同日不同時段，以及跨午夜營業互不污染。
- 同一請求重送不重複登記；兩位經理從相同版本送出，後送者收到衝突。
- 重新結算更新薪資但保留標記；舊畫面不能送出過期的結算版本。
- clerk／未登入者不能讀取或更新新端點。
- 逾時後先讀回狀態，不盲目重送；其他支付及更正帳本不變。
