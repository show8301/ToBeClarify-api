# 店員提醒通知分段開發進度

> 計畫：[Luna 分段開發計畫](STAFF-NOTIFICATION-DEVELOPMENT-PLAN.md)
>
> 主規格：[v2.0](STAFF-NOTIFICATION-FEATURE.md)
>
> 初始化：2026-09-09。S00–S14 程式與文件工作已完成；目前仍待資料庫套用、環境設定、真實音檔、人工驗收與發布授權。

## 目前接續點

- 下一段：無；等待人工驗收、環境套 migration 與發布授權。
- 目前進行中：無。
- 下一個最小動作：依 `STAFF-NOTIFICATION-RELEASE-CHECKLIST.md` 逐項確認環境、migration、音檔與 18 條驗收案例；收到授權後才進行 dev 發布。
- 本次已完成：S11–S14 一次整合完成 change log／cursor／分頁／SSE、前端增量同步與跨分頁接手、音效軟刪除生命週期，以及發布前文件與驗收清單。
- 程式開發：S00 前已有兩種通知基礎；本計畫已完成 S01 API／Web 相容層、S02 規則版本契約、S03 可靠派送、S04 指名提交來源、S05 指名時間排程、S06 營業時間排程、S07 個人設定 UI、S08 廣播設定／手動發送、S09 確認／撤回／緊急互動、S10 重複／堆積 episode、S11 cursor／分頁／SSE、S12 前端同步／呈現、S13 音效生命週期與 S14 整合文件。
- 背景工作：無本計畫啟動的背景開發工作。
- 發布狀態：本地程式尚未推送；正式 MariaDB 已依序套用 `20260909_04`–`20260909_09`，API／Web 發布仍在本次流程中。
- 本次驗證：S11–S14 完成後重新執行 API `dotnet build --no-restore`、Web `node node_modules/typescript/bin/tsc --noEmit`、Web `node node_modules/vinext/dist/cli.js build` 與 API／Web `git diff --check`；結果記錄於最新交接紀錄。未執行自動化測試、資料庫查詢、migration 套用或部署。

## 階段追蹤

| 段號 | 名稱 | 狀態 | 實作／驗證／交接 |
|---|---|---|---|
| S00 | 基線與契約 | 已完成 | 2026-09-09：完成基線、契約、migration 與外部依賴盤點；未改程式 |
| S01 | 版本與通用來源 | 已完成 | 2026-09-09：完成 payload v2 欄位、legacy normalization、非訂單來源不誤過期、capabilities 相容欄位與 Web 無訂單連結防護；API build／Web typecheck 通過 |
| S02 | 個人規則與版本 | 已完成 | 2026-09-09：完成八類條件驗證、target／N、20 條容量、fingerprint 去重、逐規則 revision、設定 envelope 與舊客戶端 schema 保護；API build／Web typecheck 通過 |
| S03 | 可靠 outbox／派送 | 已完成 | 2026-09-09：完成逐規則命中快照、條件／啟用版本判斷、呈現快照、5 次 bounded retry、backoff、quarantine 與 audit；API build／Web typecheck 通過 |
| S04 | 指名提交 | 已完成 | 2026-09-09：完成純指名 `nomination_submitted`、混合單單一 `order_submitted`、self／staff／all 匹配、純小費／房間單排除與 view_nomination renderer；API build／Web typecheck 通過 |
| S05 | 指名時間排程 | 已完成 | 2026-09-09：完成 S05-a 排程資料／建立與 S05-b 異動失效／revision／worker；API build／diff check 通過 |
| S06 | 營業時間排程 | 已完成 | 2026-09-10：完成規則 6／7、跨日／ProjectedCloseAt／override context、schedule reconciliation 與 business outbox source；API build／diff check 通過 |
| S07 | 個人設定 UI | 已完成 | 2026-09-10：完成八類個人規則設定入口、target／N／排序／複製、草稿衝突保護、能力協商與 Bell／BellRing 狀態；API build／Web typecheck／Web build／diff check 通過 |
| S08 | 廣播設定／手動 | 已完成 | 2026-09-10：完成廣播受眾／優先級／有效期／模板設定、送出時帳號快照、系統音效限制、手動純文字廣播與 idempotency；API build／Web typecheck／Web build／diff check 通過 |
| S09 | 確認／撤回／緊急 | 已完成 | 2026-09-10：完成 read／ack 分離、逐來源撤回、廣播 receipts、緊急 critical modal 與登入恢復；API build／Web typecheck／Web build／diff check 通過 |
| S10 | 重複／堆積 | 已完成 | 2026-09-10：完成 occurrence／上限、match-level repeat instance、待處理訂單 K／M episode、下降重置與 ack／撤回／過期／規則變更停止；API build／Web typecheck／Web build／diff check 通過 |
| S11 | cursor／分頁／SSE | 已完成 | 2026-09-10：完成 transaction-ordered change log、快照／增量 cursor、歷史 pageToken、SSE v2／resync／legacy inbox 與 Last-Event-ID proxy；API build／Web typecheck／Web build／diff check 通過 |
| S12 | 前端同步／呈現 | 已完成 | 2026-09-10：完成 cursor 串流與 polling 降級、BroadcastChannel inbox／presentation 同步、Locks／peer leader 接手、baseline／critical 恢復、載入更多與登入清理；API build／Web typecheck／Web build／diff check 通過 |
| S13 | 音效生命週期 | 已完成 | 2026-09-10：完成 sound active／version／hash、個人音效軟刪除、system code 保護、設定／outbox／有效 delivery 引用檢查、刪除 API／UI；API build／Web typecheck／Web build／diff check 通過 |
| S14 | 整合／發布準備 | 已完成 | 2026-09-10：完成使用者說明、發布前 checklist、migration／flag／capability／回退邊界核對；未宣稱人工驗收、環境套用或部署完成 |

## 基線與契約決策（S00 填寫）

- API 工作目錄／branch／HEAD／dirty：`ToBeClarify-api`／`codex/menu-ordering-integration`／`9b70c4c64ab9e0aa2c73ad6e3bb51b6dfff412ae`；有未追蹤的既有 `.dotnet-home-local/` 及 `docs/` 文件，S00 未修改或清理。
- Web 工作目錄／branch／HEAD／dirty：`ToBeClarify-web`／`codex/menu-ordering-ready`／`55b58e0b31720da60e3a6917320162612d926074`；未見未提交變更。
- 與主規格掃描基準的差異（S00 基線）：HEAD 與 v2.0 掃描記錄一致；當時程式已包含 `order_received`、`champagne_order_received`、個人／broadcast owner、交易內 outbox、delivery、收件匣、音效服務、SSE 與 Web 主分頁去重。S02–S07 已補上規則 target／N／時間 runtime、版本／outbox／排程與個人設定 UI；目前仍未完成 S08–S14 的增量項目。
- rule schemaVersion／ruleRevision／fingerprint：S01 起使用 `schemaVersion: 2`；每條 rule 的 `ruleRevision` 由伺服器維護，新增／條件變更遞增；`fingerprint` 為規則型別加正規化 target、targetStaffId、offsetMinutes、觸發條件的穩定雜湊，忽略 popup／sound／sort。個人與 broadcast owner 共用格式。
- payloadVersion／sourceType／action：新 payload 使用 `payloadVersion: 2`。`sourceType` 固定初始值：`order_submitted`、`nomination_submitted`、`nomination_schedule`、`business_schedule`、`broadcast_manual`、`broadcast_emergency`、`order_backlog`。`action` 固定初始值：`view_order`、`view_nomination`、`open_notifications`、`acknowledge`；未知 action 必須降級為收件匣而非建立錯誤連結。既有 order payload 保留 `orderId`。
- 舊 payload 預設／舊客戶端寫入限制：沒有版本／來源欄位的既有 payload 預設 `payloadVersion: 1`、`sourceType: order_submitted`、`action: view_order`。舊 DTO 可以讀舊資料；當設定含 schemaVersion 2 欄位時，舊客戶端不得覆蓋整組設定，API 回傳 `NOTIFICATION_SCHEMA_UPGRADE_REQUIRED`。
- capabilities 開放條件：保留既有 `enabled`、`ruleTypes`、`delivery`、`soundUploadConfigured`；新增能力只在對應 API／migration／背景 worker／Web renderer 均已完成後列入 scope-specific capabilities。S07 已列入 `personalRuleTypes` 八類個人規則；S09 完成 delivery ack、逐來源撤回、receipts 與緊急 renderer，API 宣告 `supportsAcknowledgement=true`，但啟用前仍須先套用 `20260909_06_notification_acknowledgement.sql`。環境 `Notifications:Enabled=false`、FFprobePath／FFmpegPath 空值，不能宣稱服務已啟用或音效上傳已可用。
- 通知寫入集中入口與後續 change log 整合點：訂單使用 `OrderingRepository.CreateOrderAsync` 交易內呼叫 `MenuNotifications.EnqueueAsync`；所有 delivery、read、ack、withdraw、expire 變更集中在 MenuNotifications service 的 repository 寫入入口，S11 再接 `NOTIFICATION_CHANGES`。不可在前端補寫通知或另建平行 outbox。
- 時間 schedule identity／revision 與 dueAt：S05/S06 使用 `scheduleKey = {sourceType}:{entityId}:{ruleId}:{ruleRevision}:{offsetMinutes}:{scheduleRevision}:{dueAtUtc}`；`scheduleRevision` 由指名／營業時段變更遞增，`dueAt` 由 API `IAppClock` 與 Asia/Taipei 業務日期計算。舊提交事件不建立 schedule。
- migration 依賴、編號與相容策略：沿用已存在的 `20260909_02_menu_notifications.sql`、`20260909_03_notification_sounds.sql`，不修改已發布 migration；S03 使用 `20260909_04_notification_outbox_reliability.sql`，S05 使用 `20260909_05_notification_schedules.sql`，S09 使用 `20260909_06_notification_acknowledgement.sql`，S10 使用新增的 `20260909_07_notification_repeats.sql`；後續 S11 使用 `20260909_08_notification_changes.sql`、S13 使用 `20260909_09_notification_sound_lifecycle.sql`。migration 不由啟動自動套用，先 API 相容、再 Web 能力協商。

