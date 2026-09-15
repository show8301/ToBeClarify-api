# 點餐履約階段 1

階段 1 保留指名服務的原預約與購買分鐘，新增現在接待預覽、休息調整、服務補登、跨營業期改期、衝突協調與折讓記錄。

## 管理 API

- `GET /api/admin/orders/{orderId}/fulfillment/{unitId}/start-preview`：回傳現在開始後的完整服務區間、原休息分鐘與受影響的下一單。
- `POST /api/admin/orders/{orderId}/fulfillment/{unitId}/transition`：沿用分項履約操作識別碼與版本控制，新增 `start_now`、`backfill`。
  - `start_now` 可帶 `restMinutes`、`compensationAmount`、`compensationReason`；完整購買分鐘不變。
  - `backfill` 可帶 `actualStartsAt`、`actualEndsAt` 及原因，補登仍保留原預約快照。
  - `reschedule` 可帶 `targetBusinessPeriodId`，目標營業期須為可履約狀態。
- `POST /api/admin/ordering-maintenance/expire-waiting`：店經理／開發者可依目前失效分鐘數執行一次等待單維護；只處理已達期限的舊版等待單，適合現場補跑與驗收，不會提前失效尚未到期的訂單。
- `carry_forward` 是分項履約的跨營業期操作。可先不填目標營業期，把尚未服務項目標記為「待跨期」以完成原營業期關店／結算；新營業期開放後再指定不同且為 `open`／`coordination` 的目標營業期與新時段。原訂單與金額歸屬不變，只有尚未服務的項目改由新營業期履約。

## A10／A35 驗收前置條件

以下步驟使用正常管理權限與現有業務入口建立資料，不直接寫入資料庫。

### A10：建立並恢復 expired 等待單

1. 在已開店的營業期由後台建立一個 flow 1 點餐碼，再用該碼送出沒有指名的餐點單；訂單會進入 `submitted` 等待佇列。
2. 為縮短驗收等待，可暫時在「點餐設定」將提醒／升級／失效分鐘設為遞增的小值（例如 `1/2/3`），驗收完成後恢復營運設定（預設 `5/10/20`）。
3. 等待單達到失效分鐘後，由店經理／開發者呼叫 `POST /api/admin/ordering-maintenance/expire-waiting`。回應數量代表本次實際轉為 `expired` 的訂單，未到期單不會被處理。
4. 查詢訂單歷程確認 `submitted → expired`，再以 `POST /api/admin/orders/{orderId}/reschedule` 提供未來時間。歷程應出現 `expired → submitted`；原餐點、折抵與實收只恢復原單狀態，不新增一筆收款。

### A35：建立「已結算但尚未服務」資料

1. 開啟 flow 2 營業期，建立含尚未開始餐點或指名項目的訂單。
2. 在原營業期尚未關店時，從分項履約入口對尚未服務項目執行 `carry_forward`，先不填 `targetBusinessPeriodId`；餐點需一次移轉全部剩餘數量，且必須填寫原因。此步驟會釋放原期占用並標記為 `carried_forward`。
3. 以 `business-period/action` 依序執行原期 `close`、`settle`。原訂單的 `businessPeriodId` 與金額保持不變，原期可完成結算，這就是「已結算但尚未服務」的可重現前置資料。
4. 開啟下一個營業期後，再對同一分項執行 `carry_forward`，填入不同且狀態為 `open`／`coordination` 的 `targetBusinessPeriodId`；指名項目另填新的未來開始時間。之後可由新期正常接單、開始、完成。
5. 驗收時確認來源營業期仍為 `settled`、分項的 `fulfillmentPeriodId` 已指向新期、原訂單金額與餐點折抵未再次建立，且沒有回開歷史營業期。

折讓在同一資料庫交易新增 `charge_reduce` 費用紀錄，服務事件寫入 `ORDER_FULFILLMENT_EVENTS`。晚開始碰到後續時段時，系統留下 `ORDER_FULFILLMENT_CONFLICTS`、釋放受影響的忙碌區段並將該單轉為待協調。

排程工作到達等候期限時，只釋放尚未接單的加購暫占並回到待處理狀態，不會刪除加購或改寫原購買資料；接單與開始時仍會重新檢查母指名的剩餘容量。
