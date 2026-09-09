# 菜單、消費政策、報價與通知整合

2026-09-09；依使用者授權實作 A＋B＋C。這份文件描述程式與發布前置條件，並不表示已更新正式資料庫或正式 API。

## 本次行為

### A：前後台一致性

- `/menu` 是正式菜單路徑；`/meun` 導向 `/menu`。
- 單品呈現圖片、描述、標籤、數字價格；後台以逗號輸入標籤。
- 套餐顯示份數及主餐／甜點／飲品／其他角色。
- **公開菜單**：停售子品隱藏，套餐及原價保留；全部子品停售／不存在或完全無內容時隱藏母套餐。無可顯示套餐時，隱藏套餐區塊。
- **點餐頁**：固定套餐仍显示完整內容；任一必要子品停售則不可加入或提交。全部子品停售則隱藏母套餐。這避免以原價賣出不完整套餐。
- `menuSettings.showSets` 只控制公開菜單展示；要停止供應，使用套餐 `isAvailable`。
- 套餐專用品使用 `policy.canOrderAlone=false`，仍需 `isAvailable=true` 才能在套餐供應。
- 後台子品編輯完成後才更新草稿；取消不新增空列，切換商品時同步名稱。後端驗證內容非空、有效 ID、角色、份數及重複商品；停售草稿可留空。
- 新排序 API `PUT /api/admin/menu/order` 使用完整 ID 清單、revision、單次交易，只更新排序欄位。衝突保留草稿並要求重新載入，不逐筆覆寫商品。
- 公開菜單重新取得 live API；失敗時明示快照僅供參考。顯示時間是 API 回應產生時間，並非最後修改資料的時間。

### B：政策與歷史資料

- 僅說明 `information`：可新增文案，不會從文字推導收費。
- 系統連結 `system`：目前只支援入場餐點信物 `minimum_meal_credit`、每節指名基礎費 `base_nomination_fee`；金額由 ordering settings 產生。
- 可設定首頁、菜單、點餐確認的展示位置，以及台北時間的生效／結束時間。展示開關不會停止原有營運計價。
- 舊資料沒有政策時維持說明模式；首頁展示預設關閉，需要管理者明確選擇，避免全部文案塞入首頁。
- 商品事件分類 `eventCategories` 使用受控代碼陣列，第一個為 `champagne_tower`。不從品名、顯示標籤或一般菜單分類猜測。
- 套餐事件分類為自身與所有組成品分類聯集，後台顯示繼承來源。專用品可參與事件分類。
- 訂單保存提交時名稱、單價、份數、已乘上套餐份數的子品數量、事件分類、規則及信物快照；後續菜單修改不改寫歷史。
- 顧客訂單明細與管理者通知訂單頁可查看提交時的套餐內容。管理者後續改單不會改寫原始提交快照，畫面明示為歷史。

### C：報價及店內通知

- `POST /api/ordering/quote` 共用正式提交的計價服務，回傳完整費用、套餐內容、適用規則、折抵、餘額與是否待店內確認。
- 報價有效五分鐘；綁定點餐 session 及完整請求。Web 在 v2 API 下所有訂單都先確認報價；`Menu:RequireQuote=true` 時，API 強制所有含餐點訂單先報價。
- 提交重新確認價格、政策、套餐供應、組成、信物餘額；發生變更回傳衝突，要求重新報價。交易內鎖定相關資料以防報價到提交間異動。
- 相同報價 token 重送會回傳原訂單，避免回應中斷時重複建單與通知。前端另防止按鈕重複提交。
- 訂單與 notification outbox 同交易寫入，僅提交成功才派送；接收帳號及命中規則在事件建立時快照。
- 本次支援「收到點餐訂單」「收到香檳塔訂單」兩種規則，可設個人通知或全體有效帳號廣播。沒有 StaffMemberId 的有效管理帳號仍可收廣播。
- 派送前重查帳號、規則啟用與版本。每訂單／收件人只存一份 delivery；同時命中時廣播優先，香檳塔廣播高於一般點餐廣播，個人音效依規則順序。
- 待店內確認訂單明示等待確認。已讀、關閉提示與接單分開；取消、過期、完成訂單的提示失效，但保留歷史。
- `/admin/notifications` 提供規則、收件匣、已讀、音效啟用及桌面通知權限。音效必須由使用者點擊後啟用，無法繞過瀏覽器播放限制。
- 使用 SSE；Web 同源代理對通知串流停用緩衝。串流失敗後以 15 秒輪詢維持同步，worker 每 5 秒處理 outbox。背景分頁／裝置節流仍會影響實際延遲。
- 同帳號分頁使用 Web Locks／BroadcastChannel 選出提示與播音分頁，並記錄展示去重。初次登入載入收件匣，不重播離線期間音效。
- 音檔驗證：MP3 或 Ogg/Opus、最多 1 MiB、最多五秒、單一可解碼音軌。個人音效依 StaffMemberId 授權，廣播只能選系統音效。
- 音效使用獨立私人檔案與 metadata 表，透過驗證身分的端點讀取；不會進入公開圖片清理機制。規則啟用前檢查音效權限及檔案存在。