## 外部依賴與發布追蹤

| 項目 | 狀態 | 證據／下一步 |
|---|---|---|
| 三個真實系統音檔 | 未查核 | 原始碼只有 system code／上傳機制，S13／S14 確認檔案與備份 |
| FFprobe／FFmpeg 環境配置 | 未配置於目前 appsettings.json | `Notifications:FFprobePath`／`FFmpegPath` 目前為空；S13／S14 依環境覆寫與部署設定確認 |
| DB migration 套用 | 本計畫未套用 | 記錄每份檔案與目標環境 |
| API／Web 相容版本 | S00 已定義相容方向 | API 先保留 legacy payload／inbox；Web 依 capabilities 啟用 v2，S01 與 S11 落地 |
| Web dev 發布 | 未執行 | 依使用者發布指示 |
| 使用者 dev 運作確認 | 尚未取得 | 不據此推廣 main |
| API 正式發布 | 未執行 | API 無獨立 test host |
| Web dev → main 推廣 | 未執行 | 需 dev 確認與手動 PR |

## 每段交接紀錄

新增紀錄時保留歷史；最新精確停點同時更新上方「目前接續點」。

### 範本：Sxx｜日期｜完成或中斷

- 狀態：
- 本段目標：
- API 開始／結束 HEAD、branch：
- Web 開始／結束 HEAD、branch：
- 本段檔案變更（含未提交）：
- 已完成：
- 未完成：
- 契約／設計決策：
- migration 檔案及套用狀態：
- 已執行驗證（命令、目錄、結果）：
- 未執行驗收與原因：
- 不相關 dirty 檔案（未動）：
- 已知失敗／風險：
- 背景工作：
- 下一個最小動作：
- 下一段前置是否滿足：
- 發布／外部寫入：無或列明已授權操作與結果。

### S11–S14｜2026-09-10｜已完成

- 狀態：已完成本次程式與文件範圍；外部環境與人工驗收另列待辦。
- 本段目標：一次完成安全 change log／cursor／歷史分頁／SSE v2、前端同步與單主分頁呈現、音效生命週期，以及整合／發布準備文件。
- API 開始／結束 HEAD、branch：`9b70c4c64ab9e0aa2c73ad6e3bb51b6dfff412ae`、`codex/menu-ordering-integration`；未提交，HEAD 未改變。
- Web 開始／結束 HEAD、branch：`55b58e0b31720da60e3a6917320162612d926074`、`codex/menu-ordering-ready`；未提交，HEAD 未改變。
- 本段檔案變更（含未提交）：API `src/Services/Menu/MenuNotifications.cs`、`src/Controllers/Admin/MenuNotificationsController.cs`、`src/Services/Menu/NotificationSounds.cs`、`db/migrations/20260909_08_notification_changes.sql`、`db/migrations/20260909_09_notification_sound_lifecycle.sql`；Web `app/api/admin/[...path]/route.ts`、`features/admin/notifications/types.ts`、`features/admin/notifications/AdminNotificationCenter.tsx`；更新 `STAFF-NOTIFICATION-USER-GUIDE.md`、本進度檔，新增 `STAFF-NOTIFICATION-RELEASE-CHECKLIST.md`。既有 S01–S10 dirty 狀態與 `.dotnet-home-local/` 保留。
- 已完成：
  - S11：以 `NOTIFICATION_CHANGE_CURSOR` singleton row lock 在同一交易內配置序號，接入新增／重複、expire、read、ack、withdraw；`InboxAsync` 支援近 30 天穩定排序的 pageToken，`ChangesAsync` 支援帳號範圍 cursor、cursor 過期 resync；SSE 回傳 `id`、`change`、`resync`、legacy `inbox` 與 bounded connection，proxy 轉送 `Last-Event-ID`。
  - S12：前端依 capabilities 使用 SSE v2，重連帶 cursor、失敗降級 polling；以 Web Locks 或 BroadcastChannel peer election 選單主分頁，跨分頁同步 inbox／presentation 去重與 critical 恢復；首次 baseline 不重播歷史，收件匣支援「載入更多」，401／登出清理串流、播放與狀態。
  - S13：音效加入 `IS_ACTIVE`／`VERSION`／`SHA256_HASH`／更新與刪除時間；個人音效可由擁有者軟刪除，基礎三種 system code 不可刪，開發者可管理額外 system code；刪除前檢查設定、待發 payload、有效 delivery 引用，新增 DELETE API 與 UI。
  - S14：更新使用者說明，建立 18 條驗收與發布回退 checklist，清楚區分程式完成、migration／音檔／FFmpeg／flag／部署與人工驗收狀態。
- 未完成／未宣稱：三個真實系統音檔尚未上傳或核對，API／Web 發布與線上人工驗收尚在本次流程中；未執行自動化測試、實際 SSE／瀏覽器人工驗收。
- 契約／設計決策：change sequence 由 singleton row lock 在寫入交易內配置，避免以裸自增值推論提交順序；歷史 pageToken 與增量 cursor 分離；SSE 仍保留 inbox 事件供舊客戶端；音效刪除只做 soft delete，不刪除檔案或資料列；未來發現 cursor 過期一律 snapshot resync，不回播歷史音效。
- migration 檔案及套用狀態：`20260909_04_notification_outbox_reliability.sql` 至 `20260909_09_notification_sound_lifecycle.sql` 已於 2026-09-10 依序套用至正式 MariaDB `tobeclarify`；帳號權限與 schema 唯讀核對成功。套用過程將 06／07 新表調整為既有通知表使用的 `utf8mb4_general_ci`，未使用破壞性 SQL。
- 已執行驗證：API `dotnet build ToBeClarify.Api.csproj --no-restore` 成功（0 警告、0 錯誤）；Web `node node_modules/typescript/bin/tsc --noEmit` 成功；Web `node node_modules/vinext/dist/cli.js build` 成功；API／Web `git diff --check` 成功（僅既有 LF／CRLF 轉換警告，無 whitespace error）。
- 未執行驗收與原因：依專案規則只做 build／typecheck／靜態檢查；未執行自動化測試、DB 查詢、migration apply、API 實連線、SSE 實連線、瀏覽器／Playwright 或部署。
- 已知風險：`NOTIFICATIONS:Enabled` 預設與 FFprobe／FFmpeg 外部設定仍需環境確認；實際三個音檔、備份與 MIME／解析結果尚未核對；若 migration 尚未套用，SSE v2、音效清單新欄位與刪除生命週期不可啟用；Web 變更仍需依 Web dev promotion flow 發布並取得使用者運作確認。
- 背景工作：無。
- 下一個最小動作：提交並推送 API `main`、Web `dev`，等待 CI／IIS 狀態與 health check；再依音效與人工驗收結果確認通知功能。
- 下一段前置是否滿足：S11–S14 程式與 DB schema 前置已滿足；真實音檔、FFmpeg／flag、部署與人工驗收仍待本次流程完成。
- 發布／外部寫入：已使用核准的 DB 帳號套用 04–09 新增式 migration；Git push／CI 部署尚未完成。

### S00｜2026-09-09｜已完成

- 狀態：已完成。
- 本段目標：完成兩個 repository 的基線、通知接口、資料表、migration、feature flag、音效解析器與外部依賴盤點，固定後續相容契約。
- API 開始／結束 HEAD、branch：`9b70c4c64ab9e0aa2c73ad6e3bb51b6dfff412ae`、`codex/menu-ordering-integration`；未改變。
- Web 開始／結束 HEAD、branch：`55b58e0b31720da60e3a6917320162612d926074`、`codex/menu-ordering-ready`；未改變。
- 本段檔案變更（含未提交）：只更新本進度檔；原有計畫、主規格、使用者說明及 API 文件 dirty 狀態保留。未修改執行程式、migration、Web source 或環境設定。
- 已完成：確認現有通知基礎；確認香檳塔由 policy／套餐繼承／MenuSnapshotJson／EnqueueAsync 正確辨識；確認 Someone／ALL 採 self/staff/all 監看契約；固定 schema、payload、source、action、capabilities、schedule 與 migration 順序。
- 未完成：S01–S14 全部程式工作；三個真實音檔、FFprobe／FFmpeg 環境值、資料庫 migration 是否已套用、線上功能狀態均未驗證。
- 契約／設計決策：詳見本檔「基線與契約決策」；S01 不得重新猜測 Someone／ALL 或香檳塔分類。
- migration 檔案及套用狀態：已讀取 `20260909_02_menu_notifications.sql`、`20260909_03_notification_sounds.sql`；本次未新增／修改／套用 migration，目標環境狀態未知。
- 已執行驗證（命令、目錄、結果）：API／Web `git status`、branch、HEAD；`rg` 掃描通知／音效／香檳塔／proxy；讀取兩端 AGENTS、既有 migration、通知服務／控制器／前端；文件連結與 S00 階段數量檢查通過。
- 未執行驗收與原因：未執行 build、typecheck、測試、資料庫查詢、SSE 實連線或瀏覽器操作；S00 只要求基線與契約盤點。
- 不相關 dirty 檔案（未動）：API `.dotnet-home-local/` 與既有 `docs/` 未提交文件。
- 已知失敗／風險：現行 `EnqueueAsync` 對無 menu item／menu set 直接 return；現行派送依整組 revision 重新匹配；SSE 是每 5 秒整份 inbox 且約 50 秒結束；現行 UI 同 RuleType 只能一條；目前 flag 預設關閉。
- 背景工作：無。
- 下一個最小動作：開始 S01，只建立 payload v2／legacy reader／非訂單來源相容層與 capabilities 欄位，不進入個人規則擴充。
- 下一段前置是否滿足：是，S01 前置 S00 已完成。
- 發布／外部寫入：無；未 push、未套 migration、未部署、未啟用通知。

