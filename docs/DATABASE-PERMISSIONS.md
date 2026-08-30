# 資料庫帳號權限限制

> 重要：本文件描述的是「直接使用 SQL 操作資料庫」時提供的受限帳號。該帳號僅允許 `SELECT`、`INSERT`、`UPDATE`，沒有刪除資料、刪除資料表或其他破壞性 DDL 的權限。

API 呼叫與直接 SQL 維護是兩個不同的操作邊界。API 是否能刪除資料，取決於 API 部署所使用的連線帳號、端點權限與業務規則；API 可以保留並執行受控的 `DELETE` 業務流程，不得把本文件的 SQL 維護帳號限制誤套用成 API 功能限制。

## 開發與部署規則

- 不得使用本文件所指的受限 SQL 帳號執行 `DELETE`、`DROP`、`TRUNCATE`、`RENAME` 等破壞性 SQL；遇到權限錯誤時不得反覆重試或改用更危險的語句。
- API 的 `HTTP DELETE` 端點與 repository/service 刪除方法可以存在並依 API 連線帳號執行；仍須遵守角色權限、業務狀態檢查、交易與稽核紀錄。
- 訂單是否採硬刪除或狀態取消，依營運規則與報表保留需求決定；不能因直接 SQL 帳號受限，就假設 API 端點一律禁止刪除。
- Schema migration 若包含移除欄位、索引、表格或資料，必須由具備 DDL／刪除權限的 DBA 另行審核與執行；不得使用提供給直接 SQL 維護的受限帳號套用。
- 不得把具備刪除權限的 API 連線字串、密碼或高權限帳號放入文件、migration 工具或提交到 repository。

## 提交前檢查

1. 執行 migration 或手動 SQL 前，先確認目前使用的是哪一個連線帳號，不要把受限維護帳號當成 API 服務帳號。
2. 以受限維護帳號執行 SQL 時，搜尋並禁止 `DELETE`、`DROP`、`TRUNCATE` 或其他刪除資料的語句。
3. API 新增刪除流程時，確認端點授權、業務狀態、交易、稽核與報表保留規則。
4. 若直接 SQL 確實需要資料清除或破壞性 schema 變更，建立獨立的 DBA 變更申請；不要把高權限帳號寫入 repository。

## 目前已知涉及刪除操作的程式

- `src/Repositories/Ordering/OrderingRepository.cs`、`src/Repositories/Admin/Content/AdminContentRepository.cs` 與 `src/Services/Media/AdminMediaUploadService.cs` 含 API 刪除流程；是否可執行由 API 部署連線帳號與端點授權決定。
- `db/migrations/20260829_02_drop_admin_display_name.sql` 與 `20260829_03_enforce_admin_staff_relation.sql` 含移除欄位／索引操作，只能由 DBA 依權限另行處理；不可使用直接 SQL 維護的受限帳號執行。
