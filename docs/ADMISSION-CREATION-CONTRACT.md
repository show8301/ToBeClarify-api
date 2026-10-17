# 開碼入場收費：第一階段契約

日期：2026-10-17。狀態：第 1 部分及後續開碼收款交易、Web 收款表單已加入原始碼；未發布、未執行 migration。整批修改及驗證限制見 [整批實作／上版準備](../../ToBeClarify-web/docs/service-workbench-admission-implementation-20261017.md)。

## 1. 有效收費來源

| 資料 | 唯一來源 | 規則 |
| --- | --- | --- |
| 新流程入場費 | `ORDERING_ADMISSION_POLICY.ADMISSION_AMOUNT`，`ID='default'` | 整數 G，0–1,000,000,000,000；NULL 為未設定，明確設定 0 才代表零元入場。 |
| 餐點折抵額度 | `ORDERING_SETTINGS.MINIMUM_MEAL_CREDIT`，`ID='default'` | 沿用既有非負 Int32 額度；與收費無相等關係。 |
| 優惠／折讓 | 本次入場確認的 `discountAmount` | 不大於入場費；例外由 manager／developer 填原因。 |
| 本次實收 | `received` 時為入場費減優惠；`unpaid`／`waived` 時為 0 | 必須明確確認，不由開碼成功推定收款。 |

不解析 `PRICING_RULES.PRICE_TEXT`、標題或說明，也不使用 `MinimumMealCredit` 推定入場單價。migration 不從文案或歷史開碼推算金額，初值為 NULL。發布後需經理經版本核對、明確設定收費；缺設定時新預覽回報 `ADMISSION_POLICY_NOT_CONFIGURED`。

`PRICING_RULES` 繼續作為展示資料。flow v2 結算已改為逐筆入場應收減折讓，再沿用既有淨實收／保留款分配；舊版估算只保留在 flow v1，兩套來源不疊加。本階段不重算歷史結算、不補寫歷史收款。

## 2. 已加入的 API

所有端點需要既有後台登入，回應沿用 `ApiResponse<T>`，標示 no-store。瀏覽器走同源 `/api/admin/...`。

### GET／PUT `/api/admin/order-session-admission-policy`

僅 manager／developer。GET 回傳：

```json
{
  "admissionAmount": null,
  "version": 0,
  "mealCreditAmount": 60000,
  "amountSource": "ordering_admission_policy",
  "creditSource": "ordering_settings.minimum_meal_credit"
}
```

PUT 範例金額僅說明契約，不代表已設定正式收費：

```json
{ "admissionAmount": 100000, "expectedVersion": 0, "reason": "確認本期入場收費" }
```

`admissionAmount`、`expectedVersion` 必須存在。明確傳 NULL 可撤銷收費設定，使後續預覽停止；不能把省略欄位當作撤銷。原因必填且最多 500 字。以交易鎖定政策及對應設定，版本相符才更新；每次版本遞增並保存前後金額、原因、操作人及時間。版本衝突回傳 HTTP 409／`ADMISSION_POLICY_VERSION_CONFLICT`。此 API 不修改餐點額度。

### GET `/api/admin/order-sessions/admission-preview`

一般店員可取得自己要確認的預覽：

```json
{
  "contractVersion": 1,
  "businessPeriodId": "period-id",
  "businessDate": "2026-10-17",
  "periodVersion": 3,
  "policyVersion": 1,
  "admissionAmount": 100000,
  "mealCreditAmount": 60000,
  "expiresAt": "2026-10-17T12:05:00+08:00",
  "pricingRevision": "64-character-lowercase-hmac",
  "confirmedCreationAvailable": true,
  "legacyCreationAllowed": true
}
```

已完成 writer，`confirmedCreationAvailable` 為 true；實際可用仍須完成 migration、開啟 flow v2 營業期及配置有效數值收費。`pricingRevision` 真實值為 64 位小寫十六進位；它使用既有 OrderingToken HMAC、獨立用途標記，綁定操作人、營業期 ID／日期／版本／flowVersion、政策版本、入場費、折抵及到期時間。期限為 5 分鐘，不保存預覽或點餐憑證。