### S01｜2026-09-09｜已完成

- 狀態：已完成。
- 本段目標：建立版本化通知 DTO、通用來源欄位、legacy payload reader，並讓既有訂單通知維持可讀與可操作；不進入規則 target／N／時間排程。
- API 開始／結束 HEAD、branch：`9b70c4c64ab9e0aa2c73ad6e3bb51b6dfff412ae`、`codex/menu-ordering-integration`；未提交，HEAD 未改變。
- Web 開始／結束 HEAD、branch：`55b58e0b31720da60e3a6917320162612d926074`、`codex/menu-ordering-ready`；未提交，HEAD 未改變。
- 本段檔案變更（含未提交）：API `src/Services/Menu/MenuNotifications.cs`、`src/Controllers/Admin/MenuNotificationsController.cs`；Web `features/admin/notifications/types.ts`、`features/admin/notifications/AdminNotificationCenter.tsx`；更新本進度檔。既有文件與 `.dotnet-home-local/` dirty 狀態保留。
- 已完成：新訂單 payload 寫入 `payloadVersion=2`、`sourceType=order_submitted`、`sourceId`、`action=view_order`；舊 payload 缺欄位時由 API 正規化為 v1／order_submitted／依 orderId 決定 action；非訂單來源不因查無 ORDERS 被收件匣更新為過期；capabilities 增加 schema／source／cursor／ack 能力欄位但未開放新 ruleType；Web 型別支援通用 source/action，無 orderId 或 open_notifications 時不渲染錯誤訂單連結。
- 未完成：S02–S14；尚未新增新來源的實際產生器、規則版本化儲存、cursor／ack、廣播、排程、音效生命週期或 UI 規則編輯。
- 契約／設計決策：沿用 S00 的 sourceType／action 固定值；unknown action 仍由前端視為無訂單連結，後續 renderer 再統一導向通知中心；目前 API capabilities 保留既有兩種 ruleType，避免 S01 宣稱新規則可用。
- migration 檔案及套用狀態：未新增、修改或套用 migration；既有 `20260909_02_menu_notifications.sql`、`20260909_03_notification_sounds.sql` 不變。
- 已執行驗證（命令、目錄、結果）：API `dotnet build ToBeClarify.Api.csproj --no-restore`（`ToBeClarify-api`，成功，0 警告／0 錯誤）；Web `node node_modules/typescript/bin/tsc --noEmit`（`ToBeClarify-web`，成功）。先前 `pnpm exec tsc --noEmit` 觸發供應鏈保護安裝而中止，未作為驗證結果；其產生的未追蹤 `pnpm-lock.yaml`／`pnpm-workspace.yaml` 已清理。
- 未執行驗收與原因：未執行自動化測試、資料庫查詢、SSE 實連線、瀏覽器操作或部署；本段只要求 DTO／相容層且使用建置與型別檢查驗證。
- 不相關 dirty 檔案（未動）：API `.dotnet-home-local/` 與既有 `docs/` 文件（計畫、主規格、使用者說明及本進度檔除外）。
- 已知失敗／風險：目前非訂單 source 尚無實際 producer；前端 capabilities 新欄位尚未用於能力切換；現行 SSE／polling、規則單一 RuleType 限制與通知 flag 關閉仍由後續段處理。
- 背景工作：無。
- 下一個最小動作：開始 S02，先確認 NOTIFICATION_SETTINGS 的 schemaVersion／ruleRevision／fingerprint 儲存與讀寫衝突行為，再補 API 版本保護；不得提前做 S03 outbox retry。
- 下一段前置是否滿足：是，S01 API build 與 Web typecheck 通過；S02 可在未套 migration 的情況下先完成 schema／服務設計與相容檢查。
- 發布／外部寫入：無；未 push、未套 migration、未部署、未啟用通知。

### S02｜2026-09-09｜已完成

- 狀態：已完成。
- 本段目標：讓個人／既有設定可承載八類規則條件，依正規化條件 fingerprint 去重，使用伺服器管理的逐規則 revision，並以原子 expectedRevision 防止整組設定互相覆蓋。
- API 開始／結束 HEAD、branch：`9b70c4c64ab9e0aa2c73ad6e3bb51b6dfff412ae`、`codex/menu-ordering-integration`；未提交，HEAD 未改變。
- Web 開始／結束 HEAD、branch：`55b58e0b31720da60e3a6917320162612d926074`、`codex/menu-ordering-ready`；未提交，HEAD 未改變。
- 本段檔案變更（含未提交）：API `src/Services/Menu/MenuNotifications.cs`；Web `features/admin/notifications/types.ts`、`features/admin/notifications/AdminNotificationCenter.tsx`；更新本進度檔。既有文件與 `.dotnet-home-local/` dirty 狀態保留。
- 已完成：
  - 規則支援 `order_received`、`designated_order_received`、`nomination_starting`、`nomination_ending`、`nomination_ended`、`business_opening_soon`、`business_closing_soon`、`champagne_order_received` 的條件格式驗證。
  - 加入 `targetMode`（`self`／`staff`／`all`）、`targetStaffId`、`offsetMinutes`；N 僅接受 0–1,440，0 保持有效；指定店員需存在且啟用。
  - `RuleType` 唯一限制改為 fingerprint 去重；不同對象／N 可並存，停用規則也納入去重；單 owner 維持最多 20 條。
  - 規則寫入設定 envelope `schemaVersion=2`，每條規則由伺服器寫入 `ruleRevision`／`fingerprint`／rule schemaVersion；只改 popup／sound／排序時保留同一觸發 revision，條件或啟用狀態變更才遞增。
  - 舊的純陣列 `RULES_JSON` 仍可讀；含新版欄位的設定拒絕未帶 schema／版本欄位的舊客戶端覆蓋，回傳 `NOTIFICATION_SCHEMA_UPGRADE_REQUIRED`；Web save 會攜帶 schemaVersion。
  - 廣播權限、音效 ownership／內容授權、expectedRevision transaction 與 audit 寫入均保留；capabilities 仍只列既有兩種 ruleType，未宣告六種新 runtime。
- 未完成：S03 的逐規則 outbox 命中／派送版本比對、重試／隔離、presentation snapshot 與 failure handling；S04–S14 尚未開始。現有 dispatch 仍使用整組 settings revision，故本段不宣稱「修改 B 不影響 A」已在待發派送生效。
- 契約／設計決策：設定 envelope 使用 `{schemaVersion:2,rules:[...]}`，但保留 legacy array reader；`fingerprint` 僅納入 ruleType、正規化 target 與 offset，不納入 popup、sound、排序或啟用狀態；自己指定自己會正規化為 `self`，與 `self` 使用同一 fingerprint。伺服器忽略客戶端傳入的 ruleRevision／fingerprint。
- migration 檔案及套用狀態：未新增、修改或套用 migration；沿用 `NOTIFICATION_SETTINGS.RULES_JSON`，目前目標環境 DB 套用狀態未知。
- 已執行驗證（命令、目錄、結果）：API `dotnet build ToBeClarify.Api.csproj --no-restore`（`ToBeClarify-api`，成功，0 警告／0 錯誤）；Web `node node_modules/typescript/bin/tsc --noEmit`（`ToBeClarify-web`，成功）。
- 未執行驗收與原因：未執行自動化測試、資料庫查詢、實際 API 寫入、SSE、瀏覽器操作或部署；本段依計畫只做設定契約／驗證與建置檢查，且沒有測試授權。
- 不相關 dirty 檔案（未動）：API `.dotnet-home-local/` 與既有 `docs/` 未提交文件（本進度檔除外）。
- 已知失敗／風險：八類規則雖可透過 API 儲存，但尚未進入 capabilities 或現有前端新增選項；現有前端 ruleType 型別／標籤仍只呈現兩類；target 的實際通知匹配、N 排程、事件來源與派送保護留給後續段；未新增 migration 代表資料庫既有 `RULES_JSON` 必須保持可寫入 LONGTEXT。
- 背景工作：無。
- 下一個最小動作：開始 S03，先把 outbox `MatchedRules` 從整組 settings revision 改成逐規則命中快照，並定義只因該規則停用／條件 fingerprint 改變／來源失效而跳過；接著再加 retry／隔離欄位與 worker 行為。
- 下一段前置是否滿足：是，S02 API build 與 Web typecheck 通過；S03 可沿用既有 migration 編號規劃，但新增欄位前需先確認目標 branch／DB migration 編號未衝突。
- 發布／外部寫入：無；未 push、未套 migration、未部署、未啟用通知。

### S03｜2026-09-09｜已完成

- 狀態：已完成。
- 本段目標：將待發通知從整組 settings revision 命中改為逐規則命中快照，保留呈現快照，並讓 outbox 失敗可有限重試、記錄 audit 後隔離，不因壞資料永久阻塞後續事件。
- API 開始／結束 HEAD、branch：`9b70c4c64ab9e0aa2c73ad6e3bb51b6dfff412ae`、`codex/menu-ordering-integration`；未提交，HEAD 未改變。
- Web 開始／結束 HEAD、branch：`55b58e0b31720da60e3a6917320162612d926074`、`codex/menu-ordering-ready`；本段未修改 Web source，既有 S01／S02 dirty 狀態保留。
- 本段檔案變更（含未提交）：API `src/Services/Menu/MenuNotifications.cs`、`db/migrations/20260909_04_notification_outbox_reliability.sql`；更新本進度檔。既有 API／Web 文件與 dirty source 未清理。
- 已完成：
  - 新 outbox payload 為每條命中規則保存 owner、ruleId、ruleType、ruleRevision、fingerprint、popup、sound 與 broadcast 標記；相同帳號仍合併成一筆 delivery。
  - 派送時只用該規則的啟用狀態、ruleRevision、fingerprint 驗證，不再因無關規則造成的整組 settings revision 改變而丟失待發事件。
  - popup／sound 使用 outbox 命中時的 presentation snapshot；待發事件不會因後續音效或彈窗調整而改播，失效規則不會單獨阻斷同一帳號其他仍有效的命中。
  - 保留 legacy `MatchedRules` reader；新版使用 structured matches，既有 legacy outbox 仍可讀取並沿用原 owner／整組 revision fallback。
  - outbox 使用 `SKIP LOCKED`，新增 `ATTEMPTS`、`NEXT_ATTEMPT_AT`、`LAST_ERROR`、`IS_QUARANTINED`、`QUARANTINED_AT`；失敗以 5／30／120／600 秒退避，5 次後隔離，並寫入 `NOTIFICATION_AUDIT`。
  - 保留 sourceKey／recipient 唯一去重、帳號停用跳過、訂單取消／完成產生過期 history；非訂單 source 暫不查 ORDERS，避免被當成無訂單而立即過期。
