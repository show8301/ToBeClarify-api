# 點餐履約階段 1

階段 1 保留指名服務的原預約與購買分鐘，新增現在接待預覽、休息調整、服務補登、跨營業期改期、衝突協調與折讓記錄。

## 管理 API

- `GET /api/admin/orders/{orderId}/fulfillment/{unitId}/start-preview`：回傳現在開始後的完整服務區間、原休息分鐘與受影響的下一單。
- `POST /api/admin/orders/{orderId}/fulfillment/{unitId}/transition`：沿用分項履約操作識別碼與版本控制，新增 `start_now`、`backfill`。
  - `start_now` 可帶 `restMinutes`、`compensationAmount`、`compensationReason`；完整購買分鐘不變。
  - `backfill` 可帶 `actualStartsAt`、`actualEndsAt` 及原因，補登仍保留原預約快照。
  - `reschedule` 可帶 `targetBusinessPeriodId`，目標營業期須為可履約狀態。

折讓在同一資料庫交易新增 `charge_reduce` 費用紀錄，服務事件寫入 `ORDER_FULFILLMENT_EVENTS`。晚開始碰到後續時段時，系統留下 `ORDER_FULFILLMENT_CONFLICTS`、釋放受影響的忙碌區段並將該單轉為待協調。
