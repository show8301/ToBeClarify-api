# 整單指名服務：開始、漏按補登與代表權

2026-10-28。Web 完整點單管理與指名營業工作台共用此契約。API 沒有測試主機；本次 API 發布 main，Web 發布 dev 後由使用者驗收。

## 操作契約

`GET /api/admin/orders/{id}/fulfillment/service-eligibility` 回傳 snapshot、checkedAt，以及 accept、start_now、backfill_scheduled、backfill_custom、complete 各自的 enabled、reasonCode、reason、targets 與 conflicts。

每位 target 有自己的有效開始、服務結束、忙碌結束、購買分鐘、休息、版本與 skipped。conflicts 只包含服務人員、訂單識別、時段及重疊分鐘，不含其他顧客姓名或備註。預覽是提示，POST 必須在取得鎖後重新核對。

`POST /api/admin/orders/{id}/fulfillment/service-command` 使用 operationId、snapshot、action、reason、actualTimes。快捷操作不接受實際時間輸入；backfill_custom 必須明確填寫每位待補登人員的 startsAt，並選擇 stillInProgress 或 endsAt。更正原因必填。所有時間為帶 offset 的時間點，台灣顯示固定 Asia/Taipei。

- 現在才開始：伺服器同一時間開始所有未開始的指名，自動承接，保留每人的購買分鐘與休息。服務及休息與後續預約重疊就拒絕，首尾相接允許。不更動其他預約。
- 已按預約開始、忘記按：用各人的目前有效 scheduledStartsAt，按鍵即代表操作者確認這個事實。不是最初的 originalScheduledStartsAt，也不是點擊時間。原本已承接有忙碌鎖時，更新同一筆鎖。
- 已超過預計結束：快捷補登停用。更正浮層要求選擇仍在進行或已完成；完成需實際結束時間。購買時長不因超時而增加。
- 整單完成：只完成已開始且未完成的指名。尚有未開始項目就拒絕；提早完成須在進階入口提供原因。
- 完成、取消的指名會明確略過；交回協調／待跨期的項目會阻擋快捷操作。餐點、包廂、收款仍各自處理。

同單任一有效指名人員或 manager/developer 可代表全單。服務人員、價格快照、分潤歸屬不變，confirmedBy 與 audit 記錄實際操作者。代表權不表示其他人已讀或確認通知，也不授予其他訂單或財務權限。

## 一致性與歷史相容

整單與單項共用 ApplyFulfillmentTransitionAsync。session → order → unit（ID 排序）→ staff（ID 排序）取得鎖，在同一 READ COMMITTED 交易中重新核對 snapshot、代表權、店家承接、營業期、進度與衝突；任一失敗整筆 rollback。相關忙碌區段、Nominee 投影、履約分項、提醒、事件、歷史與操作結果一起提交。

同一 orderId/operationId 只存一次結果；相同請求重試回傳原結果，不同內容回報 OPERATION_PAYLOAD_CONFLICT。單項舊請求在未使用 stillInProgress 時保留原 payload hash，維持既有重試相容。

V1 保留原 FLOW_VERSION 及資料形狀，由投影適配共用時段／代表權與忙碌鎖邏輯；原預約存入 audit 的 before/after。失效 V1 補登在同筆交易恢復原本退還的餐點折抵，餘額不足則整筆失敗。沒有批次升級歷史訂單。

## Schema 與本次 DBA 變更審閱

先依 [本機 DBA runbook](DATABASE-MIGRATION-RUNBOOK.md) 套用 [20261028_01_order_service_operations.sql](../db/migrations/20261028_01_order_service_operations.sql)，再發布 API。

- 操作表新增 scope，原 UNIT_ID 改可空；整單不填第一個 unit 或虛構 ID。
- ORDER_FULFILLMENT_OPERATION_UNITS 為多目標關聯；TARGET_KIND=unit 表示 V2 分項，nominee 表示 V1 指名，父鍵參照原操作。由既有 UNIT_ID 回填舊操作。
- 事件 unique 從 (operationId,eventType) 替換為 (orderId,operationId,unitId,eventType)，使同次整單操作留下每人的事件，仍有去重能力。
- 沒有刪除資料、欄位、表、歷史衝突、通知已讀／確認或 STAFF_BUSY_BLOCKS 唯一鍵。移除的是舊事件索引，並在同一 ALTER 建立更精確的 unique。
- 所需資料庫權限為 SELECT、INSERT、UPDATE、CREATE、ALTER、INDEX。已核對目標 MariaDB 11.2.2；MariaDB 的 REFERENCES 為 unused，見[官方 GRANT 文件](https://mariadb.com/docs/server/reference/sql-statements/account-management-sql-statements/grant)。不需要變更帳號或授權。

應用程式回退時保留此向前相容 schema。已有整單事件後不能直接恢復舊 unique，否則合法多分項事件會衝突；若需 schema 復原，須先停止寫入、匯出新操作與事件，再由 DBA 另案處理，不能刪除新紀錄以便回退。MariaDB DDL 隱含提交，執行失敗須停止並檢查部分狀態。

## 清理與退場

已移除一般開始中自動擠動後續預約的 HandleLateStartConflictsAsync，以及補登直接重複 INSERT 忙碌鎖的分支；Web 移除兩份專用 start-preview 流程、逐人確認門檻與等待其他人文案。

confirm-nominee、nominee response、confirm-addon、V1 transition/backfill-served 保留路由，相容入口改用共用能力；decline、改期、部分餐點／包廂、取消、跨期與早完成仍保留。start-preview 保留給舊頁面，資格依共用政策收斂。

原 UNIT_ID、V1 routes、CanStartNow、Nominee requested 時間投影及歷史衝突表暫不刪除。Web 正式版切換後至少觀測 14 天，確認實際存取紀錄沒有舊呼叫且回退方案相容，才另案移除。此次只產生 [唯讀不一致報告](../db/diagnostics/order_service_consistency.sql)，不批次修復舊資料。2026-10-28 查得：有效時間投影不一致 0、應有忙碌鎖缺失 0、已結案且休息過期但仍 active 的舊忙碌紀錄 37、已結束服務仍 pending 的排程提醒 0、歷史操作關聯缺失 0。37 筆舊紀錄只列報告，未批次變更。

## 發布證據與人工驗收

Migration checksum：`2B8C0AEF5242FF2320211BB0518DAE4A64613B122CBD94B8F063B92EF03B5FAA`。

2026-10-28 執行前後：168 張訂單（V1 49 張，expired 7 張）、總額 99,697,632、餐點折抵 774,690、657 筆操作、665 筆事件、63 筆忙碌紀錄均不變；657 筆舊操作關聯回填，缺漏 0；舊操作與事件逐列 JSON 比對相同。完整結構與資料備份存本機忽略目錄 `.codex-tmp/order-service-migration-backup-20261028-161805/`，不提交資料內容。

本次依 dev 發布規範執行 build、typecheck、lint、publish、部署狀態與 HTTP health；不執行自動化測試套件。既有測試中與逐人確認衝突的預期已調整，保留供明確要求時執行。

使用者在 Web dev 驗收兩個入口：多人任一人代辦、現在開始無衝突／有後續預約／休息重疊／首尾相接、已鎖定服務漏按補登、各人不同有效時間、超時補登服務中或已完成、整單失敗全部不變、兩人同時操作、同一 operationId 重試、跨午夜時間、金額與收款不變、原因浮層滑鼠／鍵盤／觸控。Web main 待使用者確認後才以 dev → main PR 推進。