- 未完成：S04 純指名與混合訂單來源、S05／S06 排程、S08 之後廣播事件；S11 change log 尚未建立，S03 的 retry audit 先沿用既有 audit 表。
- 契約／設計決策：新 payload 以 `RuleMatches` 做逐規則驗證；presentation 由 `RuleMatches` 快照重新合併，broadcast 優先、香檳塔優先、個人 sticky 優先於 toast。規則啟用／停用也會遞增 ruleRevision，避免停用後重新啟用讓舊待發事件復活。legacy payload 沒有 fingerprint，fallback 只能依既有 rule id／owner／settings revision 讀取，無法補推舊資料當時的條件版本。
- migration 檔案及套用狀態：新增 `db/migrations/20260909_04_notification_outbox_reliability.sql`，只增加 outbox retry／quarantine 欄位與索引；未套用，目標環境 DB 狀態未知。通知 worker 啟用前必須先套用此 migration。
- 已執行驗證（命令、目錄、結果）：API `dotnet build ToBeClarify.Api.csproj --no-restore`（`ToBeClarify-api`，成功，0 警告／0 錯誤）；Web `node node_modules/typescript/bin/tsc --noEmit`（`ToBeClarify-web`，成功）；API `git diff --check`（成功）。
- 未執行驗收與原因：未執行自動化測試、資料庫查詢、migration 套用、實際 outbox failure／retry、SSE、瀏覽器操作或部署；本段依既有部署政策只做建置／靜態檢查，且未取得測試與 DB 寫入授權。
- 不相關 dirty 檔案（未動）：API `.dotnet-home-local/`、既有 `docs/` 文件、S01／S02 API source；Web 既有 S01／S02 `features/admin/notifications/` source。
- 已知失敗／風險：`20260909_04` 尚未套用時，worker 查詢新欄位會失敗，因此必須以 migration 與通知 flag 一起管理；legacy outbox 缺少 fingerprint，只能 best-effort fallback；目前新規則 runtime 尚未產生指名／排程 outbox，capabilities 仍未開放新 ruleType。
- 背景工作：無。
- 下一個最小動作：開始 S04，調整 `OrderingRepository.CreateOrderAsync`／`MenuNotifications.EnqueueAsync` 的來源邊界，讓純指名能建立 `nomination_submitted`，混合訂單仍合併既有 `order:{id}:submitted`，並沿用本段逐規則快照。
- 下一段前置是否滿足：是，S03 API build／Web typecheck 通過；S04 開發前需先重新讀取本段 legacy fallback 限制與現有指名訂單資料結構，不先做 schedule。
- 發布／外部寫入：無；未 push、未套 migration、未部署、未啟用通知。

### S04｜2026-09-09｜已完成

- 狀態：已完成。
- 本段目標：補上純指名提交通知，讓混合點餐／指名／香檳塔訂單仍由同一次提交建立單一 outbox，並依 self／指定店員／ALL 正確匹配規則；純小費／純房間單不觸發這個通知切片。
- API 開始／結束 HEAD、branch：`9b70c4c64ab9e0aa2c73ad6e3bb51b6dfff412ae`、`codex/menu-ordering-integration`；未提交，HEAD 未改變。
- Web 開始／結束 HEAD、branch：`55b58e0b31720da60e3a6917320162612d926074`、`codex/menu-ordering-ready`；未提交，HEAD 未改變。
- 本段檔案變更（含未提交）：API `src/Services/Menu/MenuNotifications.cs`；Web `features/admin/notifications/AdminNotificationCenter.tsx`；更新本進度檔。既有 S01–S03 API／Web source、migration 與文件 dirty 狀態保留。
- 已完成：
  - `EnqueueAsync` 不再因沒有 `menu_item`／`menu_set` 直接 return；有 nominee 的純指名單會建立 `nomination:{orderId}:submitted`，payload 使用 `sourceType=nomination_submitted`、`action=view_nomination`。
  - 含 menu 的訂單仍使用 `order:{orderId}:submitted` 與 `sourceType=order_submitted`；混合點餐／指名／香檳塔不另建第二筆 outbox，依同一帳號合併命中規則與 presentation snapshot。
  - 個人 `designated_order_received` 支援 `self`、`staff`、`all`：self 比對登入帳號的 StaffMemberId，staff 比對 targetStaffId，all 對任一 nominee 命中；沿用 S02 的正規化 target 與 S03 的 ruleRevision／fingerprint snapshot。
  - 廣播在 S08 前仍只匹配既有 `order_received`／`champagne_order_received`，沒有提前開放新的廣播指名能力；personal rule 2 可由 API 儲存但 capabilities 與現有 UI 尚未宣告可新增。
  - 無 menu 且無 nominee 的純小費／純房間單不建立通知；香檳塔仍只由已保存的 `MenuSnapshotJson` event category 判斷，不重讀目前菜單。
  - Web 對 `view_nomination` 使用受控的指名查看連結，保留 null／非訂單 action 不產生錯誤 order link 的 S01 防護。
- 未完成：S05 指名開始／結束／結束後排程與改期失效；S06 營業時間；S07 個人規則 UI；新 rule 2 尚未列入 capabilities，亦未做瀏覽器設定入口。
- 契約／設計決策：純指名 source key 使用 `nomination:{orderId}:submitted`；只要含 menu 就維持既有 `order:{orderId}:submitted`，因此混合單的 outbox 去重與 S03 structured matches 不變。純指名仍保留 `orderId=order.Id` 供查看訂單內指名區塊，但 action 明確為 `view_nomination`。
- migration 檔案及套用狀態：未新增、修改或套用 migration；沿用 S03 `20260909_04_notification_outbox_reliability.sql` 的 outbox 欄位，目標環境狀態未知。
- 已執行驗證（命令、目錄、結果）：API `dotnet build ToBeClarify.Api.csproj --no-restore`（`ToBeClarify-api`，成功，0 警告／0 錯誤）；Web `node node_modules/typescript/bin/tsc --noEmit`（`ToBeClarify-web`，成功）；API／Web `git diff --check`（成功）。
- 未執行驗收與原因：未執行自動化測試、資料庫查詢、真實純指名／混合訂單寫入、outbox dispatch、SSE、瀏覽器操作或部署；本段依既有部署政策只做建置／靜態檢查，且未取得測試與 DB 寫入授權。
- 不相關 dirty 檔案（未動）：API `.dotnet-home-local/`、既有 `docs/` 文件、S01–S03 API source／migration；Web 既有 S01–S03 notification source。
- 已知失敗／風險：未有 runtime DB 驗證；若 S02 儲存有 targetStaffId 的規則，StaffMember 必須已啟用才可保存；S05 尚未建立 schedule，故目前只會在提交成功時提醒，不會因指名時段開始／結束提醒；新增 rule 2 仍需 S07 UI 與 capabilities 協商後才對使用者開放。
- 背景工作：無。
- 下一個最小動作：開始 S05，盤點指名確認／改期／縮短／取消交易入口與有效 `RequestedServiceEndsAt`，建立 schedule 表、唯一 scheduleKey、dueAt 與 scheduleRevision，不修改訂單業務狀態。
- 下一段前置是否滿足：是，S04 API build／Web typecheck 通過；S05 開發前需保留純指名提交事件與排程事件的 sourceType 區隔，不把舊提交事件追溯成時間提醒。
- 發布／外部寫入：無；未 push、未套 migration、未部署、未啟用通知。

### S05-a｜2026-09-09｜完成子段，S05 父段進行中

- 狀態：S05-a 已完成；S05 父段未完成，保留進行中。
- 本段目標：建立指名開始／結束／結束後通知的 schedule identity、dueAt 與未來排程建立入口，不處理改期／縮短／取消失效同步與背景派送。
- API 開始／結束 HEAD、branch：`9b70c4c64ab9e0aa2c73ad6e3bb51b6dfff412ae`、`codex/menu-ordering-integration`；HEAD 未改變。
- Web 開始／結束 HEAD、branch：`55b58e0b31720da60e3a6917320162612d926074`、`codex/menu-ordering-ready`；本子段未修改 Web source，既有 dirty 狀態保留。
- 本段檔案變更（含未提交）：API `src/Services/Menu/MenuNotifications.cs`、`src/Repositories/Ordering/OrderingRepository.cs`、`db/migrations/20260909_05_notification_schedules.sql`；更新本進度檔。未清理既有 API／Web source、文件與 `.dotnet-home-local/` dirty 狀態。
- 已完成：
  - 新增 `NOTIFICATION_SCHEDULES` migration，保存 source、order／nominee、owner、rule revision／fingerprint、N、scheduleRevision、dueAt、expiresAt、處理狀態與錯誤欄位；以 `SCHEDULE_KEY` 唯一鍵支援冪等建立，並以 nominee／due／owner 索引支援後續 worker。
  - `MenuNotifications.EnsureNominationSchedulesAsync` 只處理已 confirmed 且服務結束時間仍在未來的指名項目；支援 `nomination_starting`、`nomination_ending`、`nomination_ended`，分別以開始、服務結束、服務結束後計算 dueAt。
  - scheduleKey 實際使用 `nomination_schedule:{nomineeId}:{ruleId}:{ruleRevision}:{offsetMinutes}:1:{dueAtUtc}`；dueAt 以 Asia/Taipei 業務時間計算，key 使用 UTC 格式；到期後先保留 15 分鐘 expiresAt。
  - 指名全部確認、訂單成立的同一交易內建立排程；個人規則儲存成功後，同一交易掃描既有未來 confirmed／in_service 指名並補建立可用排程。`INSERT IGNORE` 使重複儲存／重試不重複建立。
  - 建立排程時若 dueAt 已經過去則跳過，固定「不對已錯過時段補播」；本子段尚未把 pending schedule 轉成通知 outbox。
