# 作品交付檔案儲存：待核准發布與復原程序

狀態：本 PR 僅準備程式、migration 與操作流程；不授權套用正式 schema、設定、回填或部署。

## 路徑證據與設定

2026-10-20 唯讀取得 GitHub repository variables：API `API_DEPLOY_PATH` =
`D:\www_root\ToBeClarify_API`；Web `WEB_DEPLOY_PATH` = `D:\www_root\ToBeClarify_web`；
`DEV_WEB_DEPLOY_PATH` = `D:\www_root\ToBeClarify_web_dev`。來源分別是兩個 repository 的
`GET /repos/show8301/{repo}/actions/variables`，不是從範例推測。

三個部署目錄共用 `D:\www_root`，預定 API 設定為：

```json
{ "Media": { "PrivateRootPath": "D:\\www_root\\media" } }
```

新作品檔案為 `D:\www_root\media\art-deliveries\{deliveryId}\{assetId}.png`。
資料庫 `STORAGE_KEY` 僅保存 `art-deliveries/{deliveryId}/{assetId}.png` 相對鍵；
不回傳前端。`STORAGE_PATH` 是前一版已加入的相容欄位，保留舊路徑；讀取順序為
`STORAGE_KEY` → `STORAGE_PATH` → `FILE_BYTES`。有檔案鍵但內容遺失／hash 不符時直接失敗，
不得偷偷改讀 BLOB 掩蓋檔案損壞。新圖只寫檔案與中繼資料，仍採原有 10 MiB 上傳／
12 MiB PNG 編碼上限，本次不引入 30 MiB 或新的 LONGBLOB 方案。

這份 PR 基於遠端已存在的 `5a4d373` 私密圖片實作，沿用它的 PrivateRootPath 與授權端點。
若該版已另設 private root，**先停寫並備份，再完整複製舊 root 的內容至新 root**，
核對相對檔名、大小、SHA-256 後才切換設定。不要只改設定而讓現有 STORAGE_PATH 失聯。
原 root 保留至回滾窗口結束。留言圖片仍使用同一 private root，但本次回填命令用
`--private-image-category=delivery` 限定作品，不順帶處理留言。

## 核准後先做的主機核對（尚未執行）

1. 保存實際部署 SHA、DB 完整備份與 media 檔案備份，記錄還原方式及備份保留期限。
   停止所有上傳／舊 IIS workers；不要讓舊 API 在新純檔案資料建立後仍服務。
2. 在主機使用 PowerShell 7.2+ 執行唯讀 preflight：

   ```powershell
   .\scripts\Test-ArtDeliveryStorage.ps1 -ApiDeployPath D:\www_root\ToBeClarify_API -WebDeployPath D:\www_root\ToBeClarify_web
   ```

   它核對共同父層、全部 IIS site/application/virtual-directory 的實體映射、API app pool
   身分及 reparse points。輸出必須顯示 media 未被任何 IIS 根目錄或虛擬目錄涵蓋；
   它不建立資料夾、不修改 ACL／設定。不允許將 `D:\www_root` 整體當成公開站台根目錄。
   另核對 IIS rewrite／CDN／共享設定沒有映射此目錄。
3. 由主機管理者建立 `D:\www_root\media`，對 preflight 查得的 **實際 API 執行身分**
   配置繼承到檔案與子目錄的 Modify（讀寫、更名及刪除暫存檔）權限，維運／備份身分
   依職責配置。保留 SYSTEM／Administrators；審查並移除不必要的廣泛讀取權限。
   不授權匿名 IIS、Web app pool 或 Everyone。不要猜 app pool 名稱或重設整個父目錄 ACL。
4. 在實際 API 身分下測試建立暫存、flush、更名、讀回、刪除測試檔案；確認磁碟空間及告警。
   `media` 位於部署目錄外，兩個部署腳本只更新自己的部署目錄，因此保留；
   另檢查外部部署／備份／清理排程沒有對父目錄做同步清除。程式 rollback 備份不包含此目錄。

## Schema、發布及回填順序（均須另外確認）

1. 遵守原 API checkout 的本機 `docs/DATABASE-MIGRATION-RUNBOOK.md` 及
   [資料庫權限規範](DATABASE-PERMISSIONS.md)：核對目標及 grants、保存表結構、
   schema checksum、筆數／總大小／各列 SHA-256，不輸出憑證。若 runbook 尚未合併，使用
   原 API checkout 已授權的本機文件與 ignored 設定，不複製設定進 worktree。
2. 查核 `20261020_03_private_image_files.sql` 是否已套用，再依已審閱內容套用
   [`20261020_04_art_delivery_storage_key.sql`](../db/migrations/20261020_04_art_delivery_storage_key.sql)。
   後者只新增 nullable STORAGE_KEY，不縮小／移除 FILE_BYTES；正式先前 LONGBLOB 狀態保持。
3. 設定 PrivateRootPath、部署本版 API，維持暫停寫入並停止所有舊 workers。
   正式沒有 API 測試主機；不能以 API dev build 當作正式功能驗收。
