# 上班計薪快照與結算整合

日期：2026-10-17。狀態：本地實作，migration 尚未執行，尚未發布。

規則、調整清單、相容性與檢查結果見 Web [整合紀錄](../../ToBeClarify-web/docs/settlement-payroll-integration-plan-20261017.md)。

- 先執行 [20261017_02_shift_payroll_snapshots.sql](../db/migrations/20261017_02_shift_payroll_snapshots.sql)，再發布匹配的 API。API 沒有獨立測試主機，只有 main 發布正式服務。
- STAFF_PAYROLL_SNAPSHOTS 以人員＋營業日識別，不支援同營業日多個主要計薪職位。既有出勤僅種入未確認職位，不能從目前班表追溯猜測。
- 既有 attendance action 新增 confirm_payroll_role；僅經理／開發者可操作，須填 payrollRole 與 reason。目標的有效 developer／manager 資格由 API 檢查。
- 結算 overview 新增 payrollAssignments、run.inputVersion、run.calculationVersion 與 staffInputs.payrollSnapshotVersion。保存輸入須帶 expectedInputVersion，列須帶 payrollSnapshotVersion；舊版保存請求會被拒絕，需協調 Web 更新窗口。
- 職位更正、補登核准及訂單金額調整會使試算失效。正式結算鎖定營業期與薪資結果在同一交易內完成。
- 同日正式結算後，reopen 與新時段全日計薪被拒絕；既有其他草稿也不能正式結算。改走既有 correction／payment，原金額不變。
- 歷史已結算的未確認職位可補確認，但不重算歷史金額；已有非空職位且當日已結算則不可改寫職位。

本地 Release build 與三組計薪回歸通過；NuGet 弱點索引受網路限制有 NU1900 警告。尚未驗證真實資料庫 migration、SQL 交易並行及 Web 瀏覽器操作。
