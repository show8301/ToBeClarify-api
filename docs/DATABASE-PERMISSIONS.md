# 資料庫帳號權限限制

> 重要：目前 API 使用的資料庫帳號是受限帳號，僅允許 `SELECT`、`INSERT`、`UPDATE`（以及已獲核准的非破壞性結構調整）。帳號沒有刪除資料、刪除資料表或其他破壞性 DDL 的權限。

## 開發與部署規則

- 不得在應用程式、背景工作、一次性工具或 migration 新增 `DELETE`、`DROP`、`TRUNCATE`、`RENAME` 等破壞性 SQL。
- 不得以 `DELETE` 清理訂單、訂單明細、指名關聯、媒體資料或內容資料；保留資料供營業額、薪資與稽核報表使用。
- 訂單異動應使用既有的狀態更新、取消／失效欄位、`IS_ACTIVE`／`IS_ENABLED` 或新增稽核紀錄等方式實作。若目前資料模型沒有適用欄位，先提出 migration 與保留年限設計，再實作功能。
- Schema migration 只能採向前相容且不刪資料的做法，例如新增欄位、索引或資料回填；任何移除欄位、索引、表格或資料的 migration 都必須由具備 DDL 權限的 DBA 另行審核與執行，不能由目前 API 帳號執行。
- `HTTP DELETE` 端點、名稱含 `Delete`／`Remove` 的 repository/service 方法，以及媒體清理程式目前屬於既有遺留程式；在完成軟刪除／停用改造前禁止從 UI、API client、排程或人工操作呼叫。

## 提交前檢查

1. 搜尋 SQL 與 Dapper command 是否含 `DELETE`、`DROP`、`TRUNCATE` 或其他刪除資料的語句。
2. 確認 migration 不會移除既有資料、欄位、索引或表格。
3. 確認取消、下架、移除畫面項目都保留歷史資料，並以狀態或有效期限控制可見性。
4. 若確實需要資料清除，建立獨立的 DBA 變更申請；不要把高權限帳號或刪除權限加入 API 連線字串。

## 目前已知需要改造的舊程式

- `src/Repositories/Ordering/OrderingRepository.cs` 的訂單明細刪除流程。
- `src/Repositories/Admin/Content/AdminContentRepository.cs` 的內容、店員、相簿與選單刪除流程。
- `src/Services/Media/AdminMediaUploadService.cs` 的媒體資料清理 SQL。
- `db/migrations/20260829_02_drop_admin_display_name.sql` 與 `20260829_03_enforce_admin_staff_relation.sql` 含移除欄位／索引操作，只能由 DBA 依權限另行處理；不可使用受限 API 帳號執行。

