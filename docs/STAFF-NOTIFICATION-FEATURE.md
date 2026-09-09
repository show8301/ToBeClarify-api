# 店員提醒通知功能開發規格

> 版本：2.0｜修訂日期：2026-09-09
>
> 狀態：以新版菜單與通知程式為基礎的增量開發規格。下述「已實作」表示本機程式存在，不代表 migration 已套用或正式環境已啟用。
>
> 使用者說明：[店員提醒通知使用說明](STAFF-NOTIFICATION-USER-GUIDE.md)。
>
> 分段執行：[Luna 開發順序與任務卡](STAFF-NOTIFICATION-DEVELOPMENT-PLAN.md)；[進度與中斷接續紀錄](STAFF-NOTIFICATION-PROGRESS.md)。
>
> 範圍：八種個人規則、三種系統音效與個人音效庫、站內通知、瀏覽器桌面通知、經理／開發者店內廣播。

## 1. 已採用方案與適用邊界

- 後台新增「通知中心」；頂部固定鈴鐺提供未讀數與通知抽屜。
- 「我的通知」管理個人規則／音效；「店內廣播」由經理／開發者管理。
- 個人提示採非阻塞 Toast；可改置頂或關閉彈窗，音效獨立設定。
- 店內廣播採置頂橫幅；只有緊急廣播可使用需確認的 Modal。
- 規則用「＋新增規則」建立；同類型可新增不同參數，N = 0 有效。
- 系統提供開發者上傳的三種預設音效；音效與規則為多對一關係，可持續增加音效種類，不能硬編碼為三個選項。
- 音效首選 Ogg/Opus，支援 MP3；最長 5 秒、最多 1 MiB（1,048,576 bytes，介面可標示約 1 MB）。
- 音效檔放媒體儲存，資料庫存 metadata／sound id，沿用獨立音效表；SSE 即時通知，增量輪詢備援。
- 本期不提供 Web Push、關閉網站後的背景送達、Email、手機簡訊或外接廣播硬體控制。

本期即時提醒在已驗證登入的後台頁面運作。切換後台功能頁時保持連線；離開後台到公開網站、登出、帳號停用或登入失效時停止連線與播放。資料保留在伺服器收件匣，登入後可查看。

「已設定音效」只表示規則有選聲音，不等於裝置已能播放。OS 通知權限、網頁音效啟用與通知連線須分開顯示狀態。

## 2. 新版掃描結論與已確認業務定義

掃描基準（2026-09-09）：

- Web HEAD：55b58e0b31720da60e3a6917320162612d926074。
- API HEAD：9b70c4c64ab9e0aa2c73ad6e3bb51b6dfff412ae。
- Web 已改為 app／features／lib 架構，舊 src/admin 路徑不再適用。
- 本次為程式與 migration 靜態檢視，未執行測試、未操作線上訂單、未確認正式環境設定或音檔。
- appsettings.json 中 Notifications:Enabled 預設 false，FFprobePath／FFmpegPath 為空；環境覆寫值及三個真實音檔是否已上傳，須於發布時確認。

程式查核入口：[通知服務](../src/Services/Menu/MenuNotifications.cs)、[音效服務](../src/Services/Menu/NotificationSounds.cs)、[通知 API](../src/Controllers/Admin/MenuNotificationsController.cs)、[訂單快照](../src/Services/Menu/MenuQuoteService.cs)、[套餐分類合併](../src/Services/Client/Menu/MenuService.cs)、[前端通知中心](../../ToBeClarify-web/features/admin/notifications/AdminNotificationCenter.tsx)、[菜單分類編輯](../../ToBeClarify-web/features/admin/menu/MenuPolicyEditors.tsx)。

### 2.1 Someone／ALL：已確認

依使用者確認正式採用「監看對象」，不是訂單類型：

| targetMode | 畫面 | 匹配 |
|---|---|---|
| self | 我自己 | nominee.StaffId 等於規則擁有者 StaffMemberId |
| staff | Someone：指定店員 | nominee.StaffId 等於 targetStaffId |
| all | ALL：任一店員 | 任一有效 nominee |

規則 2–5 共用此定義。A 監看 B，通知只送給 A 的帳號，不會替 B 新增設定；ALL 也不代表廣播全員。staff 必須指定有效店員，self／all 不接受 targetStaffId。相同擁有者的 self 與指定自己的 staff 在重複條件檢查時應正規化為相同對象。

例如「B 開始前 10 分鐘」與「ALL 開始前 10 分鐘」可同時存在；B 的同一時點事件會合併命中來源，不播兩次。

### 2.2 香檳塔：新版已有可辨識的正式機制

確認可辨識，不再是待確認 mapping／需新建分類的項目。使用既有完整鏈路：

