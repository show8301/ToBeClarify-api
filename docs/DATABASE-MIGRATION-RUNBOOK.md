# 換版 migration：本機 DBA 連線作業流程

生效日期：2026-10-17。由使用者明確指定；在使用者另行變更安全連線方式前，本文件是換版 migration 的預設作業方式。

## 適用與授權

使用者是協作專案的 repository collaborator，沒有 Repo settings、GitHub Environments、Secrets 或 runner 的設定能力。換版不以新增這些設定、安裝資料庫連接器或導入 Flyway 為前提。

對使用者要求的換版，若包含必要且已審閱的 migration，沿用本機不納入 Git 的連線設定，透過使用者授權的 DBA 帳號執行。無須每次重新選擇連線方式或再次要求提供同一帳號的授權。執行環境的網路／工具權限仍依其規定處理。

這不是任意 SQL、權限提升、帳號變更或破壞性資料清除的概括授權。移除資料、欄位、索引或資料表等變更仍依[資料庫權限規範](DATABASE-PERMISSIONS.md)審核，不因帳號有權限就自動執行。

## 連線與憑證

- 現行入口是 API checkout 的 `appsettings.Local.json` 中 `ConnectionStrings.DefaultConnection`，該檔已由 `.gitignore` 排除。只在程序記憶體中讀取；不要輸出原始 JSON 或完整連線字串。
- 使用專案已安裝的 `MySqlConnector`，可由 PowerShell 載入已建置的 `bin/Release/net10.0/MySqlConnector.dll`；需載入相依組件時，使用相容的已安裝 .NET runtime，先載入 `Microsoft.Extensions.DependencyInjection.Abstractions.dll` 與 `Microsoft.Extensions.Logging.Abstractions.dll`。
- DLL 或 runtime 不存在時，先依專案版本建置或使用相容的本機執行入口；不要為了 migration 改變 API 套件版本。
- 不將密碼、完整連線字串或其他憑證複製到工具原始碼、命令列參數、文件、Git、輸出或備份。不要從聊天歷史重新硬編碼密碼。
- 密碼輪替後，以本機設定的最新值為準。設定缺漏或認證失敗時，請使用者以安全方式更新本機設定，不要求在聊天貼密碼，也不自行改用 API 服務帳號。

## 每次執行順序

1. **核對換版範圍。** 讀取 `db/migrations` 對應檔案及前置版本，只執行這次換版所需 migration。檢查檔案內容及 SHA-256，不整批重跑歷史 SQL。確認本機設定未被 Git 追蹤。
2. **唯讀確認目標。** 只輸出必要且不含密碼的主機、port、資料庫及連線帳號；以 `SELECT CURRENT_USER(), DATABASE(), VERSION()` 確認實際目標。從 `SHOW GRANTS FOR CURRENT_USER()` 判定所需權限，輸出能力摘要而非原始授權內容。核對前置欄位與 migration 已套用狀態；不猜測有無套用，也不在錯誤目標上重試。
3. **保存執行前證據。** 在本機非版本控管位置保存受影響資料表的結構，記錄路徑。依變更內容保存必要的筆數、關鍵總額或其他不含敏感資料的比較基準。涉及資料轉換時另準備合適的備份與復原方案；表結構快照不等同完整資料備份。
4. **執行核准 SQL。** 使用已確認的同一連線執行審閱過的 migration，設定適當 timeout。不可因遇到權限錯誤便改寫為更危險的 SQL 或換高權限身分。MariaDB DDL 可能隱含提交；失敗時立即停止，先核對部分套用狀態，不假設整批已回滾，也不盲目重跑。
5. **驗證結果。** 核對預期表、欄位、索引與必要資料回填，並比較既有資料筆數及金額等不變條件。只報告確實完成的檢查；schema 套用成功不代表交易並行或完整 UI 已驗收。
6. **記錄與發布。** 記錄 migration 檔名、checksum、日期、非敏感驗證結果與備份位置；不記錄憑證。驗證成功後才發布依賴此 schema 的 API，再部署 Web dev。API 仍只有 main 正式主機；Web main 仍須等使用者確認 dev 後推進。

換版檢查遵守兩個專案的 AGENTS.md。dev 發布不因採用本流程而新增自動化測試套件；保留既有建置、靜態／設定檢查、部署結果與營運 HTTP 健康檢查。

## 首次成功採用紀錄

2026-10-17 已使用此方式套用 [20261017_02_shift_payroll_snapshots.sql](../db/migrations/20261017_02_shift_payroll_snapshots.sql)。實際目標為 MariaDB，執行前已核對 CREATE／ALTER／INSERT／SELECT 能力並保存相關表結構；四個版本欄位與舊出勤標記核對成功，既有結算輸入／結果筆數及應付總額不變。

後續 API main 發布版本為 `25a1fa4001c24a94ee63869351c9fb45ac062e17`，Web dev 為 `146c0965724d648175270d2f4ca515ce004c885c`，兩者部署工作流成功。這份紀錄不宣稱完整交易並行與瀏覽器操作驗收完成。

## 未來變更連線方式

Flyway、GitHub Actions migration runner 或專用 MCP 可另案導入；需要先取得設定權限、確認連線與既有版本基準，並由使用者明確切換。本流程不因曾建議其他工具而自動失效。
