# 顧客 UID、找回碼與作品交付 API

本文件對應 2026-09-22 的 UID-only 調整。程式已完成本機 Release build，但 migration、真實資料庫套用與部署仍是獨立步驟。

## 身分與用途

- `GAME_ID` 是店員輸入的顧客遊戲 ID，保留在每次入場 session；它是查找線索，不是授權資料。
- `C-` 加 16 個 hex 字元是穩定顧客 UID。UID 用於歷史歸戶、CRM 統計、UID 作品查詢與 UID 留言；知道 UID 即可使用低強度的持有式功能，因此前台不把 UID 當成高強度身分驗證。
- 找回碼是某次入場 session 的短期點餐恢復碼，只服務原有當日點單找回流程。它不接受作品查詢、不接受留言圖片驗證，也不延長有效期。
- 作品領取碼是單筆作品的獨立短期領取碼；API 只保存雜湊，發行或重發時只回傳一次明文。
- `CUSTOMER_PROFILES.CREDENTIAL_HASH` 只為相容舊 schema 保留；新 API 不發行、不驗證、不回傳舊版顧客私密憑證。

## UID 歸戶規則

建立入場 session 時，API 以正規化後的遊戲 ID 查找已連結的 UID：沒有候選時建立新 UID；只有一個候選時沿用；多個候選時不自動合併，留給後台核對。管理員可在建立 session 時傳入 `customerUid` 明確歸戶，或由歷史顧客頁補綁；每次連結都寫入 `CUSTOMER_DELIVERY_AUDIT`。

遊戲 ID 正規化為 trim、連續空白折疊、轉大寫。它不建立唯一索引，也不會把同名或同 ID 的不同顧客強制合併。

## API contract

所有 JSON 使用既有 `{ success, data }` envelope，欄位為 camelCase；所有顧客端回應 `Cache-Control: no-store`。

| Method | Route | 輸入／回傳 | 權限 |
|---|---|---|---|
| GET | `/api/admin/customer-history` | `businessDate?`, `search?`, `page`, `pageSize` → 歷史 session、訂單、UID、找回碼狀態 | AdminOnly |
| GET | `/api/admin/customers/{uid}` | UID 摘要與歷史來店時間軸 | AdminOnly |
| GET | `/api/admin/customer-identity/candidates` | `gameId` → UID 候選與最近來店、來店次數、訂單摘要 | AdminOnly |
| POST | `/api/admin/order-sessions/{sessionId}/customer-profile` | `{customerUid?}` → 顧客 profile；連結既有 UID 需管理員 | AdminOnly / Manager for existing UID |
| POST | `/api/admin/order-sessions` | `{gameId, customerName?, customerUid?, maxNominatedStaff?}` → 新 session 與當次點餐資料；`customerUid` 為管理員明確選擇 | AdminOnly |
| POST | `/api/admin/art-deliveries` | `{sessionId,orderId?,orderItemId?,title,description?,dueDate?}` → delivery 與單筆領取碼 | AdminOnly |
| POST | `/api/admin/art-deliveries/{id}/reissue-code` | 空 body → 新單筆領取碼 | AdminManager |
| GET | `/api/admin/art-deliveries` | session、狀態、關鍵字與分頁 → 交付清單 | AdminOnly |
| POST | `/api/client/art-deliveries/lookup` | `{claimCode}` 或 `{customerUid,page?,pageSize?}` → 公開交付 DTO | 公開、限流 |
| POST | `/api/client/art-deliveries/{id}/acknowledge` | 同 lookup 輸入 → 已確認交付 DTO | 公開、限流 |
| GET | `/api/client/art-deliveries/{id}/assets/{assetId}` | `X-Delivery-Code` 或 `X-Customer-Uid` → PNG | 公開、media 限流 |

找回碼不出現在作品 API 的輸入型別。前台作品頁 `/collection` 只有「單筆領取碼」與「顧客 UID」兩種入口；領取碼可用 URL fragment 帶入，讀取後立即清除，不放 query/path。

## 留言 API

匿名文字留言仍可建立；UID 文字留言可建立；訪客附圖必須提供有效 `CustomerUid`。店員由登入後台發言時可直接附圖，不需要顧客 UID。舊版顧客私密憑證與找回碼都不接受作為留言圖片驗證。

Web 在瀏覽器端將單張 JPEG／PNG／WebP 壓縮成 WebP 後送出；API 仍重新檢查格式、大小與畫素，重編碼並移除 metadata。公開留言／回覆／單筆查詢不回傳 `customerUid`；管理端可查看 UID 關聯、圖片及既有稽核資料。隱藏留言或整串時，圖片讀取端點也會拒絕公開存取。

點讚以匿名瀏覽器識別碼去重，原始識別碼由 Web/API 簽章後以 HMAC 形式保存；它只限制同一瀏覽器識別碼重複點讚，不代表登入身分或跨裝置的一人一票。回覆分享連結由 Web 提供單筆回覆查詢，並先檢查所屬留言串及回覆是否公開。

## 找回碼狀態

`CUSTOMER_ORDER_SESSIONS` 保留 `RECOVERY_CODE_HASH`，新增 `RECOVERY_CODE_ISSUED_AT` 與 `RECOVERY_CODE_VERSION`。建立或重新發行找回碼時更新發行時間與版本；存取點餐 token 只旋轉 session access token，不改找回碼版本。歷史顧客頁只顯示「已建立／未建立」與版本，不顯示明文。

## 資料庫與發布

依序套用原留言板 migration、`20260921_01_customer_commissions.sql`、`20260921_02_guestbook_identity_media.sql`、`20260922_01_customer_identity_uid_only.sql`、`20260923_01_guestbook_likes.sql`。最後一支 likes migration 建立匿名點讚資料表。migration、真實資料庫套用及部署皆須另行安排；本次開發沒有連線或修改任何環境資料庫。

作品附件仍獨立保存，未發布前顧客只能看到進度；`ready`／`delivered` 才能讀取附件。委託不改變原訂單、付款、離場或結算狀態。

本機驗證執行 API build、Web TypeScript 檢查及留言板相關 lint；尚未連線真實 DB、套用 migration、部署或執行自動化測試套件。
