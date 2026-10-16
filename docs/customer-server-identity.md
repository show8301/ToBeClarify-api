# 角色伺服器識別 API 契約

日期：2026-10-16。此變更不需要 migration，不覆寫既有 `GAME_ID` 或 UID。Web 的完整切換說明位於相鄰 `ToBeClarify-web/docs/customer-server-identity.md`。

## 請求

`POST /api/admin/order-sessions` 與 `POST /api/client/ordering/recover` 增加可選 `serverName`（上限 16 字元）；`gameId` 保留原欄位。新 Web 以人物名稱及伺服器分開送出，API 組成 `人物ID@伺服器` 後儲存／查詢。完整 `gameId` 的輸入亦可接受；與獨立 `serverName` 矛盾時拒絕。

`GET /api/admin/customer-identity/candidates` 支援同名可選 query。完整 ID 查詢只匹配完整 ID，不含未帶伺服器的舊紀錄。回應 `gameId` 是 API 組成的完整值；其餘回應形狀及 UID 權限維持不變。

七個伺服器：伊弗利特、利維坦、奧汀、巴哈姆特、泰坦、迦樓羅、鳳凰。別名「奧丁」統一為「奧汀」；名稱內全形 `＠` 可作分隔符，名稱空白統一後完整長度不得超過 100。現有大小寫不敏感比對保留。

## 相容與錯誤

- `OrderingIdentity:RequireServerForNewSessions=false` 是預設切換期設定。舊 Web 裸 ID 新建仍接受，但不自動建立／沿用 UID，保持未歸戶供人工核對。手動指定既有 UID 仍須經理／開發者。
- 新 Web 必選伺服器；正式 Web 全面更新且經使用者確認後，設定 `OrderingIdentity__RequireServerForNewSessions=true`，使所有新建身分必須帶伺服器。回退舊 Web 前先恢復 `false`。
- 找回永遠保留裸 ID 契約供舊紀錄使用。新 Web 顯式切換舊資料模式才使用裸 ID；完整 ID 查無資料時絕不自動 fallback。
- 既有 session 的 `GAME_ID` 不變，因此原長 token、短網址、找回碼及 UID 作品入口不需重發。
- 新格式零／一／多 UID 候選分別建立、沿用、保持未歸戶。經理可於開碼前指定經核對的舊 UID；已綁定紀錄仍不能直接改綁。
- 併發重複開碼在營業期鎖內再檢查，回傳既有 `ORDER_SESSION_EXISTS`。
- 新錯誤碼：`GAME_ID_INVALID`、`GAME_ID_TOO_LONG`、`GAME_SERVER_REQUIRED`、`GAME_SERVER_INVALID`、`GAME_SERVER_MISMATCH`，均為 HTTP 400。

先發布相容 API，再部署 Web dev；經確認後由 Web dev → main 推進，最後收緊 API 設定。API 的 dev 分支僅 build，不是測試主機。建議在沒有進行中營業期時切換；本次不自動修正已混合顧客的歷史 UID。