必須有未結算、open 的 flow v2 營業期及有效數值政策。缺設定不當成零元；flow v1 回報 `FINANCE_LEGACY_READ_ONLY`，不自動升級正在營業的舊期。

### GET `/api/admin/order-sessions/creation-operations/{operationId:guid}`

只查已提交的開碼交易快照。原操作人或 manager／developer 可看；其他店員與查無資料均為 HTTP 404／`ADMISSION_CREATION_OPERATION_NOT_FOUND`，不洩漏操作是否存在。

結果欄位為 operationId、sessionId、admissionId、businessPeriodId、businessDate、admissionAmount、discountAmount、mealCreditAmount、admissionStatus、receivedAmount、chargeRecordId、receiptRecordId、cashPeriodId、accountVersion、recordedAt、recordedBy、credentialAction。

- 此為「建立當時」快照；後續退款、更正與即時餘額仍讀既有 finance account，不能用原實收快照取代目前帳況。
- 不含 orderToken、orderUrl、shortCode 或 recoveryCode，也不自動重發。`credentialAction` 為 `explicit_reissue_if_needed`；原始成功畫面還有點餐資料時直接沿用，遺失時才由店員明確執行既有重發流程。
- HTTP 404 不證明沒有在途交易。客戶端必須保留原 operation ID／原內容，不能因 404 換新 ID 再收款。
- 此表只由新版交易 writer 寫入；舊開碼不會補造此快照。

## 3. 第二階段使用的確認與建立契約

已提供 C# DTO、Web TypeScript 型別、服務驗證與 **POST `/api/admin/order-sessions/with-admission`**，共用開碼表單已呼叫此新端點。

```json
{
  "operationId": "a-client-generated-uuid",
  "gameId": "人物",
  "serverName": "巴哈姆特",
  "customerName": "顧客",
  "maxNominatedStaff": 1,
  "admission": {
    "businessPeriodId": "period-id",
    "pricingRevision": "the-preview-hmac",
    "expiresAt": "2026-10-17T12:05:00+08:00",
    "mode": "received",
    "discountAmount": 0,
    "paymentConfirmed": true,
    "confirmedReceivedAmount": 100000
  }
}
```

建立時不接受瀏覽器指定入場單價、額度或收款營業期；由 API 在交易內核對最新政策／settings／營業期，重新算 HMAC 與實收。人物、伺服器、UID 權限沿用現有流程；operation ID 必須為非空 UUID。

確認驗證規則：

- 已到期（含剛好等於到期時間）回傳 HTTP 409／`ADMISSION_PREVIEW_EXPIRED`。
- 操作人、營業期、價格、政策版本、額度或期限遭變更，回傳 HTTP 409／`ADMISSION_PREVIEW_CHANGED`。
- 正常 `received`：`paymentConfirmed=true`，`confirmedReceivedAmount=入場費-discountAmount`。
- `unpaid`：`paymentConfirmed=false`、實收 0；`waived` 另要求全額折讓。兩者及任何正數優惠都需要 manager／developer 與非空原因。
- 未明確確認或實收不符回報 `ADMISSION_PAYMENT_CONFIRMATION_REQUIRED`；無資格例外回報 `ADMISSION_EXCEPTION_FORBIDDEN`。

目前 writer 的交易順序：

1. 先依 operation ID 查已提交結果並核對 actor／canonical payload hash；同 ID 異內容回報衝突。已完成的原請求即使預覽後來過期、關店或政策改變，仍回原結果，不重新做交易。
2. 對新操作依一致順序鎖營業期、政策／settings、顧客入場及帳款；使用相同 connection／transaction 呼叫確認驗證，再建立入場、應收／折讓／實收與折抵初值。
3. 同交易最後寫入 `ORDERING_SESSION_CREATE_OPERATIONS`。操作 ID、入場紀錄及入場事件唯一鍵防止並行重複。任一失敗全部回滾。
4. canonical payload hash 包含正規化身份、UID、指名上限及完整收費確認；不包含明碼點餐憑證。初次成功回傳 `{ issued, result }`；重試成功回傳 `{ issued: null, result }`。
5. 點餐憑證僅隨初次成功提供，不放進操作表、result JSON、audit 或日誌。查回後必須明確重發才輪替憑證，不把重試當作重發。