| 階段 | 程式與資料來源 |
|---|---|
| 菜單設定 | Web features/admin/menu/MenuPolicyEditors.tsx 的「香檳塔事件」開關，寫入 policy.eventCategories = ["champagne_tower"] |
| 服務驗證 | API MenuPolicies.Validate(ProductPolicy) 白名單驗證 eventCategories |
| 套餐繼承 | Client/Menu/MenuService.cs 將套餐本身分類與子品分類合併去重成 EffectiveEventCategories |
| 訂單報價 | MenuQuoteService.ResolveLine 將單品分類或套餐 EffectiveEventCategories 保存於 MenuLineSnapshot，套餐也保存 components |
| 提交再驗證 | MenuQuoteService 檢查當前政策、套餐子品與 snapshot 一致性，變更時要求更新報價 |
| 訂單快照 | NewOrderAggregate.MenuSnapshotJson 保存 MenuOrderSnapshot，含 Lines[].EventCategories |
| 提醒辨識 | MenuNotifications.EnqueueAsync 對 snapshot.Lines.Any(EventCategories.Contains("champagne_tower")) 判斷 |

結論：單品本身標記、套餐本身標記、套餐因子品繼承，都有正式程式路徑；同單多份／多項只觸發同一香檳塔提交事件。商品改名不影響判斷。未標記的商品即使名稱含「香檳塔」也不會命中。

沿用此機制，不再新增同義商品 category、重算歷史菜單或另建香檳塔判斷器。新商品仍由管理者正確設定開關；歷史無快照訂單不回補新單通知。

「點餐訂單」也已明確以 order.Items 中 menu_item／menu_set 判斷。純指名、純小費、純房間單目前不進這個通知切片；未來補指名新單時必須調整此入口邊界。

### 2.3 已存在的通知基礎

| 模組 | 已實作行為 | 尚待擴充 |
|---|---|---|
| 規則 | order_received、champagne_order_received；個人／廣播設定；整組 revision 儲存與 audit | 六種指名／營業規則、target／N、同類型多參數 |
| UI | 通知中心、頂部入口、收件匣、none/toast/sticky、廣播 banner、上移音效優先 | Bell／BellRing 圖片按鈕、完整規則編輯、離頁草稿提醒 |
| 提交事件 | OrderingRepository.CreateOrderAsync 在 commit 前 EnqueueAsync；空受眾也快照 | 純指名提交與時段異動事件 |
| 派送 | sourceKey 唯一 outbox；交易 FOR UPDATE SKIP LOCKED 領取；delivery 對 source/account 去重 | 有限失敗重試與隔離、時間任務、逐規則版本 |
| 收件匣 | 帳號收件，近 30 天最新 100 筆，15 分鐘有效期、已讀、取消／完成失效 | 分頁、增量 cursor、撤回、已知道 |
| SSE | 每 5 秒送整份 inbox，約 50 秒結束連線；Web catch-all proxy 特別轉送串流 | 可重播 id/cursor、穩定重連與增量同步 |
| 前端備援 | SSE error 後轉 15 秒整份輪詢；Web Locks／BroadcastChannel 主分頁、localStorage 去重 | 單一訂閱主頁、接手補漏、快取清理 |
| 音效 | 系統／個人上傳、授權讀取、FFprobe 檢查與 FFmpeg 解碼、5 秒／1 MiB | 刪除／引用保護、三個系統音檔交付確認 |
| 廣播 | 所有有效後台帳號，包含無 staff 管理者；系統音效、廣播合併優先 | 受眾選擇、優先級、定時、手動、緊急確認、撤回 |

已存在的 API／SQL 保持相容，不能把以上項目重新建成第二套服務。

### 2.4 本次識別的增量風險

1. **目前一類只能一條**：SaveAsync 以 RuleType 去重；UI 同型別存在後隱藏新增按鈕。需一起改成條件 fingerprint 與通用「＋新增規則」。
2. **純指名被入口略過**：EnqueueAsync 在無 menu item/set 時直接 return。需先判斷各類業務命中，純指名也產生一筆提交 outbox；混合單仍使用同一 sourceKey。
3. **整組 revision 可能吞待發事件**：目前派送要求 MatchedRules 的整組 revision 等於最新設定。修改不相關規則也可能讓既有待發匹配全消失。新增逐規則 revision／觸發 fingerprint，依原快照和該條規則是否停用／條件改變處理，不因別條規則儲存丟失提醒。
4. **廣播／營業不能假設有 orderId**：目前 message 必有 OrderId，InboxAsync 找不到訂單即失效，UI 固定連到訂單。新增 sourceType、sourceId、nullable orderId 及受控 action；不同來源各自判斷失效。
5. **SSE 不是增量重播**：目前無 id／cursor；正常結束也會走前端 error 後永久輪詢。升級時保留 legacy inbox 協定並用 capabilities 協商。
6. **多分頁主頁接手可能漏提示**：非主頁面已建立 baseline，其他頁面也各自收 SSE；接手需以共享呈現狀態比對仍有效未呈現項目，不只依各頁記憶體 baseline。
7. **首次登入跳過所有既有廣播**：目前 baseline 全標記已呈現；加入 requiresAck 後，尚有效未確認廣播必須另行恢復。
8. **最新 100 筆不是完整歷史**：未讀總數可能大於清單，既有按鈕是「目前清單全部已讀」。保留文案，另增歷史分頁與真正截至序號全部已讀。
9. **音效 metadata 不走 MEDIA_ASSETS**：目前獨立 NOTIFICATION_SOUNDS.FILE_NAME，受保護 content endpoint；保留已完成隔離，不強迫遷移到圖片媒體表。
10. **主題程式已實作不等於已啟用**：capabilities、worker feature flag、解析器配置、DB migration、真實音檔各自核對；不能只以有 UI 判定可送達。