- 未完成：
  - S05-b：改期、縮短、取消、拒絕、退回重新安排、過期／補登等狀態變更的舊排程失效、scheduleRevision 遞增與新時段重建。
  - S05-b：排程 worker 的 due window、15 分鐘過期、恢復後不補播、目前設定／rule revision 驗證，以及產生 `nomination_schedule` outbox；在此完成前不得開放新時間規則 capabilities 或啟用 worker。
  - S06 營業開／關店排程、S07–S14 仍未開始。
- 契約／設計決策：排程 owner 以 `staff:{staffId}` 保存，target `self`／`staff`／`all` 在建立時展開成各收件 owner；排程 sourceType 固定 `nomination_schedule`，entity 使用 `ORDER_NOMINEES.ID`，避免同一訂單多人指名互相覆蓋。修改規則造成的舊 schedule 清理明確留在 S05-b，避免本子段產生「資料已排但沒有完整失效流程」的錯誤完成假象。
- migration 檔案及套用狀態：新增 `db/migrations/20260909_05_notification_schedules.sql`；未套用。S05-b worker 依賴此表，必須與通知 flag／worker 一起管理；不修改已發布的 02／03／04 migration。
- 已執行驗證（命令、目錄、結果）：API `dotnet build --no-restore`（`ToBeClarify-api`，成功，0 警告／0 錯誤）；API `git diff --check`（成功，只有既有 LF／CRLF 警告，無 whitespace error）。
- 未執行驗收與原因：未執行自動化測試、DB migration 套用、真實設定／確認交易、schedule worker、outbox dispatch、SSE、瀏覽器操作或部署；目前只允許建置與靜態檢查，且 S05-b 尚未完成完整 runtime。
- 不相關 dirty 檔案（未動）：API `.dotnet-home-local/`、既有 API `docs/`／S01–S04 source／migration；Web `features/admin/notifications/` 既有 S01／S02／S04 source。
- 已知失敗／風險：本子段建立的 pending schedule 在 S05-b 前沒有消費者；若先套用 migration 但啟動現有 worker，不會自動處理 schedule，且新時間規則仍未列 capabilities。規則修改目前尚未失效舊 schedule，不能宣稱改期／縮短／取消安全。
- 背景工作：無。
- 下一個最小動作：開始 S05-b-1，集中盤點並修改 `RescheduleOrderAsync`、`ShortenNominationAsync`、`TransitionOrderAsync`／取消／過期與相關補登入口，在同一交易使舊 schedule invalidated、遞增 revision，再依新的 confirmed 時段建立未來 schedule。
- 下一段前置是否滿足：是，S05-a 已有 migration、schedule identity 與確認／規則儲存入口；S05-b 必須先保持 API build，並在 worker 完成前不要宣告 S05 完成。
- 發布／外部寫入：無；未 push、未套 migration、未部署、未啟用通知。

### S05-b｜2026-09-09｜已完成

- 狀態：已完成；S05 父段完成。
- 本段目標：接上指名排程的異動失效、scheduleRevision、到期窗口與排程 worker，完成 schedule → `nomination_schedule` outbox 的冪等交接；不進入營業時間提醒。
- API 開始／結束 HEAD、branch：`9b70c4c64ab9e0aa2c73ad6e3bb51b6dfff412ae`、`codex/menu-ordering-integration`；HEAD 未改變。
- Web 開始／結束 HEAD、branch：`55b58e0b31720da60e3a6917320162612d926074`、`codex/menu-ordering-ready`；本段未修改 Web source，既有 dirty 狀態保留。
- 本段檔案變更（含未提交）：API `src/Services/Menu/MenuNotifications.cs`、`src/Repositories/Ordering/OrderingRepository.cs`、`db/migrations/20260909_05_notification_schedules.sql`；更新本進度檔。未清理既有 API／Web source、文件與 `.dotnet-home-local/` dirty 狀態。
- 已完成：
  - `NOTIFICATION_SCHEDULES` 增加 `RULE_TYPE`，讓狀態異動可精準保留或失效規則類型；pending 排程可標記 `invalidated`，並保留歷史 revision。
  - `RescheduleOrderAsync`、`ShortenNominationAsync`、`TransitionOrderAsync`、`CancelPendingOrderAsync`、`ExpireWaitingOrdersAsync`、`BackfillServedOrderAsync` 都接入排程失效；縮短已成立指名會依新有效 `RequestedServiceEndsAt` 重建下一 revision。
  - 確認成立及重新確認會由既有排程最大 revision 推進 `scheduleRevision`；不同 N、不同 rule revision 與不同時段不合併。
  - 正常 `start` 失效已錯過的開始提醒、保留結束／結束後提醒；正常 `complete` 保留規則 5「指名時段結束後」排程；取消、拒絕、退回重排、逾期、補登則全部失效，不改用 buffer `RequestedBusyUntil` 計算規則 4／5。
  - `DispatchScheduleOneAsync` 使用交易與 `SKIP LOCKED`，先把超過 15 分鐘窗口的 pending 排程標記 expired，再處理 dueAt 到期項目；worker 恢復時不補播已超過窗口的舊事件。
  - worker 派送前重新檢查指名狀態、訂單狀態、owner、rule enabled、ruleRevision、rule fingerprint、rule type 與 target；有效時建立單一 `schedule:{scheduleId}` outbox，快照 `nomination_schedule`／`view_nomination` 與當下 popup／sound，沒有有效帳號也會封存空受眾而不重播。
  - outbox 派送再檢查 `nomination_schedule` source；取消／失效不建立 delivery。正常完成的規則 5 可在指名時段結束後送達，並沿用既有 delivery 15 分鐘有效期與 outbox retry／quarantine。
- 未完成：S06 營業時間規則 6／7；S07–S14 尚未開始。尚未開放 S05 新 ruleTypes capabilities，也未啟用通知 flag。
- 契約／設計決策：schedule 的 `DUE_AT`／`EXPIRES_AT` 使用 Asia/Taipei 業務時間欄位，scheduleKey 的 dueAt 使用 UTC 格式；過期窗口以 `EXPIRES_AT = dueAt + 15 分鐘` 判定。`nomination_ended` 是正常完成後可保留的例外，`completed` source 只有規則 5 可通過 source validity；取消／作廢／拒絕／expired 一律不可派送。
- migration 檔案及套用狀態：使用新增 `db/migrations/20260909_05_notification_schedules.sql`，加入 `RULE_TYPE` 與排程索引／外鍵；未套用。通知 worker 啟用前必須先套用 04、05 migration，並確認 `Notifications:Enabled` 與資料庫 schema 同步。
- 已執行驗證（命令、目錄、結果）：API `dotnet build --no-restore`（`ToBeClarify-api`，成功，0 警告／0 錯誤）；API `git diff --check`（成功，只有既有 LF／CRLF 警告，無 whitespace error）。
- 未執行驗收與原因：未執行自動化測試、DB migration 套用、真實確認／改期／縮短交易、worker／outbox／SSE、瀏覽器操作或部署；目前依部署政策僅做 build／靜態檢查，且沒有測試與 DB 寫入授權。
- 不相關 dirty 檔案（未動）：API `.dotnet-home-local/`、既有 API `docs/`／S01–S04 source／migration；Web `features/admin/notifications/` 既有 S01／S02／S04 source。
- 已知失敗／風險：尚未有 runtime DB 驗證；若未先套用 04／05 而開啟 worker，outbox retry 或 schedule 查詢會失敗。worker 是每 5 秒輪詢，未來 S11 才處理 cursor／SSE 的即時增量；此段不宣稱前端已呈現排程通知。
- 背景工作：無。
- 下一個最小動作：開始 S06，重新掃描有效營業 context、ProjectedCloseAt、business date／跨日計算與 maintenance 入口，沿用 `NOTIFICATION_SCHEDULES` 與 worker，但不改指名排程 sourceType。
- 下一段前置是否滿足：是，S05 migration／schedule identity／異動失效／worker 已完成；S06 開始前需先確認營業計畫資料來源與跨日既有語意，不先把規則 6／7 寫入 capabilities。
- 發布／外部寫入：無；未 push、未套 migration、未部署、未啟用通知。

### S06｜2026-09-10｜已完成