4. 從 API 部署目錄、以同一設定與可寫檔的維運身分執行：

   ```powershell
   dotnet ToBeClarify.Api.dll --private-image-migration=copy --private-image-category=delivery
   dotnet ToBeClarify.Api.dll --private-image-migration=verify --private-image-category=delivery
   ```

   keyset 每批 100 個 ID、每次只鎖一列；SELECT 僅傳長度與 SHA2，BLOB 用 1 MiB SUBSTRING
   分塊傳回，適用既有 16 MiB max_allowed_packet。檔案保留原始 bytes，不重新編碼。
   逐檔驗證長度與 SHA-256，與 DB 端 SHA2 比較；最後比對前後總筆數、總大小與實際讀檔大小。
   包含 soft-removed 附件，排除雲端連結。失敗以非零結束，既有 bytes 不刪除；
   已提交列維持有效，可修正後重跑，不是整批單一交易。不要並行執行兩個 maintenance 程序。
   交接的 17 筆／最大 9,957,796 bytes 僅為歷史基準，正式執行須重新取得當下值。
5. 人工驗收：Admin 授權讀圖；顧客正確 UID／領取碼可讀；錯誤碼、未 ready、已移除附件
   均不可讀；Web 既有 same-origin URL 可用；磁碟路徑及 `/api/client/media/{id}` 無法取得私密圖。
   驗收通過才恢復寫入。本次 Web PR 僅部署說明，無 API 契約或前端 runtime 修改。

## 寫入一致性、備份與回滾窗口

上傳先在目標資料夾寫唯一 `.tmp`，flush 至磁碟，原子更名成不覆寫的最終檔，驗證長度與 hash，
才提交附件記錄。DB 失敗或 commit 結果不確定時保留檔案，避免刪掉已提交附件；崩潰可能留下
暫存／孤兒檔。report-orphans 只列出候選，不刪除；STORAGE_PATH 與 STORAGE_KEY 都視為引用。
soft-remove 只改 DB 狀態，檔案保留。清理須另外核准、停寫、再次查引用並備份。

備份至少涵蓋 MariaDB 完整資料、`D:\www_root\media`、設定與 ACL；同一停寫窗口建立配對備份，
備份落到不同磁碟／主機並實際做還原演練。可用 `robocopy <media> <backup> /E /COPY:DAT /DCOPY:DAT`
複製檔案（依維運規範處理回傳碼），另保存 `icacls /save`；不要用會刪除目的檔的 /MIR。
DB-only 或程式部署 rollback 不足以還原純檔案附件。

回滾窗口預設保留所有舊 BLOB、舊 STORAGE_PATH、原 private root、配對備份；由使用者另行確認
驗收完成與保留期限後才考慮清理。這次不執行 clear-legacy。
若必須回到舊 binary：停寫、備份，使用**本版工具**先還原所有純檔案圖的 BLOB：

```powershell
dotnet ToBeClarify.Api.dll --private-image-migration=restore-legacy --private-image-category=delivery
dotnet ToBeClarify.Api.dll --private-image-migration=verify-legacy --private-image-category=delivery
```

restore-legacy 僅在 BLOB 為 NULL 時，以同一列 transaction 用每次 1 MiB CONCAT 重建；
提交前再次比對 DB 長度／SHA2。已有 BLOB 只驗證，不覆寫；也接受尚未 copy 的舊列，
因此回填中途失敗仍可復原。verify-legacy 唯讀確認每筆都已恢復 BLOB 並核對檔案／hash。
所有舊 API 會讀取的圖片類別
都必須完成還原（若包含本版之前新增的留言純檔案圖，也須另外明確處理 guestbook），才切回
舊 binary；保留新增欄位及檔案，避免破壞之後重新推進。若沒有檔案或配對備份，停止回滾操作。

## 本次準備工作的驗證紀錄

- `dotnet build -c Release`、`dotnet publish ToBeClarify.Api.csproj -c Release` 通過。
- `dotnet run --project tests/PrivateImageStorage/PrivateImageStorage.csproj -c Release`：
  26 項檢查通過，包含實際磁碟讀寫、immutable retry、損壞偵測、路徑限制、legacy fallback、
  9,957,796 bytes 以 10 次不超過 1 MiB 讀取重建、短讀與取消失敗處理。
- `git diff --check` 與 preflight 腳本的 PowerShell parser 檢查通過。
- 本機沒有隔離的 MariaDB 測試主機；未實際執行 SQL 回填／CONCAT 還原或 migration，
  不把上述分塊重建檢查稱為 DB 整合測試。正式列鎖／packet／checksum 驗收仍待核准後執行。
- 未在 IIS 主機執行 preflight、變更 ACL／設定、部署、回填或清除 BLOB。
- 建置仍出現既有 ImageSharp 3.1.12 的 NU1902／NU1903 警告；本次未更動套件版本。
- migration `20261020_04_art_delivery_storage_key.sql` 的 SHA-256：
  `5c513c81ac34b7d47ee676cf6c9959884c590b10aa176a7045d94ae57526a74c`。