## 3. 設定位置與互動

### 3.1 通知中心

路由為 /admin/notifications；店內廣播頁籤可使用 ?tab=broadcasts。

- 我的通知：規則清單、音效庫、裝置狀態與預覽。
- 店內廣播：管理者可見的廣播規則、手動發送與確認進度。
- 頂部鈴鐺：開啟最近通知抽屜，提供查看訂單、已讀、全部已讀、前往設定。
- 無 StaffMemberId 的管理帳號可管理與接收店內廣播；個人規則／音效編輯顯示需關聯店員身分。
- 通知抽屜保留最近 30 天；顯示層只計算未過期且未讀項目。過期紀錄可查閱，但不再彈出或播放。

### 3.2 規則卡片

每張卡片包含啟用開關、通知時機、監看對象（適用時）、N 分鐘（適用時）、彈窗選項、音效鈴鐺、預覽、複製及移除。排序提供拖曳與鍵盤上移／下移。

- 新規則預設啟用、一般提示、無音效；新使用者預設空規則清單，不自動替所有人套用通知。
- N 是整數，允許 0–1,440；快速選項 0、5、10、15、30、60。
- 同型別不同條件可並存，例如提前 10 分鐘與提前 0 分鐘各一筆。
- 相同型別、監看對象、N 及其他觸發條件完全相同，視為重複；改聲音或彈窗不構成另一個觸發條件。停用規則也納入檢查。
- 「儲存設定」原子提交所有新增、編輯、排序、移除；失敗則全部保持原狀。
- 用 revision 樂觀鎖防止跨分頁互相覆蓋；衝突回 409，保留草稿供使用者重新整理後比對。
- 已儲存設定影響未來事件；不因新增／修改規則追溯播放既有訂單。單人最多 20 條規則，與既有容量限制保持一致。

### 3.3 顯示方式

| 選項 | 行為 |
|---|---|
| 關閉彈窗（none） | 只保留通知抽屜；可另設音效 |
| 一般提示（toast） | 桌面右上角、手機頂部，6 秒收起 |
| 置頂提示（sticky） | 非阻塞卡片，直到關閉或事件失效 |
| 店內廣播（banner） | 全域橫幅，標示店內廣播、優先級與發送者 |
| 緊急確認（critical_modal） | 僅緊急廣播可用，按「已知道」記錄確認 |

- 關閉個人彈窗不會關閉音效；彈窗與音效都關閉時仍有收件匣紀錄。完全不收此類個人通知應停用規則。
- Toast 最多同時顯示 3 張，剩餘顯示數量摘要；通知內容不丟失。懸停／鍵盤聚焦時暫停收起倒數。
- 關閉提示、標記已讀、廣播已知道、訂單確認是不同操作；已知道不能自動接單或更改訂單狀態。
- 一般廣播不再同時另彈相同 Toast；個人與廣播重疊時依第 7 節合併。
- 一般訊息使用 polite live region，不搶焦點；緊急對話框處理焦點限制與回復。圖示不能只靠顏色辨識，BellRing 使用靜態波紋即可。
- 緊急廣播同時多筆時依序呈現；到期或撤回後解除阻擋，不偽造已確認。

### 3.4 音效選擇與裝置啟用

- 一般 Bell：未設定，標示「無音效」；BellRing：已設定，附音效名稱。
- 點擊開啟選擇器：不播放音效、系統音效、我的音效；每項提供播放／停止預覽。
- 同一音效可供多條規則選擇，不同規則也可各用不同音效。
- 本裝置提供「啟用音效／試聽」與「允許桌面通知」兩個按鈕；不能把登入動作視為已取得這兩種權限。
- 音效失敗須顯示可操作的裝置提示；不要只寫診斷 log，也不可移除已收到的通知。

## 4. 八種個人通知規則

本表監看對象依第 2.1 節已確認定義。規則 1、8 已有基礎實作；2–7 為本次增量範圍。

| 編號 | ruleType | 條件 | 觸發時間 |
|---|---|---|---|
| 1 | order_received | 含點餐品項的提交訂單 | 提交交易成功 |
| 2 | designated_order_received | 含符合監看對象的指名 | 提交交易成功 |
| 3 | nomination_starting | 我自己／Someone／ALL、N | 指名開始 − N 分鐘 |
| 4 | nomination_ending | 我自己／Someone／ALL、N | 指名服務結束 − N 分鐘 |
| 5 | nomination_ended | 我自己／Someone／ALL、N | 指名服務結束 + N 分鐘 |
| 6 | business_opening_soon | N | 預定開店 − N 分鐘 |
| 7 | business_closing_soon | N | 預定關店 − N 分鐘 |
| 8 | champagne_order_received | 明確香檳塔分類 | 提交交易成功 |

規則 2 也包含監看對象；規則 1、8 無分鐘／對象參數。N = 0 是有效值，不能以 falsy 判斷當成未填。

### 4.1 時間與狀態