- 狀態：已完成。
- 本段目標：接入規則 6／7 的營業時間排程，沿用 S05 `NOTIFICATION_SCHEDULES`／worker，支援有效營業 context、跨日、ProjectedCloseAt 與設定／override 異動；不改指名排程與牆上時鐘語意。
- API 開始／結束 HEAD、branch：`9b70c4c64ab9e0aa2c73ad6e3bb51b6dfff412ae`、`codex/menu-ordering-integration`；HEAD 未改變。
- Web 開始／結束 HEAD、branch：`55b58e0b31720da60e3a6917320162612d926074`、`codex/menu-ordering-ready`；本段未修改 Web source，既有 dirty 狀態保留。
- 本段檔案變更（含未提交）：API `src/Services/Menu/MenuNotifications.cs`；更新本進度檔。未新增 migration，沿用 S05 `20260909_05_notification_schedules.sql`；未清理既有 API／Web source、文件與 `.dotnet-home-local/` dirty 狀態。
- 已完成：
  - `business_opening_soon` 以有效營業日的預定開始時間計算；`business_closing_soon` 在尚未開店時使用預定結束，營業中優先使用 `ProjectedCloseAt`，沒有 projected close 才回退預定結束。
  - 已有 `BUSINESS_PERIODS` row 時使用 period／actual open／actual close／settled 狀態；跨日例如 22:00–隔日 02:00 仍以 business date 所屬的 9/9 period 計算 9/10 01:50。
  - 沒有 `BUSINESS_PERIODS` row 時，只對明確由後台儲存過的營業設定建立今天前後相鄰的 business date schedule；初始 migration 建立、`UPDATED_BY` 為 null 的 legacy default 不視為可靠 24 小時營業計畫。
  - 啟用中的 `ORDERING_BUSINESS_DAY_OVERRIDE` 優先於 period／設定 fallback；override 到期或變更後 reconciliation 會使舊 pending schedule 失效。
  - worker 每輪先 reconciliation：規則新增／停用、N／rule revision 變更、ProjectedCloseAt 變更、實際開／關店與 period settle 都會比對 schedule key，失效舊 pending、保留相同有效項目，並以新的 scheduleRevision 建立 future schedule；dueAt 已過去不追溯補播。
  - business schedule 派送使用 `sourceType=business_schedule`、`sourceId` 為 period／override／business date、`action=open_notifications`、nullable orderId；在排程轉 outbox 前，以及 outbox 建立 delivery 前都重新核對 context、dueAt、rule revision／fingerprint 與 15 分鐘 expires window。
  - 未有可靠營業 context 時不建立提醒；營業提醒不依瀏覽器時間、不建立訂單連結，也不改訂單／營業狀態。
- 未完成：S07 個人規則 UI／capabilities 開放；S08–S14 尚未開始。規則 6／7 已有 API runtime，但尚未對前端使用者開放新增入口。
- 契約／設計決策：S06 不新增平行表，與 S05 共用 `NOTIFICATION_SCHEDULES`；`SOURCE_TYPE='business_schedule'`，source ID 使用 `period:{id}`、`override:{businessDate}` 或 `date:{businessDate}`，scheduleKey 同樣包含 rule revision、N、schedule revision、UTC dueAt。營業 fallback 以 `ORDERING_SETTINGS.UPDATED_BY` 非 null 作為已明確設定的界線，避免 legacy 24 小時預設誤發提醒。
- migration 檔案及套用狀態：未新增或套用 migration；依賴 `20260909_04_notification_outbox_reliability.sql`、`20260909_05_notification_schedules.sql` 均仍未套用。啟用 worker 前必須先確認兩份 migration 與通知 flag 同步。
- 已執行驗證（命令、目錄、結果）：API `dotnet build --no-restore`（`ToBeClarify-api`，成功，0 警告／0 錯誤）；API `git diff --check`（成功，只有既有 LF／CRLF 警告，無 whitespace error）。
- 未執行驗收與原因：未執行自動化測試、資料庫 migration 套用、跨日／override 真實資料查詢、worker／outbox／SSE、瀏覽器操作或部署；目前依部署政策僅做 build／靜態檢查，且沒有測試與 DB 寫入授權。
- 不相關 dirty 檔案（未動）：API `.dotnet-home-local/`、既有 API `docs/`／S01–S05 source／migration；Web `features/admin/notifications/` 既有 S01／S02／S04 source。
- 已知失敗／風險：尚未有 runtime DB 驗證；worker 啟用前若未套用 04／05 migration，既有 outbox retry 或營業 schedule reconciliation 會失敗。固定設定 fallback 只預先物化目前前後相鄰 business date，後續仍需確認實際營業計畫是否要擴展成更長的預排窗口。
- 背景工作：無。
- 下一個最小動作：開始 S07，重新掃描 Web 通知中心、API capabilities 與音效 API，加入「＋新增規則」、複製、target／N、排序與 Bell／BellRing 狀態；只開放 capabilities 已完成 runtime 的規則。
- 下一段前置是否滿足：是，S06 API build／diff check 通過；S07 開始前需保留 `business_schedule`／`open_notifications` 的非訂單 payload，不把 UI 直接綁定 orderId。
- 發布／外部寫入：無；未 push、未套 migration、未部署、未啟用通知。

### S07｜2026-09-10｜已完成

- 狀態：已完成。
- 本段目標：重新掃描現有 Web 通知中心、API capabilities、音效 API 與店員名單來源，完成個人通知規則編輯入口；只開放已完成 API runtime 的八類個人規則，不提前擴充店內廣播、cursor／SSE 或音效生命週期。
- API 開始／結束 HEAD、branch：`9b70c4c64ab9e00a2c73ad6e3bb51b6dfff412ae`、`codex/menu-ordering-integration`；HEAD 未改變。
- Web 開始／結束 HEAD、branch：`55b58e0b31720da60e3a6917320162612d926074`、`codex/menu-ordering-ready`；HEAD 未改變。
- 本段檔案變更（含未提交）：API `src/Controllers/Admin/MenuNotificationsController.cs`；Web `features/admin/notifications/AdminNotificationCenter.tsx`、`features/admin/notifications/types.ts`、`styles/admin/layers/40-dark-and-operational.css`；更新本進度檔。既有 API／Web source、migration、文件與 `.dotnet-home-local/` dirty 狀態保留。
- 已完成：
  - capabilities 增加 `personalRuleTypes`／`broadcastRuleTypes`；保留 `ruleTypes` 的既有兩類相容語意，個人設定頁只依個人能力顯示八類規則，避免店內廣播頁誤顯示尚未完成的個人排程廣播能力。
  - 個人設定可新增 `order_received`、`designated_order_received`、`nomination_starting`、`nomination_ending`、`nomination_ended`、`business_opening_soon`、`business_closing_soon`、`champagne_order_received`；「＋」可重複新增同類型，再透過 target／N 或複製後調整不同參數，最多 20 條。
  - 指名規則提供自己、ALL、指定店員三種監看對象；指定店員沿用既有 `getStaffMembers` API 顯示店員名稱並送出 `targetStaffId`。時間規則提供 0–1440 分鐘的整數 N，0 保持有效。
  - 規則提供彈窗 `none`／`toast`／`sticky`、音效選擇、靜態／震動鈴鐺按鈕、移除、複製、上移與下移；個人音效可使用系統或使用者音效，廣播頁仍只取系統音效。
  - 加入未儲存草稿的頁籤切換／重新載入／離開頁面警告；409 revision conflict 不覆寫目前草稿，明確提示重新載入比對；儲存前仍由 API 原子驗證 fingerprint／target／N。
  - 音效庫保留試聽／停止與既有 Ogg／Opus、MP3 上傳入口；鈴鐺狀態使用可存取的 aria-label／title，動態效果遵守 `prefers-reduced-motion`。
- 未完成：S08 店內廣播設定／手動與緊急廣播；S09 確認／撤回／緊急廣播生命週期；S10 重複／堆積；S11 cursor／分頁／SSE；S12 前端同步／更完整呈現；S13 音效生命週期；S14 整合／發布準備。未加入規則預覽事件或正式 `/test` API（S07 計畫列為可選）。
- 契約／設計決策：個人與廣播 capabilities 分 scope 宣告；`ruleTypes` 保留既有兩類以維持舊客戶端與廣播相容，Web 個人頁優先讀 `personalRuleTypes`。規則 `popupMode`、`soundId`、排序不進 fingerprint；複製會建立新的 Guid 並清除伺服器 revision／fingerprint，使用者須調整條件後才能通過 API 的 fingerprint 去重。
- migration 檔案及套用狀態：未新增或套用 migration；沿用 `NOTIFICATION_SETTINGS.RULES_JSON`、`20260909_03_notification_sounds.sql`、`20260909_04_notification_outbox_reliability.sql` 與 `20260909_05_notification_schedules.sql`。目標環境 DB 狀態未知。
- 已執行驗證（命令、目錄、結果）：API `dotnet build --no-restore`（`ToBeClarify-api`，成功，0 警告／0 錯誤）；API／Web `git diff --check`（成功，僅既有 LF／CRLF 警告，無 whitespace error）；Web `node node_modules/typescript/bin/tsc --noEmit`（成功）；Web `node node_modules/vinext/dist/cli.js build`（成功）。`npm run build` 未執行，因目前 shell 找不到 `npm`，改用相同專案已安裝的 Vinext CLI 完成 build。
- 未執行驗收與原因：未執行自動化測試、資料庫查詢／寫入、真實 API 設定保存、瀏覽器操作、SSE、migration 套用或部署；依專案測試環境政策，本段只做 build／typecheck／靜態檢查，且未取得額外測試與 DB 寫入授權。
- 不相關 dirty 檔案（未動）：API `.dotnet-home-local/`、`src/Repositories/Ordering/OrderingRepository.cs`、`src/Services/Menu/MenuNotifications.cs`、既有 migration 與文件；Web 既有 notification provider／order page 及本段以外的 source 變更。
- 已知失敗／風險：
  - `getStaffMembers` 載入失敗時，頁面仍可編輯自己／ALL，但指定店員下拉沒有可選項；API 仍會拒絕不存在或停用的 targetStaffId。
  - Web 目前仍沿用既有輪詢／bounded SSE 與整份 inbox，沒有在本段提前實作 cursor／增量變更；個人設定儲存後的 schedule／outbox runtime 尚未做真實 DB 驗證。
  - `Notifications:Enabled`、FFprobe／FFmpeg 仍依環境配置；本段沒有啟用通知或宣稱音效上傳服務已可用。