## 與通知 v1.1 的範圍關係

本次是菜單與提交事件的整合範圍，並非整份 `STAFF-NOTIFICATION-FEATURE.md` 完工。指名時間倒數、上下班／營業時間規則、手動／緊急廣播、逐人「已知道」、重複提醒、音效版本／刪除及完整審計查詢等仍屬通知系統後續範圍。本次的 outbox、帳號 delivery、事件分類、授權、私人音效與 SSE 可繼續擴充。

音效庫不附帶未经確認的聲音檔，也不自行指定實際香檳塔商品。正式啟用前，開發者上傳系統音效，管理者完成商品 mapping 與廣播規則。

## 發布順序與相容性

1. Web 先發布 `dev` → `www-dev.marchgroup.net`。舊 API 下保留既有編輯／下單能力；新版政策欄位、原子排序與通知依能力啟用。
2. API `dev` 只建置及產生 artifact，沒有獨立測試主機。Web dev 本身不代表新 API 已上線。
3. 正式 API 發布需要另外確認。確認後先備份並由 migration 帳號依序執行三份新增式 migration：
   - `db/migrations/20260909_01_menu_policies_and_quotes.sql`
   - `db/migrations/20260909_02_menu_notifications.sql`
   - `db/migrations/20260909_03_notification_sounds.sql`
4. migration 使用既有 MariaDB 相容語法，不自動回填商品分類或改寫既有金額。不要在新 API 執行期間移除欄位／資料表。
5. 部署新 API 時先保留 `Notifications:Enabled=false` 與 `Menu:RequireQuote=false`。這段相容期仍接受舊正式 Web 的無報價提交，新 Web 則一律走報價確認；避免分開發布造成舊站無法點餐。先確認 v2 菜單／報價契約與資料，再準備音效。
6. 設定 `Notifications:FFprobePath`、`Notifications:FFmpegPath` 為受維護的音訊工具絕對路徑；API 身分需要執行權限，並可寫入 `Media:RootPath/notification-sounds`。
7. 開發者在音效庫上傳系統音效（建議代碼 `order_chime`、`time_reminder`、`store_broadcast`）；管理者標記真實商品並儲存廣播規則後，再開啟 `Notifications:Enabled=true`、重新啟動 API。
8. 使用者確認 dev 運作正常後，Web 只透過 `dev → main` 手動 PR 晉升。不得由功能分支直接進 main。新版 Web 正式上線後，把 API `Menu:RequireQuote=true`，結束相容期並啟用無報價提交的強制拒絕；回退到舊 Web 前須先關閉此開關。

回退以先停用 notifications worker／回退應用程式為主，保留新增資料與欄位。正式環境部署、資料庫更新及音效設定尚未在本次開發發布中執行。

## 驗證紀錄與人工確認

本次依專案規範只執行 .NET Release build、Web production build、TypeScript、ESLint、差異／設定檢查及開發部署狀態／HTTP 可用性檢查，不執行自動化測試或操作正式資料。

人工確認重點：一項子品停售與全部停售、固定套餐不能不完整下單、舊購物車調價重新報價、重送同 token、套餐改名後歷史保留、多個香檳塔只產生一次提交通知、規則停用、個人與廣播合併、取消訂單、登入／登出、多分頁及音效權限。這些是待執行的驗收案例，不是已完成的測試紀錄。

## 本機 SDK

API 的 `global.json` 固定 .NET 10 SDK 主版本並允許後續 feature band。此帳號的 PowerShell 7 與 Windows PowerShell profile 優先使用 `%USERPROFILE%/.dotnet`；新工作階段 `dotnet --version` 為 `10.0.400`。此設定不移除 .NET 8，也不改系統層級的 CMD PATH。