- 後端 IAppClock 為唯一時鐘來源，使用 Asia/Taipei 業務日期與跨日設定；API 時間回傳含 offset，禁止依用戶裝置時區排程。
- 指名開始／結束以有效已成立排程為準。3、4 僅在未取消、未失效、未完成且已成立的時段適用；待確認／需改期時不發服務倒數。
- 5 是「排定結束後」提示，不等同「逾時未完成」。正常完成的有效指名仍可觸發，取消／作廢的不觸發；逾時未處理須另用廣播條件。
- 結束使用 RequestedServiceEndsAt 的現行有效值，不用 RequestedBusyUntil 的緩衝時間。
- 改期／縮短／取消時增加 scheduleRevision 並失效舊排程。派送前在交易中再次核對 revision、狀態、規則啟用情形。
- 6 使用該業務日期的有效預定營業時段；7 在尚未開店時使用預定結束，營業中以 ProjectedCloseAt 優先。調整預定時間會重排，實際提前開／關店則取消已無意義倒數。
- 無明確有效營業時段時不產生提醒，不將預設 24 小時視為可靠開關店安排。既有 override 沿用後端解析，不模擬牆上時間。
- 跨日例：9/9 22:00 開、9/10 02:00 關，關店前 10 分鐘為 9/10 01:50，businessDate 仍為 9/9。

### 4.2 延遲、離線與重排

背景工作每 15 秒掃描到期資料；N = 0 表示排程時間等於事件時間，非毫秒級送達保證。健康系統的後台前景提醒目標延遲不超過 30 秒。

- 排程到期後 15 分鐘內可補處理；開始前／結束前提示還須截斷於事件邊界。N = 0 的當下提示可保留 15 分鐘，仍須核對事件有效。
- 新增規則／排程改到已過的觸發時間，不補播倒數；排程異動可更新通知內容與歷史失效狀態。
- 重排到未來可產生新 revision 的提醒；同一 revision 不得重複產生。
- 首次登入先載入歷史與 cursor，不逐筆播放離線期間個人通知；顯示未讀摘要。短暫斷線重連可補提示仍有效且未呈現項目。
- 尚有效且需確認的強制廣播登入後重新顯示；已確認／撤回／過期不再阻擋。
- 事件匹配時記錄收件人與規則版本，離線不改變受眾。個人規則預設不依今日上班狀態過濾。

## 5. 強制型店內廣播

### 5.1 權限、受眾與預設

- 經理與開發者可管理、發送、停用／撤回廣播；後端每次驗證角色，包含測試與確認進度查詢。
- 預設對象為所有有效後台帳號，包含未關聯店員的管理者。是否正在登入僅影響即時連線，不決定是否列入受眾。
- 可明確改為今日上班者、指定角色、指定店員；儲存／發送前顯示範圍。今日上班採既有業務日期 daily work mode，不能以電腦日期替代。
- 登入狀態不作受眾條件；發送時快照符合對象的 account ids，之後新增帳號不追溯加入既有廣播。
- 個人規則與個人靜音不能停用廣播；網站仍無法繞過瀏覽器／裝置的播放限制。
- 廣播必選有效系統音效。第一版經理只能挑系統音效；開發者可上傳系統音效。避免直接分享別人的個人音效。
- 「啟用音效」代表裝置允許播放，不代表廣播允許被個人設定關閉。

### 5.2 模板與管理欄位

| 模板 | 預設狀態 | 規則建議 |
|---|---|---|
| 香檳塔新訂單 | 分類與系統音效就緒後啟用 | 全員、高優先、一次、橫幅 |
| 指名開始／結束／結束後 | 停用 | 可選對象與 N；一般／高優先 |
| 開店／關店倒數 | 停用 | 全員、一般、一次 |
| 新點餐訂單 | 停用 | 個人規則已能處理，管理者按需求啟用 |
| 待處理訂單堆積 | 停用 | 數量 K ≥ 1、連續 M 分鐘、最多 R 次 |
| 手動廣播 | 提供發送入口 | 標題、內容、範圍、有效時間、音效 |
| 緊急廣播 | 提供發送入口 | 原因必填、全員、需逐人已知道；不自動發送 |

廣播規則須有名稱、事件條件、受眾、優先級、顯示模式、soundId、有效期間、重複間隔／次數、版本與啟用狀態。內容只接受純文字，連結使用受控站內訂單路由。

- 預設每事件一次；需重複者間隔至少 1 分鐘、最多 5 次，每次有 occurrenceNo。接收者確認、事件解除、到期或撤回時停止其後續提醒。
- 預設有效期 15 分鐘，管理者可設 1–1,440 分鐘。定時規則不能超過第 4 節的事件有效邊界。
- 堆積事件使用現有訂單狀態列出「待處理」集合後才啟用；以首次持續達門檻作 episode，下降後重置，避免每輪掃描都成為新事件。
- 緊急提示要求確認時，不靠無限循環音效催促；重複仍受次數上限限制。
- 「全部已讀」不得代替已知道；管理者可查看待確認／已確認／到期未確認人數。
- 廣播測試只回傳發起者預覽，不投遞給全員、不新增正式確認紀錄。
- 建立／變更規則、上傳系統音效、手動發送、撤回與確認均寫入通知專用 audit。

## 6. 音效儲存與保護

### 6.1 檔案與生命週期

