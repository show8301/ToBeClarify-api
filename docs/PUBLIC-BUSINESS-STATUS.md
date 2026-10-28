# 首頁營業狀態

`GET /api/client/home` 的 `data.businessStatus` 提供 `state`（`open` 或 `closed`）、`startsAt`、`projectedCloseAt` 與 `checkedAt`。時間帶為台北時間；關店時兩個營業時間為 null。狀態以實際營業期間的開關店狀態為準，預定時間和測試營業日覆寫不會自動改變公開狀態。超過預計關店時間後仍以手動關店為準。

`SITE_SETTINGS` 的 `shopInfo.businessStatus` 是舊版首頁的人工文字，保留供尚未切換的使用端相容；新版首頁不讀取它。後台不再提供此欄位編輯。`shopInfo.openHours` 仍是人工維護的固定營業時間說明。全部使用端切換並確認後，另行審核及備份，再依資料庫權限規範處理舊 JSON 欄位；此版本不執行資料移除。
