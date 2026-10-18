# 帳款經手店員顯示名稱

`GET /api/admin/ordering-finance/sessions/{sessionId}` 的 `records` 新增可為 null 的 `createdByDisplayName`。它以原 `createdBy` 帳號 ID，批次查詢既有 `ADMIN_USERS.STAFF_MEMBER_ID` 與 `STAFF_MEMBERS.DISPLAY_NAME`；顯示目前店員名稱，不宣稱為交易發生時的名稱快照。停用帳號仍可解析，帳號或店員資料不存在時回傳 null。

原帳號 ID、帳務金額、寫入流程與既有讀取授權保留；沒有新增帳號名單權限或端點。此欄位僅補充目前帳款查詢，既有 revision JSON 不回填名稱。不需要資料庫 migration。

Web 相容舊版 API：欄位缺少時，若經手帳號與登入者相同則使用登入者顯示名稱；其他紀錄顯示「名稱尚未取得」，不將帳號 UID 當成名稱。完整跨店員顯示需發布此 API 增量變更。API 仍依 main 的一般正式發布流程；Web 依 dev 驗收後再推進 main。
