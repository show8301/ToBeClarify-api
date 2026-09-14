# 留言板首次上線與驗收

## 狀態與部署閘門

程式在 Web／API 的 feature/guestbook，尚未執行 migration 或部署。
建置通過不等於資料庫整合驗證完成；先使用獨立、可丟棄的 MySQL／MariaDB 資料庫驗證。
禁止以正式留言測試隱藏、編輯、限流或並行寫入。

## 執行期設定

| 服務 | 設定 | 用途 |
| --- | --- | --- |
| Web | GUESTBOOK_PROXY_SECRET | 與 API 共用的隨機密鑰，至少 32 字元；不要使用 NEXT_PUBLIC_ 前綴 |
| Web | GUESTBOOK_TRUSTED_IP_HEADER | 被可信代理覆寫的單一訪客 IP header 名稱 |
| Web | GUESTBOOK_PUBLIC_ORIGIN | 瀏覽器實際 origin，例如 https://www-dev.marchgroup.net；不含路徑與結尾斜線 |
| API | Guestbook__ProxySecret | 同一密鑰；也可用伺服器私有設定 Guestbook:ProxySecret |

沿用現有 PUBLIC_CLIENT_API_BASE_URL／ADMIN_API_BASE_URL 連線設定，不將上游網址放在瀏覽器程式。
設定須注入實際執行 Web／API 的程序，不只是 runner 建置環境；變更後重新啟動對應程序。
密鑰不得提交 Git、放入前端 bundle、貼到 log 或聊天。

Web 只接受指定來源的 JSON POST，讀取可信 IP，簽署 IP 與時間後交給 API。
API 要求簽章在 60 秒內，伺服器時鐘必須同步。直接呼叫舊／新 public POST 都不能繞過簽章。
缺少密鑰／可信 IP 時公開寫入拒絕服務，不會退回不受限的寫入。

若採 CF-Connecting-IP，必須先確認：
- Web origin 只能由受信任的 Cloudflare／IIS 路徑連入，不能以 origin IP 繞過代理。
- 中間代理保留的是經驗證的訪客 IP，不能由外部請求任意指定。
- 不直接信任任意 X-Forwarded-For 清單，也不拿代理自身 IP 作所有訪客的 key。

不滿足以上条件先不要開放提交；單獨設定 header 名稱並不足以防偽造。
限流以正規化 IP 的 HMAC 作 key，不存明文 IP；同一 Wi-Fi／NAT 共用每 180 秒一次額度。
成功新增／回覆共用額度；後台發言不套用訪客限流。這不是完整的 DDoS 防護。

## 首次發布順序

1. 在獨立 DB 還原原始 schema／去識別測試資料，備份並執行 API 的 db/migrations/20260911_01_guestbook.sql。
2. 完成下方驗收，確認伺服器 IP 信任鏈、密鑰、外部 origin 和資料庫時區（沿用現有台灣時間）設定。
3. 安排維護窗口。舊 Web 不簽署提交，新 API 上線後舊 Web 將無法寫入；不支援無縫 API-first 混版。
4. 備份正式資料庫及 Web／API 發布檔案，停止或阻擋留言寫入，再於批准後執行 migration。
5. 依既有分支規範發布：Web 先 dev 驗收，使用者確認才 dev → main；API dev 只有建置，main 才正式部署。API 無 dev host，不能讓未驗證 Web dev 修改正式留言來代替測試。
6. 部署配對版本與私有環境設定；確認匿名閱讀、登入管理、IIS 運行狀態後再開放。
7. 不快取 /api/guestbook、/api/client/guestbook、/api/admin/guestbook 回應；確認 CDN 沒有覆蓋 no-store。若以前有快取留言頁／API，清除相關快取。

migration 含 ALTER TABLE，不能假設整份 SQL 可交易回滾，也不能重複執行。
失敗時先核對已成功的欄位／表，再由維運處理剩餘步驟，不要直接重跑。
回退時保持留言寫入關閉：舊 API 缺少新版鎖定／限流／編輯標記，不可作為正常可寫的回退版本。
不要刪掉新表或還原備份覆蓋已收到的新留言；恢復服務需明確的資料保留方案。

## 獨立 DB 驗收清單（尚未執行）

- Migration 能保留舊留言／回覆，新增三張表均為 InnoDB／utf8mb4；繁體字、emoji、換行正常。
- 訪客只填名字／內容即可立即公開；純文字顯示，不把 HTML 當標籤執行。
- 新留言與回覆共用 180 秒限制；同 IP 同時兩次送出只能成功一次，另一筆不得寫入；429 帶 Retry-After。
- Honeypot 非空不寫 DB；無簽章／錯誤簽章／逾時簽章不可提交；舊 comments POST 同樣受限制。
- 回覆只能接主留言；主串被隱藏／鎖定時不可送出，包含與鎖定並行的請求。
- developer／manager／clerk 可管理；未登入者不可管理。店員本人名稱來自帳號綁定，無綁定帳號只能選其他身分。
- 編輯出現公開標記、保留修改前後稽核；兩人編輯同一版本，第二人得到衝突，不覆蓋第一人。
- 隱藏整串後，公開列表與直接回覆查詢都不可讀；恢復整串不會恢復單獨隱藏的回覆。
- 關閉／重新開啟回覆、置頂排序與版本衝突正常；公開載入更多採 nextCursor，一般留言不重複顯示置頂。
- 吉祥物更名只影響新留言；匿名店員公開 DTO 不包含真正帳號／staff ID，後台仍可稽核。
- 桌面／手機、現有明暗色系、編輯 dialog、錯誤提示與無資料狀態人工檢查。

## 資料保留

冷卻記錄會累積；目前沒有自動刪除工作。
維運核准後可每日分批清理 NEXT_ALLOWED_AT 小於 UTC 現在減一天的記錄，
每批最多 1000 筆，監看負載並避免長交易；不得刪除尚未過期的 key。
保留一天緩衝，避免清理與有效限流競爭。

稽核記錄包含修改前的文字與實際管理員 ID，僅後台有權限读取。
目前不自動刪除；保留期限與個資刪除流程由店家決定，備份亦須納入權限管理。
公開隱藏不是硬刪除，也無法收回使用者已經閱讀或另存的內容。
