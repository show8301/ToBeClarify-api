# 小費訂單、收款與分配

> 此為先前版本紀錄。人頭均分、店家餘額及手動公共小費規則已由 [公共小費按工時分配](PUBLIC-TIP-HOURLY.md) 取代；交易、防重及作廢流程仍沿用。

新增員工小費統一由後台「補登已成交訂單」建立。`TipOrderLineRequest.distributionMode` 支援 `equal`、`designated`、`custom`；`allocations` 為出勤人員及比例。店家小費和既有顧客點餐契約保留。新分配契約只允許後台已成交補登。

訂單只增加應收；補登可關聯既有收款，但不自動產生實收。收款由帳款紀錄另登。所有員工分配以整數 G 保存，均分尾差依人員 ID、自訂比例依最大餘數分配，總和等於小費總額。均分採原營業日已有正數計薪分鐘的人員，跨角色去重、按人均分，不使用工時比例；保存後不因出勤異動自動改寫。報價包含分配快照，提交交易會再核對出勤名單。

新增 `ORDER_TIP_DISTRIBUTIONS` 保存每個小費訂單項目的分配版本及 JSON 快照。原 `ORDER_TIPS` 保留單筆總額，避免每位員工各增加一份訂單金額。結算排除已另存分配的原基礎列，再依快照展開員工來源；沿用訂單有效淨收款比例認列，未收款不發放，退款降低認列。

`GET /api/admin/ordering-finance/sessions/{sessionId}/order-tips` 列出原訂單小費；`PUT .../order-tips/{itemId}` 更正其分配，需要原因、帳款與分配版本及操作 UUID。沿用後台權限。整筆更正只取代分配，不改訂單總額或收退款。舊分配及原因保存在 `ORDER_AUDIT_LOG`，操作重送透過 `ORDERING_FINANCE_OPERATIONS` 查回；不同內容重用 UUID、過期版本和未出勤對象均拒絕。

更正與作廢取得營業期共用鎖，未正式結算回到 draft；正式結算結果及版本保留，由經理在最終步驟填原因重新結算。

## 舊版補登小費

`tip_supplement` 不再允許新增或修改，舊 Web 收到 `FINANCE_TIP_USE_ORDER` 提示。原紀錄保留既有結算行為，直到人員核對後作廢；不自動轉單、不自動刪除或改寫正式結算。作廢需原因、帳款／紀錄版本及 UUID，保留原紀錄與 revision，不影響應收或實收。

有未作廢舊版小費分配的顧客，建立新的小費訂單會被攔下；有未作廢舊版分配的原訂單，也不能再更正訂單分配，避免兩套來源重複。

核對順序：確認實際小費總額、原小費訂單及有效收款；若原訂單已存在，作廢重複的舊版分配並於原訂單設定正確分配；若漏開訂單，核對舊應收調整是否已包含此款，處理後才作廢舊版分配、補登小費訂單及關聯既有收款。不能把每一筆舊分配都自動轉成新應收。已作廢收款不能當作有效實收。

## 發布與驗證

先依 [DBA migration 流程](DATABASE-MIGRATION-RUNBOOK.md) 套用 [20261021_01_order_tip_distributions.sql](../db/migrations/20261021_01_order_tip_distributions.sql)，再發布 API，最後發布 Web dev。這份 migration 只加表，既有業務資料不變；不在本次開發期間套用正式資料庫。

`dotnet run --project tests/OrderBackfill -c Release` 包含整數分配、出勤範圍、比例與收款認列測試。`-- --integration` 延用原測試的專用本機資料庫限制，驗證 migration 重跑、訂單／應收／實收分離、重送、舊版分配作廢、分配版本與正式結算保留。dev 發布仍依 AGENTS 預設跳過測試套件。