- 採專用音效服務，沿用媒體儲存抽象；檔案位於持久化 Media:RootPath/notification-sounds/，部署不能覆蓋，備份包含檔案及 metadata。
- 沿用獨立 NOTIFICATION_SOUNDS 的 FILE_NAME、MIME_TYPE、DURATION_MS、OWNER_STAFF_ID、SYSTEM_CODE；新增 hash、isActive、version、updatedAt 等必要欄位。檔案不搬入 MEDIA_ASSETS，既有 sound id 與檔案路徑保持有效。
- 檔案名採隨機 id；使用 media id，不暴露實體路徑。不上傳資料庫 Blob／Base64，不使用 localStorage 作正式音效庫。
- 系統三音效使用穩定 code：order_chime、time_reminder、store_broadcast；名稱可調整，真實音檔由開發者提供。不得將 BellRing 圖示誤當成音效名稱。
- 開發者可新增更多系統音效；三個基礎 code 不得被移除。未有真實音檔時視為資產待交付，不宣稱音效完成。
- 個人音效跨裝置同步；裝置播放許可與桌面通知許可不會跟著帳號自動授予。
- 正被規則或有效通知引用時拒絕刪除，要求先解除引用；未引用音效可軟刪除，檔案依保留政策處理。
- 維持音效與圖片 metadata 隔離；新增音效清理只查音效規則／有效通知引用，不加入圖片孤兒清理。

### 6.2 驗證與存取

- 允許 .ogg（Opus）與 .mp3；0 < duration ≤ 5 秒、size ≤ 1,048,576 bytes。44.1／48 kHz 與單聲道為建議值，非額外拒收條件。
- 前端先預覽，後端必須用受限資源的音訊解析器驗證實際 codec、可解碼性與時長；MIME／副檔名／簽章不足以證明 5 秒限制。
- 沿用已實作的 FFprobe／FFmpeg 檢查，不新增另一套解析器，不轉檔；不得信任客戶端送出的 duration。限制解析時間／記憶體、上傳速率，拒絕損壞與偽裝內容。
- 檔案先暫存驗證後再原子公開；metadata 寫入失敗可回收未引用檔案，避免儲存半成品。
- 音效讀取使用已授權的 /api/admin/notifications/sounds/{id}/content；個人音效只有擁有者可讀，系統音效供有效後台帳號讀。
- 維持專用 sound endpoint 授權與獨立目錄，驗證匿名 media endpoint 無法藉音效 id 或路徑讀取。現有音效不登錄 MEDIA_ASSETS，不為本功能擴大匿名媒體權限。
- 支援正確 audio MIME、Range 與 private cache；個人音訊快取於登出清除。缺檔／不可解碼時保持文字通知並顯示「音效不可用」，不可靜默替換成其他音效。

## 7. 通知產生、合併與即時傳遞

### 7.1 事件與排程

沿用訂單與 outbox 同交易寫入，以及 dispatcher 的 FOR UPDATE SKIP LOCKED 交易領取，再原子寫入 delivery／PROCESSED_AT；新增重試狀態與失敗隔離。若未來改成交易外執行長工作，才加入可恢復租約，不要求本次重寫已可用的領取機制。

- outbox 保存狀態、attempts、nextAttemptAt、processedAt 與錯誤；worker crash 交易回滾後可恢復。失敗 metadata 須在失敗交易回滾後另行記錄，避免每次重試都回到 0；leaseUntil 僅在採用租約時新增。
- 排程保存 entityId、scheduleRevision、ruleRevision、dueAt、expiresAt、狀態；唯一鍵防止多 worker 重複排程。
- 受眾依事件發生時規則有效版本匹配；派送前再次套用取消／停用等失效條件。
- 個人規則停用後取消待發提醒，不抹除已發歷史；廣播規則停用停止未來發送，已發廣播需明確撤回。
- 通知不可修改訂單業務狀態，歷史訂單不得在 migration 時全數轉成新通知。
- 保留最近 30 天收件匣，過期先做邏輯失效；實體資料清理由另行授權的維護機制執行，遵守直接 SQL 帳號無 DELETE／DROP／TRUNCATE 權限。

### 7.2 合併與聲音優先序

groupKey 使用來源實體＋事件時刻／有效 revision，例如同一 order 提交，或同一 nominee 在同一 dueAt 的提醒。不同 N 導致不同時間者必須分開，不能把提前 10 分鐘與當下提醒一起去掉。

- 同一 groupKey 對同一 account 只有一張收件卡，保留所有命中規則及來源。
- 音效與畫面優先：緊急廣播 > 高優先廣播 > 一般廣播 > 個人規則；同級廣播以固定規則 id 排序。
- 個人畫面取最強顯示（sticky > toast > none）；音效取排序最前且 soundId 非空的命中規則。全部 soundId 為空才無聲。
- 廣播與個人事件同時命中時只顯示廣播卡／播廣播音效，保留個人命中原因。需確認不能因合併遺失。
- 同一批匹配先完成合併再投遞，避免先播個人音效才收到同源廣播。明確的重複廣播更新原收件卡，presentation id 另含 occurrenceNo；首次、第二次等有意重複不被去重吃掉，確認狀態仍綁定原廣播。
- 合併卡的 requiresAck 依所有有效命中來源計算；撤回一項廣播須移除該來源再重算，不能連帶撤回其他未撤回廣播，也不補播被蓋過的個人音效。
- soundId 非空但失效時顯示錯誤，不繼續換聲音；強制廣播在儲存／啟用時驗證音效可用。
- 聲音串列播放，最多待播 3 個，超量合併為提示摘要；通知紀錄仍全部保留。緊急提示可中止一般待播，禁止多聲重疊。
- 同瀏覽器多分頁以 BroadcastChannel 搭配互斥／租約選主頁面播放與顯示桌面通知；主頁面關閉後接手。不同裝置各可提醒一次，已讀／已知道在伺服器同步。
- at-least-once 傳遞加持久化 presentation id 去重，不能宣稱瀏覽器 crash／確認遺失下達成絕對 exactly-once。

