# 店員提醒通知發布前檢查表

> 目的：把 `STAFF-NOTIFICATION-FEATURE.md` 第 9.2 節的驗收案例、migration、環境依賴與回退邊界集中記錄。
>
> 目前狀態：程式與文件完成；`20260909_04`–`20260909_09` 已套用正式 MariaDB，API production 與 Web production 已完成部署；MP3 parser 已改為 API 內建，仍待正式音效、通知環境設定與人工驗收。此文件的「程式路徑已核對」不等於線上功能通過。

## 1. 發布前外部前置

| 項目 | 目前狀態 | 完成證據／備註 |
|---|---|---|
| API migration 依序套用 | 已完成 | `20260909_04`–`20260909_09` 已依序套用；06／07 新通知表使用既有 `utf8mb4_general_ci`，不可由應用啟動自動套用 |
| `Notifications:Enabled` | 待環境確認 | 未啟用時 capabilities 應回報 disabled，不可宣稱通知可用 |
| .NET MP3 parser | 已完成 | API 內建 MPEG Layer III frame parser；不需 FFprobe／FFmpeg 環境設定 |
| 三個 system sound | 待資產交付 | `order_chime`、`time_reminder`、`store_broadcast` 的實際檔案、MIME、備份與可播放性 |
| Web dev 發布 | 已完成 | `ec0931494e976bf36a86352298f2a8db9f4b1c0f`；IIS deployment 與 `www-dev.marchgroup.net/api/health` HTTP 200 |
| API 發布 | 已完成 | `579d566e1c47efe9f48ec2381a3b2c1c9f41e813`；IIS deployment success，`/api/client/menu` HTTP 200／`contractVersion=2` |

## 2. 18 條驗收案例追蹤

狀態只使用：`程式路徑已核對`、`待人工驗收`、`待環境`、`未執行`、`失敗`。

| # | 驗收重點 | 程式路徑／目前判定 | 下一步 |
|---:|---|---|---|
| 1 | `＋` 新增 10／5／0 分鐘；完全相同條件拒絕 | 程式路徑已核對：設定 fingerprint／offset validation／UI 新增 | 待人工驗收 |
| 2 | popup／sound 獨立；停用不產生未來通知 | 程式路徑已核對：rule renderer／dispatch enabled 判斷 | 待人工驗收 |
| 3 | self／staff／ALL 匹配只影響設定擁有者 | 程式路徑已核對：target normalization／recipient matching | 待人工驗收 |
| 4 | 混合來源合併、來源可追溯、不同時間不合併 | 程式路徑已核對：delivery matches／group key／presentation id | 待人工驗收 |
| 5 | N=0、跨日、ProjectedCloseAt、改期／取消／worker 重啟 | 程式路徑已核對：schedule reconciliation／bounded worker | 待人工驗收 |
| 6 | 原子設定、revision 衝突、跨人讀寫拒絕 | 程式路徑已核對：expectedRevision／owner authorization | 待人工驗收 |
| 7 | 三種系統音效、自訂音效、非法或損壞檔案拒絕 | 程式路徑已核對；實際三音檔與 parser 待環境 | 待環境＋人工驗收 |
| 8 | media 保護、圖片清理不刪音效 | 程式路徑已核對：authorized content／獨立 sound root | 待人工驗收 |
| 9 | 廣播預設全員含管理者；離線有收件；個人停用不擋廣播 | 程式路徑已核對：audience snapshot／broadcast owner | 待人工驗收 |
| 10 | 重複上限、ack／解除／到期／撤回停止；read 不等於 ack | 程式路徑已核對：repeat instances／ack／withdraw | 待人工驗收 |
| 11 | 多分頁主頁接手、SSE→polling、cursor 過期、401 清理 | 程式路徑已核對：Web Locks／BroadcastChannel／SSE v2 | 待人工驗收 |
| 12 | 手動／outbox 不重複、rollback 不通知、read／withdraw 可重播 | 程式路徑已核對：idempotency／transaction change log | 待人工驗收 |
| 13 | 拒絕音效／桌面權限仍保留站內訊息；手機可操作 | 程式路徑已核對：audio／desktop 狀態與降級 | 待人工驗收 |
| 14 | 預覽不產正式收件；焦點／鍵盤／窄螢幕／隊列 | 程式路徑已核對：preview isolation／critical modal／queue | 待人工驗收 |
| 15 | 香檳塔分類與訂單 snapshot 全部符合 | 程式路徑已核對：菜單分類／MenuSnapshotJson／EnqueueAsync | 待人工驗收 |
| 16 | 純指名與混合指名 sourceKey；既有規則不退化 | 程式路徑已核對：nomination source／legacy rule path | 待人工驗收 |
| 17 | 改 B 不丟 A；停用 A 停止 A；舊客戶端不能覆蓋 v2 | 程式路徑已核對：rule-level match snapshot／schema guard | 待人工驗收 |
| 18 | 非 order source 不被失效 SQL 清掉；proxy cursor；歷史可分頁 | 程式路徑已核對：source-aware expiry／Last-Event-ID／pageToken | 待人工驗收 |

## 3. 回退與操作邊界

- 回退優先使用 `Notifications:Enabled=false` 或 capability gating，不刪除通知歷史、change log、音效資料，也不使用 `DELETE`／`DROP`／`TRUNCATE` 破壞性回退。
- 若只回退 Web，API 保留 legacy `inbox`／polling；若只回退 API，Web 應依 capabilities 降級，不顯示尚未可用的 v2 選項。
- 音效停用採 `IS_ACTIVE=false` 軟刪除；不直接刪除檔案，避免既有 payload／歷史通知失效。
- Web 發布順序固定為 `dev` → 使用者確認 → `dev` 到 `main` 的手動 PR；不可由本段自動 push、部署或啟用正式廣播。
- migration 套用後若要暫停功能，使用 flag／capability 回退，保留 schema 與資料，待修正版本再恢復。

## 4. 本次 build-only 證據

- API：`dotnet build ToBeClarify.Api.csproj --no-restore`。
- Web 型別：`node node_modules/typescript/bin/tsc --noEmit`。
- Web build：`node node_modules/vinext/dist/cli.js build`。
- 靜態檢查：API／Web `git diff --check`。
- 本次未執行：自動化測試、API／SSE 實連線、瀏覽器操作、Web production promotion；本次已完成資料庫唯讀核對、04–09 migration apply、API production 與 Web dev deployment。
