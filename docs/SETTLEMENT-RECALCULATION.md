# 已正式結算班次的補正後重新結算

適用於出勤、履約或帳款已補正，但正式結算仍保留舊工時與薪資的情況。此功能須由經理或開發人員操作，不建立新營業時段，也不再次登記付款。

## 操作及契約

1. 完成來源資料補正後，於營運結算的「正式結算」步驟選擇「補正後重新結算」。
2. POST /api/admin/settlement/recalculation/preview 接受 businessDate、sessionNo。一致性唯讀預覽會套用最新有效出勤及帳款，沿用原始結算規則與舊制入場費快照，列出全員分鐘、計薪時數、薪資差額及已登記淨支付後的餘額。
3. 填寫 1–500 字補正原因，核對全員差額與既有事後更正後確認。
4. POST /api/admin/settlement/recalculation/confirm 另需預覽的 sourceToken、expectedCorrectionVersion、GUID operationId、reason。無異動、未關店、未完成服務、未核准補打卡、規則快照遺失或計薪異常均不可確認。
5. GET /api/admin/settlement/recalculation/history 顯示各補正版本的時間、原因及薪資總額差異。

三個入口皆採用 AdminManager 政策。現有一般計算、保存輸入與重開時段入口仍拒絕改寫已正式結算班次。

## 交易、版本與歷史

- 確認採用 Serializable 交易，先鎖定營運控制列和原結算列，再於同一交易讀取全部來源及支付、更正事件並重新計算。來源指紋與版本必須符合已核對的預覽。
- 使用同一交易更新有效計薪輸入、有效薪資結果、現金摘要、輸入版本與補正版本；結算保持 finalized，保留首次正式結算時間及人員。
- 原結算與新版計薪輸入、結果、原規則和原因以完整業務快照寫入現有 SETTLEMENT_AUDIT_LOG，動作為 settlement_recalculated。原始快照在該次紀錄的 BEFORE_JSON，新版快照在 AFTER_JSON；後續補正繼續新增紀錄。
- operationId 使用稽核紀錄主鍵。在結算列鎖定後核對已完成操作；相同請求重送回傳原操作結果，編號被不同內容重用則拒絕。
- 支付／追回與既有事後差額事件皆不修改。預覽餘額為「新版應付 + 既有有效事後差額 − 已登記淨支付」。未指定人員的事後差額仍需管理員分配核對，不能推定歸屬。
- 若同日有多個正式結算時段，因現有收入來源是全日範圍，拒絕重算以免重複全日計薪。

使用現有表及欄位，無需 migration。發布順序為 API 既有 main 流程，再將 Web 發布到 dev；正式 Web 仍須使用者驗收後由 dev 推進 main。本次開發不修改正式資料或自動執行任何班次的重新結算。

## 驗證

記憶體回歸入口為 [SettlementRecalculation](../tests/SettlementRecalculation/SettlementRecalculation.csproj)，涵蓋 57 → 117 分鐘、1 → 2 小時、原快照不變、原規則、支付／追回及事後更正、來源指紋與異常阻擋。此檢查不連資料庫，不能代替 MariaDB 並行交易或瀏覽器驗收；dev 發布仍依 AGENTS.md 預設跳過自動化測試。
