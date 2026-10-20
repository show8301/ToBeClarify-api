# 當日計薪職位更正

經理與開發人員可沿用出勤 `confirm_payroll_role` 操作，更正當日已確認的主要計薪職位。請求帶 `expectedPayrollSnapshotVersion`、操作 UUID 與原因。服務員與指名人員可互換；經理、幕後仍受原有資格檢查限制，不變更帳號登入權限。舊客戶端仍可確認未結算或尚未確認職位的舊出勤；更正已有職位的正式結算必須帶預期快照版本。

更正適用人員的整個營業日。既有 `STAFF_PAYROLL_SNAPSHOT_AUDIT` 保存前後職位、快照版本、原因與操作者，不修改原班表審核紀錄；無新增 schema migration。尚未正式結算的試算標為 draft 並增加輸入版本。正式結算的工時輸入、金額、來源快照及版本保持原樣，直到經理確認重新結算。

結算讀取／試算以獨立輸入副本對齊較新的職位快照，沿用原主要輸入 ID、分鐘、活動時數、備註及補打卡核准來源。僅更新職位及版本不會要求重新核准相同工時。多個舊主要職位的歧義資料仍須人工核對，不自動刪掉其中一份薪資。幕後兼任轉為主要幕後時合併參與輸入，不重複計算。若人工另改分鐘，原工時核准來源按既有規則失效。

正式結算 overview 的 `pendingPayrollRoleCorrections` 列出原職位與最新職位；`staffInputs` 提供待重算輸入，`results` 與 `summary` 保留已結算金額。最終步驟預覽會重新計算全員分潤、總薪資、公司收入及已發薪差額；即使金額相同，快照版本變化仍可建立稽核版本。確認時沿用來源 token、交易鎖與原有 V2/V3 歷程；更新輸入的職位／版本及核准欄位，再保存新結果。新職位在預覽後再次變更會使舊 token 失效。

既有「同日多份正式結算範圍有歧義」保護保持生效；不藉此變更新增同日分段職位計薪或重複全日薪資。

驗證：

- `dotnet run --project tests/SettlementRecalculation -c Release`：職位輸入遷移、核准保留、舊快照不變、同事分潤差異與金額相同的版本更正。
- 加 `-- --payroll-integration` 並設定 `PAYROLL_ROLE_TEST_CONNECTION`：僅接受 `127.0.0.1` 非 3306 連接埠、尚不存在的 `codex_payroll_*` 資料庫。建立合成 schema，驗證正式金額保留、未結算失效、稽核、資格、回滾與並行版本衝突，不讀專案連線設定或正式資料。
- 既有 `tests/AttendanceSettlementSync` 與 `tests/ShiftPayroll` 覆蓋出勤、計薪與快照衝突規則。