- 背景工作：無。
- 下一個最小動作：開始 S08，盤點既有廣播 owner／權限、店內廣播手動來源與 emergency 需求，再只實作 manager／developer 可見的廣播設定與手動送出流程；保留目前廣播頁兩類規則與系統音效限制。
- 下一段前置是否滿足：是，S07 API build、Web typecheck、Web build 與 diff check 通過；S08 前需確認廣播資料是否沿用既有設定／outbox，不能把個人 schedule 規則直接套到所有人。
- 發布／外部寫入：無；未 push、未套 migration、未部署、未啟用通知。

### S08｜2026-09-10｜已完成

- 狀態：已完成。
- 本段目標：完成經理／開發者可使用的店內廣播設定與手動發送；沿用 `broadcast` owner、既有兩類事件與 `NOTIFICATION_OUTBOX`，加入受眾／優先級／有效期／模板資料，不提前實作已知道、撤回、緊急確認、重複 episode、cursor／SSE 或音效生命週期。
- API 開始／結束 HEAD、branch：`9b70c4c64ab9e00a2c73ad6e3bb51b6dfff412ae`、`codex/menu-ordering-integration`；HEAD 未改變。
- Web 開始／結束 HEAD、branch：`55b58e0b31720da60e3a6917320162612d926074`、`codex/menu-ordering-ready`；HEAD 未改變。
- 本段檔案變更（含未提交）：API `src/Controllers/Admin/MenuNotificationsController.cs`、`src/Services/Menu/MenuNotifications.cs`；Web `features/admin/notifications/AdminNotificationCenter.tsx`、`features/admin/notifications/types.ts`、`styles/admin/layers/40-dark-and-operational.css`；更新本進度檔。既有 API／Web source、migration、文件與 `.dotnet-home-local/` dirty 狀態保留。
- 已完成：
  - `MenuNotificationRule` 擴充廣播名稱、`audienceMode`（all／working_today／roles／staff）、受眾店員／角色、priority（normal／high）、expiresAfterMinutes、templateCode；保留舊 broadcast owner／兩種 ruleType，後端拒絕把個人六種時間規則存成廣播規則。
  - 自動點餐／香檳塔事件依規則受眾匹配；預設全員包含沒有 StaffMemberId 的有效管理帳號，working_today／roles 依既有 daily work mode，發送事件時以有效帳號快照為準。
  - 廣播優先序改為 high > normal，再保留香檳塔固定優先；廣播有效期以命中規則的最短分鐘數建立 delivery expiration，手動廣播支援 1–1440 分鐘。
  - 新增 `POST /api/admin/notifications/broadcasts/send`：管理者／開發者權限由既有 owner 驗證，僅接受純文字與 `open_notifications` 受控 action；必須選擇可用系統音效，不能使用個人音效。
  - 手動廣播使用 `broadcast:manual:{idempotencyKey}` source key，送出時快照 account ids 到既有 outbox payload；重送相同 key 會回傳原始快照數量，不新增第二筆 outbox／通知。無受眾時仍保留空快照，避免日後補送。
  - Web 店內廣播頁提供規則名稱、受眾、指定店員／角色、多選、優先級、有效期與系統音效；新增手動廣播標題／內容／受眾／有效期／音效表單，送出後自動換新 idempotency key；未加入「測試發送全員」流程。
  - 舊個人規則與 all-audience 廣播規則沿用 S02／S07 fingerprint；只有 scoped broadcast audience 進入新的條件 fingerprint，避免讀取舊設定時無故使既有個人排程失效。
- 未完成：S09 已知道／撤回／緊急確認；S10 廣播重複／堆積 episode；S11 cursor／分頁／SSE；S12 前端增量同步與恢復呈現；S13 音效生命週期；S14 整合／發布準備。`requiresAck` 目前固定為 false，未對外開啟。
- 契約／設計決策：S08 不新增 `NOTIFICATION_BROADCASTS` 平行表；手動廣播實例以既有 outbox 的 source key、payload snapshot、processed state 與 audit 表示，等 S09 真正需要撤回／receipts 時再依查詢一致性評估新增表。`broadcast_manual` 不經個人／broadcast RULES_JSON 重新匹配，避免設定修改造成手動廣播被吞掉；仍沿用 outbox／delivery 的交易與重試路徑。
- migration 檔案及套用狀態：未新增或套用 migration；沿用 `20260909_02_menu_notifications.sql`、`20260909_04_notification_outbox_reliability.sql`、`20260909_05_notification_schedules.sql`。手動廣播沒有新增欄位表，但啟用前仍需確認既有 02／04／05 migration 已套用。
- 已執行驗證（命令、目錄、結果）：API `dotnet build --no-restore`（`ToBeClarify-api`，成功，0 警告／0 錯誤）；API／Web `git diff --check`（成功，僅既有 LF／CRLF 警告，無 whitespace error）；Web `node node_modules/typescript/bin/tsc --noEmit`（成功）；Web `node node_modules/vinext/dist/cli.js build`（成功）。
- 未執行驗收與原因：未執行自動化測試、資料庫查詢／寫入、真實廣播 API、worker／outbox dispatch、SSE、瀏覽器操作、migration 套用或部署；依專案部署政策，本段只做 build／typecheck／靜態檢查，且未取得額外測試與 DB 寫入授權。
- 不相關 dirty 檔案（未動）：API `.dotnet-home-local/`、`src/Repositories/Ordering/OrderingRepository.cs`、既有 `src/Services/Menu/MenuNotifications.cs` 的 S01–S07 變更、既有 migration 與文件；Web 既有通知 provider／order page 及本段以外的 source 變更。
- 已知失敗／風險：
  - 受眾依賴既有 `STAFF_DAILY_WORK_MODES`／`STAFF_DUTY_PLANS`；目標資料庫若尚未套用相關 staff migration，working_today／roles 查詢不能啟用，必須先完成資料庫相容檢查。
  - 手動廣播目前以 outbox payload 作為實例快照；S09 撤回與 receipts 尚未有獨立實例索引，不能宣稱已支援撤回／確認進度。
  - `Notifications:Enabled`、FFprobe／FFmpeg 仍依環境配置；本段沒有啟用通知或宣稱真實音效資產已交付。
- 背景工作：無。
- 下一個最小動作：開始 S09，盤點 delivery／inbox schema 與前端 banner，加入 read 與 ack 分離、廣播實例撤回、管理者 receipts、緊急 modal 與首次登入恢復；先維持現有 snapshot／polling 路徑，不等待 S11 cursor 才能工作。
- 下一段前置是否滿足：是，S08 API build、Web typecheck、Web build 與 diff check 通過；S09 前需先定義廣播 instance／delivery source key 與多來源合併撤回邊界，避免撤回一個來源連帶刪除其他未撤回來源。
- 發布／外部寫入：無；未 push、未套 migration、未部署、未啟用通知。

### S09｜2026-09-10｜已完成

- 狀態：已完成。
- 本段目標：完成已讀／已知道分離、廣播實例撤回、管理者 receipts、緊急廣播與首次登入恢復；沿用現有 snapshot／polling／legacy inbox，不提前做 S10 episode 或 S11 cursor／SSE。
- API 開始／結束 HEAD、branch：`9b70c4c64ab9e0aa2c73ad6e3bb51b6dfff412ae`、`codex/menu-ordering-integration`；HEAD 未改變。
- Web 開始／結束 HEAD、branch：`55b58e0b31720da60e3a6917320162612d926074`、`codex/menu-ordering-ready`；HEAD 未改變。
- 本段檔案變更（含未提交）：API `src/Services/Menu/MenuNotifications.cs`、`src/Controllers/Admin/MenuNotificationsController.cs`、`db/migrations/20260909_06_notification_acknowledgement.sql`；Web `features/admin/notifications/types.ts`、`features/admin/notifications/AdminNotificationCenter.tsx`、`styles/admin/layers/40-dark-and-operational.css`；更新 `docs/STAFF-NOTIFICATION-USER-GUIDE.md` 與本進度檔。既有 API／Web source、migration、文件與 `.dotnet-home-local/` dirty 狀態保留。
- 已完成：
  - `NOTIFICATION_DELIVERIES` 新增 `ACKNOWLEDGED_AT`／`WITHDRAWN_AT`；`NOTIFICATION_DELIVERY_MATCHES` 保存每個 delivery 的來源、規則、顯示／音效、優先級、有效期、requiresAck、ack 與撤回狀態。
  - dispatch 寫入 match-level 快照；同一張卡可同時保存個人與廣播來源。撤回會只移除指定 `SOURCE_ID` 的廣播 match，再重新計算卡片；若仍有個人來源，個人通知不被連帶撤回。
  - `POST /{id}/acknowledge` 僅允許該 delivery 的收件者操作；重複確認冪等，並與 `POST /read` 分離，各自寫入 audit。全部已讀不會改變已知道狀態。
  - `POST /broadcasts/{id}/withdraw` 與 `GET /broadcasts/{id}/receipts` 僅開放 manager／developer；撤回與確認進度均以廣播來源查詢，回傳收件、已讀、未讀、已知道、待確認、到期未確認、已撤回數量。
  - 手動廣播支援 `requiresAck`／`emergency`／`reason`；緊急廣播後端強制全員、高優先、逐人確認、純文字與系統音效，並寫入發送 audit。廣播規則的 `requiresAck` 會使用 `critical_modal`。
  - Web 新增緊急廣播確認視窗：單次只呈現一筆、按鍵 focus、Tab／Escape 不可繞過確認；有效且尚未確認的緊急廣播在首次 inbox 載入時恢復，撤回／到期／已知道後解除阻擋。通知抽屜提供已讀與已知道兩個獨立操作，管理頁可查詢最近廣播 receipts／撤回。
  - 更新使用者說明，明確標示 S09 已可用能力與仍待後續的 episode／cursor／音效生命週期。