## 4. 舊 Web 相容與啟用順序

- 第一階段保留原 POST `/order-sessions` 的 request／response，沒有確認欄位仍可開碼；這條路徑不新增或推定入場實收。新版確認 body 必須使用新路徑，禁止送到忽略未知欄位的舊 handler。
- 新增設定 `OrderingAdmission:RequireConfirmedCreation`，預設 false。true 時原 POST 在任何 DB 寫入前回 HTTP 409／`ADMISSION_CLIENT_UPGRADE_REQUIRED`。
- 目前只完成本地實作，尚不可在正式服務啟用 true。發布順序為必要 migration → 相容 API → Web dev 驗收及 Web 正式推進 → 啟用 gate。啟用後舊瀏覽器顯示更新提示，不偽造確認收款。
- gate 僅限制新開碼。查詢、尋回、明確重發及既有訂單不受影響；重發不重收、不重給額度。
- API 唯一正式主機仍由 main 發布；Web 依 dev → 使用者確認 → 人工 main 流程。本階段沒有發布授權或發布操作。

## 5. 必要 schema

[20261017_03_admission_creation_contract.sql](../db/migrations/20261017_03_admission_creation_contract.sql)只新增三個表：

| 表 | 用途與不變條件 |
| --- | --- |
| `ORDERING_ADMISSION_POLICY` | singleton 數值收費及版本；NULL 初值，不從文字或額度回填金額。 |
| `ORDERING_ADMISSION_POLICY_AUDIT` | 每次政策變更的前後金額、版本、原因、時間與操作者；版本唯一。 |
| `ORDERING_SESSION_CREATE_OPERATIONS` | 已提交交易的識別碼、payload hash、收費與收款快照及關聯；operation／session／admission 各自唯一，不保存憑證。 |

沿用 `ORDERING_SESSION_ADMISSIONS`、`ORDERING_FINANCE_ACCOUNTS`、`ORDERING_FINANCE_RECORDS` 記帳，不把入場費重複寫成餐點單。實收正數必須關聯 receipt 與 cash period；未收／免收快照的實收必須為零。政策及操作 schema 限制非負額度與合法金額關係。

本檔未執行。後續發布前依 [本地 DBA 程序](DATABASE-MIGRATION-RUNBOOK.md)確認實際資料庫、MariaDB 版本、前置表／欄位、既有結構與權限，保存證據再套用。`IF NOT EXISTS` 不取代 schema 比對。前置版本包含 ordering finance stage 1／2 及營業期 flow v2；新增表不改寫既有營業、帳款或結算資料。

## 6. 驗證範圍

第一階段檢查以 API build、Web 型別／lint／build、純記憶體確認規則及 Web 回應解析為範圍。測試來源：[API checks](../tests/AdmissionContracts/Program.cs)、[Web contract checks](../../ToBeClarify-web/tests/admission-creation-contract.test.mjs)。這些不證明 MariaDB DDL、交易鎖、並行、HTTP 授權或實際收款已驗證；不加入 dev 部署測試流程。

2026-10-17 本地結果：

- API Release build 通過；31 項純記憶體檢查通過，涵蓋政策／額度／營業期／操作人變更、期限竄改／到期、明確零元、實收確認、例外權限、查回資料範圍、缺漏 JSON 欄位及舊開碼 gate。
- Web 4 項契約測試、TypeScript、本次三個 TS 檔案 ESLint、production build 通過；測試未加入部署 workflow。
- API 使用 checkout 既有 `.dotnet/dotnet.exe` 的 .NET 10 SDK。NuGet 弱點索引無法連線出現 NU1900；沒有更改套件或關閉稽核。Web build 保留 chunk 大小／路由分類提示。
- 未啟動 API 或本地預覽、未連資料庫、未套用 migration、未建立真實收款或執行 HTTP mutation，尚未驗證資料庫 DDL／鎖定與實際登入後畫面。