### 7.3 SSE、cursor 與授權

- SSE endpoint：GET /api/admin/notifications/stream，位於既有 cookie Path=/api 下，跨來源以 credentials 傳送；CORS 必須白名單 origin，不使用 wildcard credentials。
- v2 heartbeat 每 15 秒；沿用串流關閉緩衝設定。現有 Web proxy 的 SSE timeout 為 65 秒，升級可保持有界連線並在約 50 秒後正常重連，不將正常結束當成永久降級。真正失敗退回 25 秒增量輪詢並定期嘗試恢復 SSE，使用 backoff／jitter；legacy 客戶端仍使用既有 15 秒快照輪詢。
- SSE 的 Content-Type 為 text/event-stream，採 id/event/data frame；REST 才使用 ApiResponse<T>。
- 變更序號採可重播 change log：新增、已讀、已知道、撤回、失效都送事件。cursor 必須是提交後不漏資料的順序，不能直接假設自增 id 的配置順序等於提交順序。
- SSE Last-Event-ID 與 REST cursor 共用語意，綁定 account 授權；Web proxy 現在只轉送 content-type／cookie，升級須明確轉送 Last-Event-ID 或採已授權 cursor query。過期 cursor 回 resync_required，重取快照，不重播歷史音效。
- 登出／401／帳號停用清除連線、音效、通知快取；長連線須持續驗證有效登入與 token version，不能連線建立後永久授權。
- cookie 寫入 API 沿用並核對專案 CSRF 防護、Origin 檢查；不在 URL 傳 JWT 或任意 account id。
- 監控最舊待發時間、失敗重試、排程落後、連線數與音檔失敗；除錯記錄不存顧客敏感內容或 token。

### 7.4 桌面通知

只在使用者主動點擊後請求權限，HTTPS 且瀏覽器支援時啟用；權限拒絕時提供狀態與設定引導，不反覆要求。背景分頁可發簡短桌面通知，預設不包含顧客姓名／訂單內文。

規則 none 同時不發個人桌面彈窗；站內音效仍按設定。自訂聲音由網頁播放器負責，OS 通知不保證支援自訂聲音；支援時請求 OS 靜音以減少雙重聲，不能保證 OS 行為。行動裝置不支援時降級站內；本期不為它擴大成 Web Push。

