# 結算規則版本選用

規則以日期類型及生效日期新增，不覆寫既有版本。`GET /api/admin/settlement/rules` 提供版本列表；建立成功不代表已替換既有時段採用的規則。

## 未正式結算

`PUT /settlement/inputs` 可指定 `ruleVersionId`。服務端依 ID 查詢已儲存規則並驗證日期類型，保存其 ID 與完整快照，並使原試算失效。規則 ID 保留既有 `settlement-default-normal` 等非 UUID 種子值的相容性。載入既有時段時先讀其快照或已保存 ID，不因後來新增規則而自動換版。

未指定版本的新時段依營業日期取適用規則。手動選用可將生效日晚於營業日期的規則套用歷史日期，Web 必須明示此情形。

## 已正式結算

原規則快照及金額保持，直接保存輸入仍受 finalized 保護。經理／開發人員可在重新結算 `adjustments.ruleVersionId` 指定已儲存規則；預覽包含原／新 ID、生效日、全員薪資、公司收入與差額。規則與目標日期類型必須相符；未指定時沿用原快照。

預覽 token 包含指定規則及輸入來源，預覽後切換規則不能沿用舊 token。規則 ID 不同但薪資相同時仍屬有效版本變更。確認仍要求原因、版本檢查與操作 UUID，沿用原有共用鎖、交易、支付餘額與前後快照稽核。新快照於確認成功後才成為目前規則；歷史快照留在原有 `SETTLEMENT_AUDIT_LOG`。

## 驗證與發布

不需要 migration。`dotnet run --project tests/SettlementRecalculation -c Release` 驗證舊種子 ID、未結算保存與重讀、錯誤規則及日期類型、正式結算鎖定、換規則預覽、零金額差異與 token 失效。dev 換版仍依 AGENTS 預設跳過測試套件。