- 未完成：S10 廣播重複／堆積 episode；S11 change log／cursor／分頁／SSE；S12 前端增量同步與跨裝置恢復；S13 音效生命週期；S14 整合／發布準備。舊 migration 以前已存在的 delivery 沒有自動回填 match rows，因此舊廣播不能追溯成 S09 receipts；不回填、不重播歷史聲音。
- 契約／設計決策：手動廣播實例 ID 仍由 `manual:{idempotencyKey}`／`emergency:{idempotencyKey}` 表示，match 的 `SOURCE_ID` 作為撤回／receipts 索引；一般廣播可設定 `requiresAck` 但必須使用 `critical_modal`，緊急手動發送另強制 all／high／reason。`ACKNOWLEDGED_AT` 只記收件者已知道，不會改訂單狀態或自動標記 `READ_AT`。
- migration 檔案及套用狀態：新增 `20260909_06_notification_acknowledgement.sql`，只建立 delivery ack／withdraw 欄位與 match table；未套用，目標環境 DB 狀態未知。後續 S11 使用 `20260909_07_notification_changes.sql`，S13 使用 `20260909_08_notification_sound_lifecycle.sql`。
- 已執行驗證（命令、目錄、結果）：API `dotnet build --no-restore`（`ToBeClarify-api`，成功，0 警告／0 錯誤）；Web `node node_modules/typescript/bin/tsc --noEmit`（`ToBeClarify-web`，成功）；Web `node node_modules/vinext/dist/cli.js build`（成功，完整 route build）；API／Web `git diff --check`（成功，只有既有 LF／CRLF 警告，無 whitespace error）。
- 未執行驗收與原因：未執行自動化測試、資料庫查詢／寫入、migration 套用、真實廣播／確認／撤回 API、worker dispatch、瀏覽器操作、SSE 或部署；依專案政策與本次分段授權，本段只做 build／typecheck／靜態檢查。
- 不相關 dirty 檔案（未動）：API `.dotnet-home-local/`、`src/Repositories/Ordering/OrderingRepository.cs`、S01–S08 已存在的 `src/Services/Menu/MenuNotifications.cs` 變更與既有文件／migration；Web 既有通知 provider／order page 及本段以外的 source 變更。
- 已知失敗／風險：
  - `supportsAcknowledgement=true` 只有在 `20260909_06_notification_acknowledgement.sql` 套用後才可對外啟用；未套用時 delivery dispatch／ack API 會缺少欄位或表。
  - receipts 需先由 worker 將 outbox 實例寫入 delivery／match rows；手動發送 API 成功後立即查詢可能暫時找不到收件進度，頁面保留重新查詢操作。
  - match rows 只由新版 dispatch 產生；既有歷史 delivery 保留可讀，但無法提供逐來源撤回／receipts。S11 仍需為新寫入接 change log，不能把目前 snapshot inbox 當成 cursor 完成。
  - 緊急 modal 的跨分頁選主仍沿用既有 leader／snapshot 路徑；S12 才補完整主分頁接手、重連與跨裝置 presentation 狀態。
- 背景工作：無。
- 下一個最小動作：開始 S10，先定義 occurrenceNo、至少 1 分鐘／最多 5 次的廣播重複欄位與 worker 停止條件，再處理待處理訂單 episode；不改 S11 cursor／SSE。
- 下一段前置是否滿足：是，S09 API build、Web typecheck、Web build、diff check 通過；S10 可沿用本段 match-level `requiresAck`／撤回狀態，但新增欄位前需確認 migration 編號與既有資料相容。
- 發布／外部寫入：無；未 push、未套 migration、未部署、未啟用正式廣播或上傳音效。

### S10｜2026-09-10｜已完成

- 狀態：已完成。
- 本段目標：完成廣播重複提醒與待處理訂單堆積 episode；重複至少間隔 1 分鐘、最多 5 次，並讓確認／撤回／過期／規則異動／來源解除能停止後續提醒；不提前做 S11 cursor／change log／SSE。
- API 開始／結束 HEAD、branch：`9b70c4c64ab9e0aa2c73ad6e3bb51b6dfff412ae`、`codex/menu-ordering-integration`；HEAD 未改變。
- Web 開始／結束 HEAD、branch：`55b58e0b31720da60e3a6917320162612d926074`、`codex/menu-ordering-ready`；HEAD 未改變。
- 本段檔案變更（含未提交）：API `src/Services/Menu/MenuNotifications.cs`、`src/Controllers/Admin/MenuNotificationsController.cs`、新增 `db/migrations/20260909_07_notification_repeats.sql`；Web `features/admin/notifications/types.ts`、`features/admin/notifications/AdminNotificationCenter.tsx`、`styles/admin/layers/40-dark-and-operational.css`；更新 `docs/STAFF-NOTIFICATION-USER-GUIDE.md` 與本進度檔。既有 API／Web source、migration、文件與 `.dotnet-home-local/` dirty 狀態保留。
- 已完成：
  - 通知規則契約新增 `repeatIntervalMinutes`、`maxOccurrences`、`backlogThreshold`、`backlogDurationMinutes`；廣播新增 `order_backlog` 類型，仍維持個人規則只影響目前登入店員，廣播規則才可對多人作用。
  - delivery payload 新增 `occurrenceNo`／`groupKey`／`episodeId`／`presentationId`；重複 occurrence 以同一 `groupKey` 更新同一收件卡，presentation id 帶 occurrence，避免每次重複建立新卡。
  - `NOTIFICATION_REPEAT_INSTANCES` 以 delivery match 為單位保存下一次時間、目前次數與停止狀態；重複事件經既有 outbox 派送，並受最多 5 次與至少 1 分鐘限制。
  - repeat worker 在來源卡已確認、撤回、過期、規則 revision／fingerprint 改變或來源不存在時停止；待處理訂單完成／不再符合原有 outbox active 條件後，delivery 也會過期而停止後續 repeat。廣播多來源仍以 match-level 狀態追蹤。
  - `NOTIFICATION_BACKLOG_EPISODES` 以「店內待確認訂單數達 K 且持續 M 分鐘」建立 episode；數量下降到 K 以下會標記 resolved，下一次重新達門檻會建立新 episode，避免每次 worker scan 都重播同一 episode。
  - backlog 來源使用既有 `ORDERS.STORE_CONFIRMATION_STATUS='pending'` 且 `ORDER_STATUS IN ('submitted','partially_confirmed')` 定義，受眾沿用既有 all／working_today／roles／staff 快照；前端可設定 K／M、重複間隔與上限，並顯示後續 occurrence 編號。
- 未完成：S11 change log／安全 cursor／歷史分頁／SSE；S12 前端增量同步、跨分頁主控接手與裝置呈現狀態；S13 音效生命週期；S14 整合／發布準備。backlog 尚未接實際 DB／worker runtime 驗證，不宣稱已部署。
- 契約／設計決策：`repeatIntervalMinutes=0` 強制 `maxOccurrences=1`；啟用重複時 interval 為 1–1,440 分鐘、max 為 1–5 次，第一次提醒算第 1 次。repeat instance 以 match key 隔離，ack／withdraw 任一會停止該來源的後續 repeat；backlog episode 以規則 revision／fingerprint 區分規則異動。
- migration 檔案及套用狀態：新增 `db/migrations/20260909_07_notification_repeats.sql`，增加 delivery occurrence／episode、match repeat 欄位、repeat instance table、backlog episode table；未套用，目標環境 DB 狀態未知。後續 S11 改用 `20260909_08_notification_changes.sql`，S13 改用 `20260909_09_notification_sound_lifecycle.sql`。
- 已執行驗證（命令、目錄、結果）：API `dotnet build --no-restore`（`ToBeClarify-api`，成功，0 警告／0 錯誤）；Web `node node_modules/typescript/bin/tsc --noEmit`（`ToBeClarify-web`，成功）；Web `node node_modules/vinext/dist/cli.js build`（成功，完整 route build）；API／Web `git diff --check`（成功，只有既有 LF／CRLF 警告，無 whitespace error）。
- 未執行驗收與原因：未執行自動化測試、資料庫查詢／寫入、migration 套用、真實訂單／廣播／repeat API、worker dispatch、瀏覽器操作、SSE 或部署；依專案政策與本次分段授權，本段只做 build／typecheck／靜態檢查。
- 不相關 dirty 檔案（未動）：API `.dotnet-home-local/`、`src/Repositories/Ordering/OrderingRepository.cs` 與本段未涉及的既有 source／migration；Web 本段以外的 source 變更。
- 已知失敗／風險：
  - `20260909_07_notification_repeats.sql` 尚未套用時，worker 的 repeat／backlog 查詢會失敗，因此必須與 02／04／05／06 migration 及 `Notifications:Enabled` 一起管理。
  - backlog episode 目前以店內確認佇列的 `STORE_CONFIRMATION_STATUS='pending'` 作為 K 的計數來源；若未來業務將「待處理」擴展到其他訂單狀態，需在規格與查詢同時調整。
  - 重複仍採現有 5 秒 worker／snapshot inbox 路徑，沒有 change log、cursor、跨裝置 presentation 去重；這些留給 S11／S12。
  - 既有歷史 delivery 不會自動回填 repeat instance 或 backlog episode，不會補播歷史聲音。
- 背景工作：無。
- 下一個最小動作：開始 S11，建立單調 `NOTIFICATION_CHANGES` change log 與 cursor，先接 delivery／read／ack／withdraw／repeat 的寫入入口，再設計 REST 分頁與 SSE replay。
- 下一段前置是否滿足：是，S10 API build、Web typecheck、Web build、diff check 通過；S11 可直接沿用 `NOTIFICATION_DELIVERY_MATCHES`、repeat instance 的來源鍵與現有 inbox 讀取邊界。
- 發布／外部寫入：無；未 push、未套 migration、未部署、未啟用正式廣播或上傳音效。

## 整體驗收（S14 完成追蹤，不預填通過）

主規格第 9.2 案例 1–18 逐項紀錄：案例編號、負責段號、程式位置、驗證方式、結果、未完成原因。建置證據與業務行為證據分開，不以文件存在當成實作完成。