技術依據：[自動播放限制](https://developer.mozilla.org/en-US/docs/Web/Media/Guides/Autoplay)、[SSE 使用方式](https://developer.mozilla.org/en-US/docs/Web/API/Server-sent_events/Using_server-sent_events)、[Notifications API](https://developer.mozilla.org/en-US/docs/Web/API/Notifications_API/Using_the_Notifications_API)。瀏覽器限制不影響收件匣持久化。

## 8. 資料與 API 契約

### 8.1 以既有 SQL 擴充，禁止重建同名表

既有 migration：

- 20260909_02_menu_notifications.sql：NOTIFICATION_SETTINGS、NOTIFICATION_OUTBOX、NOTIFICATION_DELIVERIES、NOTIFICATION_AUDIT。
- 20260909_03_notification_sounds.sql：NOTIFICATION_SOUNDS。
- 菜單商品 policy／quote／order snapshot migration 沿用目前菜單發布順序；實作前查實際版本，不重寫已發布 migration。

| 表 | 沿用 | 新增／調整 |
|---|---|---|
| NOTIFICATION_SETTINGS | OWNER_KEY = staff:{id} 或 broadcast；REVISION、RULES_JSON | JSON schemaVersion、每條 ruleRevision／targetMode／targetStaffId／offsetMinutes；陣列順序仍作音效優先，不新增平行個人設定表 |
| NOTIFICATION_OUTBOX | SOURCE_KEY、PAYLOAD_JSON、PROCESSED_AT；既有訂單 sourceKey 不改 | payloadVersion、attempts、nextAttemptAt、lastError、隔離狀態；保留既有交易領取可先不加租約 |
| NOTIFICATION_DELIVERIES | SOURCE_KEY＋RECIPIENT_ACCOUNT_ID 唯一；PAYLOAD_JSON、READ_AT | sourceType、acknowledgedAt、withdrawnAt、presentation sequence 等；不得改換現有 delivery id |
| NOTIFICATION_SOUNDS | FILE_NAME、SYSTEM_CODE、OWNER_STAFF_ID、DURATION_MS | isActive、version、hash、updatedAt 與刪除引用檢查 |
| NOTIFICATION_AUDIT | 現有 action/entity/before/after | 依需要新增角色／來源細節，保留現有 audit |
| NOTIFICATION_SCHEDULES（新增） | 無 | entityId、scheduleRevision、ruleId/revision、dueAt、expiresAt、scheduleKey 唯一、狀態 |
| NOTIFICATION_CHANGES（新增） | 無 | accountId、deliveryId、changeType、可安全重播 sequence |
| NOTIFICATION_MATCHES（按需求新增） | 目前 matchedRules 存 payload | 精確撤回／確認合併時保存各來源及 occurrence；migration 回填不觸發新聲音 |

不要依第 7 節邏輯名詞另建平行 NOTIFICATION_EVENTS／STAFF_NOTIFICATION_SETTINGS／STAFF_NOTIFICATION_RULES；目前 outbox／delivery／RULES_JSON 已可承載，只有實際查詢與一致性需求才新增表。

廣播規則也保留 broadcast owner 的 RULES_JSON，新增受眾／優先級／requiresAck／有效期／重複欄位。手動廣播實例可透過 sourceKey 與新 outbox payload 表示，不要求先正規化整個設定系統。

既有來源鍵 order:{id}:submitted 保持不變；新增時間來源鍵須含 nominee/business id、dueAt、scheduleRevision，必要時擴大原 VARCHAR(100) 長度並驗證索引限制。新舊 payload 用版本化 reader 支援，舊資料讀取補預設，不將舊訂單重新 enqueue。

### 8.2 相容 API 與新欄位

前綴為 /api/admin/notifications。沿用同源 Web proxy；不要改成前端直連另一個 API origin。

| API | 處理 |
|---|---|
| GET /capabilities | 保留 enabled/ruleTypes/delivery/soundUploadConfigured；新增 schemaVersion、supportsCursor、supportsAcknowledgement 等 |
| GET/PUT /settings?broadcast=false 或 true | 保留 expectedRevision + rules 契約；同一路徑擴充，經理／開發者才可 broadcast=true |
| GET /sounds、POST /sounds | 沿用；systemCode 非空時開發者專用，不另造 system-sounds 上傳 |
| GET /sounds/{id}/content | 沿用授權、private no-store、Range |
| DELETE /sounds/{id}（新增） | 自己未引用音效軟刪除；系統基礎音效不可刪 |
| GET / | 保留 items/unreadCount；新增 pageToken、snapshot cursor，舊客戶端不受影響 |
| POST /read | 保留 ids（最多 100）；另新增明確截至 cursor 的批次模式 |
| GET /stream | 舊客戶端仍讀 event: inbox；協商 v2 後採新增／更新／撤回事件與 id |
| GET /changes?cursor=...（新增） | 增量同步與恢復；cursor 過期提示 resync |
| POST /{id}/acknowledge（新增） | 對收件者冪等已知道，非 read 的別名 |
| POST /test（新增） | 驗證草稿後回本人預覽 |
| POST /broadcasts/send（新增） | 管理者手動發送，idempotency key 防重複 |
| POST /broadcasts/{id}/withdraw（新增） | 撤回整個廣播實例，更新受影響收件者 |
| GET /broadcasts/{id}/receipts（新增） | 管理者確認進度 |

個人 Rule 新增 targetMode、targetStaffId、offsetMinutes、ruleRevision；舊兩種規則預設這些條件為 null。保留 id/isEnabled/popupMode/soundId；排序仍以陣列順序為準。取消目前 RuleType 唯一限制，改依型別＋正規化條件 fingerprint 去重；保留單 owner 最多 20 條，UI 顯示容量，超限明確拒絕。

條件驗證：1、8 禁止 target／offset；2 必須 target、禁止 offset；3–5 必須 target 與 offset；6、7 必須 offset、禁止 target。N = 0 可儲存。個人 popupMode 為 none/toast/sticky；新增廣播模式僅對有權管理者開放。

legacy message 的 orderId 對既有訂單保持字串；新增來源可為 null，加入 sourceType/sourceId/action。UI 不得對 null orderId 建訂單連結。無來源欄位的舊 payload 預設 sourceType=order_submitted。

新客戶端依 capabilities.ruleTypes 顯示可新增規則；舊客戶端無法辨識 v2 規則時，API 拒絕其覆蓋整組 v2 設定並提示更新，避免舊 DTO 反序列化丟掉 target／N。API 可先相容部署，Web 再依能力啟用。

## 9. 實作切分與驗收

### 9.1 沿用實際檔案

| 檔案（相對各 repository） | 增量工作 |
|---|---|
| Web features/admin/notifications/AdminNotificationCenter.tsx | 拆分規則編輯／聲音鈴鐺／廣播 UI；擴充 Provider 增量同步與接手 |
| Web features/admin/notifications/types.ts | 八類規則、target、N、廣播與非訂單 payload |
| Web features/admin/notifications/api.ts | 保留同源請求，擴充新端點／業務碼 |
| Web features/admin/notifications/NotificationOrderPage.tsx | 保留訂單查看；非訂單事件使用各自 action |
| Web features/admin/layout/AdminLayout.jsx | 沿用既有通知中心入口／頂部元件，不新增第二個 |
| Web app/api/admin/[...path]/route.ts | 保留通知 SSE proxy 特例，轉送 v2 id／重連參數 |
| API src/Services/Menu/MenuNotifications.cs | 擴充匹配、版本與事件來源，可拆分 service 但保留既有 contract |
| API src/Services/Menu/NotificationSounds.cs | 沿用解析／存取，新增生命週期與引用保護 |
| API src/Controllers/Admin/MenuNotificationsController.cs | capabilities、SSE 升級、新增端點 |
| API src/Repositories/Ordering/OrderingRepository.cs | 保留同交易 EnqueueAsync；新增指名／時段異動 outbox |
| API src/Services/Menu/MenuQuoteService.cs、Client/Menu/MenuService.cs | 香檳塔鏈路沿用，新增相容性驗收即可 |

新增背景時間排程服務與必要 migration，不重新建立通知中心、商品分類或音效上傳功能。

實作批次：

1. 契約相容與逐規則版本、capabilities、generic source payload；先修正整組 revision 丟待發通知的問題。
2. 規則 2 指名提交＋self/staff/all；確保純指名與混合訂單都正確。
3. 規則 3–7、排程異動、跨日與背景恢復。
4. 廣播受眾、手動／緊急、重複、已知道與撤回。
5. cursor、分頁、主分頁接手、Bell／BellRing、音效引用保護及完整操作說明。

### 9.2 驗收案例

1. 「＋」可增加開始前 10、5、0 分鐘三筆；完全相同條件被拒絕，不同聲音不繞過重複檢查。
2. none＋有聲音只播音效；toast＋無聲音只顯示提示；兩者關閉仍有紀錄；停用不產生未來個人通知。
3. 我自己／指定店員／ALL 匹配與確認後定義一致，設定只影響擁有者。
4. 混合點餐／指名／香檳塔訂單合併一張通知，命中來源可追溯；不同提醒時間不被合併。
5. N = 0、跨日、ProjectedCloseAt 調整、改期／縮短／取消、過期／延遲與 worker 重啟都符合第 4 節。
6. 規則整組儲存原子性、revision 衝突與所有跨人讀寫均正確拒絕。
7. 三個真實系統音效與額外自訂音效可選；超長、過大、codec 偽裝、損壞音檔由後端拒絕。
8. 匿名 media 路徑無法繞過個人音效保護；原有圖片清理不刪除音效庫。
9. 廣播預設全員含未關聯 staff 的管理者；離線仍有收件項，個人停用不能擋廣播。
10. 重複廣播達上限停止，確認／事件解除／到期／撤回會停止；全部已讀不代表已知道。
11. 同瀏覽器多分頁切主頁面、SSE 斷線轉輪詢、cursor 過期、登入失效不重複轟炸／不洩漏上個帳號通知。
12. 手動發送重按不重複、outbox 多 worker 不重複、交易 rollback 不通知；重播涵蓋已讀／撤回變更。
13. 瀏覽器拒絕音效／桌面權限時仍保留站內訊息，UI 提供明確狀態；手機無桌面 API 仍可操作。
14. 測試音效／規則／廣播不產生正式收件項；焦點、鍵盤、窄螢幕與提示隊列可用。
15. 單品香檳塔、套餐自帶分類、子品繼承、重複香檳塔品項、商品改名及提交前分類變更均沿用快照機制；無標記不誤判。
16. 純指名通知能送出，混合點餐／指名保留同一提交 sourceKey；既有兩種規則不退化。
17. 修改規則 B 不丟失規則 A 已匹配待發事件；停用 A 會停止 A 待發，舊版客戶端不能覆蓋 v2 條件。
18. 無 orderId 的營業／手動廣播不被訂單失效 SQL 清掉；proxy cursor 可往返，超過 100 筆歷史可分頁查閱。

以上是驗收規格，不構成自動執行測試套件的授權。部署 dev 時僅執行專案允許的 build、靜態／設定檢查、部署狀態與 HTTP 可用性；如需自動化測試依本次使用者指示執行。

## 10. 給 Codex 的執行與發布要求

1. 閱讀根目錄及 API AGENTS.md、ORDERING-SYSTEM.md、DATABASE-PERMISSIONS.md 與當前程式，先確認實際 Web 入口、狀態及 migration 編號。
2. 第 2.1 節 self/staff/all 已確認；第 2.2 節香檳塔已有正式來源。依現有 policy／訂單快照直接沿用，不再將這兩項列為待確認，也不另造分類。
3. 依第 9.1 節批次擴充，保持現有原子儲存、音效庫、訂單 outbox、SSE／輪詢可用；本規格的目標行為不能誤標為已完成。
4. 加入廣播與管理權限、撤回／確認、多分頁去重及可恢復的背景任務，按第 9 節驗收並同步使用者說明。
5. 發布前確認三個音檔、香檳塔分類、後端解析器與 SSE 的 IIS 設定均就緒；未交付項目必須列為未完成。
6. Web 程式／設定一律先推 dev 到 www-dev.marchgroup.net；等使用者確認運作正確後，才以 dev → main 手動 PR 推廣相同已驗證 commit。禁止 feature branch 直接進 main。
7. API dev 僅建置、沒有測試主機；API main 才部署正式。不得將「Web dev 發布」解讀成 API 正式發布授權，需規劃相容 migration／feature flag 與正式發布時點。
8. migration 不由應用啟動自動套用；直接 SQL 帳號不可 DELETE／DROP／TRUNCATE。功能可用 flag 回退，保留通知歷史及音效資料。
9. 交付時說明修改檔案、契約、驗證、尚未完成依賴與部署狀態；文件修訂不表示已實作或已發布。
