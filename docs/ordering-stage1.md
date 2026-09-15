# 點餐履約階段 1

階段 1 保留指名服務的原預約與購買分鐘，新增現在接待預覽、休息調整、服務補登、跨營業期改期、衝突協調與折讓記錄。

## 管理 API

- `GET /api/admin/orders/{orderId}/fulfillment/{unitId}/start-preview`：回傳現在開始後的完整服務區間、原休息分鐘與受影響的下一單。
- `POST /api/admin/orders/{orderId}/fulfillment/{unitId}/transition`：沿用分項履約操作識別碼與版本控制，新增 `start_now`、`backfill`。
  - `start_now` 可帶 `restMinutes`、`compensationAmount`、`compensationReason`；完整購買分鐘不變。
  - `backfill` 可帶 `actualStartsAt`、`actualEndsAt` 及原因，補登仍保留原預約快照。
  - `reschedule` 可帶 `targetBusinessPeriodId`，目標營業期須為可履約狀態。
- `POST /api/admin/ordering-maintenance/expire-waiting`：店經理／開發者可依目前失效分鐘數執行一次等待單維護；只處理已達期限的舊版等待單，適合現場補跑與驗收，不會提前失效尚未到期的訂單。
- `carry_forward` 是分項履約的跨營業期操作，必須指定不同且為 `open`／`coordination` 的目標營業期；原訂單與金額歸屬不變，只有尚未服務的項目改由新營業期履約，讓原營業期可關店／結算。

折讓在同一資料庫交易新增 `charge_reduce` 費用紀錄，服務事件寫入 `ORDER_FULFILLMENT_EVENTS`。晚開始碰到後續時段時，系統留下 `ORDER_FULFILLMENT_CONFLICTS`、釋放受影響的忙碌區段並將該單轉為待協調。

排程工作到達等候期限時，只釋放尚未接單的加購暫占並回到待處理狀態，不會刪除加購或改寫原購買資料；接單與開始時仍會重新檢查母指名的剩餘容量。
