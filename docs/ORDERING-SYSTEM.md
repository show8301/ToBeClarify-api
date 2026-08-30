# Customer ordering system

> **直接 SQL 維護帳號限制：** 提供給直接 SQL 操作的帳號僅可查詢、新增、修改，沒有刪除資料或資料表的權限。API 的點餐／後台刪除端點依 API 部署連線帳號與業務授權運作，不受此文件的直接 SQL 帳號限制。請先閱讀 [資料庫帳號權限限制](DATABASE-PERMISSIONS.md)。

## Deployment order

1. Apply `db/migrations/20260830_01_ordering_system.sql`, then `db/migrations/20260830_02_business_hours_and_order_transitions.sql` to the target MySQL/MariaDB database. The second migration adds cross-day business windows, immutable nomination snapshots, pure-companionship mode and attached service add-on orders.
2. Set `OrderingToken__Secret` to a random secret of at least 32 characters. Keep it stable across deployments; changing it invalidates all current-day customer links.
3. Set `OrderingToken__PublicWebBaseUrl` to the customer order page, for example `https://www-dev.marchgroup.net/order` while the Web is in the test environment.
4. Deploy the API, then deploy the Web test branch. The API workflow supports `main` and `dev`; the test deployment uses `DEV_API_DEPLOY_PATH` and `DEV_API_HEALTHCHECK_URL` repository variables.

The migration is deliberately not applied by application startup. This repository treats versioned migration files as the database source of truth and the deployment operator applies them before the matching API release.

## Customer API

All responses use the normal `ApiResponse<T>` envelope.

- `POST /api/client/ordering/access` validates a current-day encrypted order token.
- `POST /api/client/ordering/recover` rotates a token after game ID plus the six-digit staff assistance code are verified.
- `GET /api/client/ordering/catalog` returns the menu, staff-first service catalog and current operating settings.
- `GET /api/client/ordering/orders` returns all orders for the token's session.
- `POST /api/client/ordering/orders` submits meals, nominations and tips. Send the token in `X-Order-Token` for all routes except access and recovery.

## Admin API

All admin roles may read customer sessions and orders, create/reissue sessions, modify unexecuted order content, and handle emergency state changes. Manager/developer authorization is enforced for operating settings and reports.

- `/api/admin/order-sessions` creates and searches today's customer sessions.
- `/api/admin/order-sessions/{id}/reissue` rotates the URL and six-digit assistance code.
- `/api/admin/order-sessions/{id}/orders` lists one customer's orders.
- `/api/admin/orders/{id}/confirm-nominee` only confirms the staff member linked to the signed-in account; every nominee must confirm before the order is established.
- `/api/admin/orders/{id}/reschedule` moves an expired/conflicting start time back into the queue. Existing nominations cannot have their segment quantity extended.
- `/api/admin/ordering-settings` controls meal credit, base nomination fee, 20-minute segment configuration and reminder/escalation/expiry thresholds.
- `/api/admin/ordering-settings/pause-nomination` temporarily hides and disables nomination ordering.
- `/api/admin/ordering-reports` returns immutable revenue and tip snapshots for settlement/reporting.

## Pricing and scheduling invariants

- Prepaid credit applies only to `menu_item` and `menu_set` lines; unused credit remains on the same-day session.
- The base nomination fee is a separate `nomination_base` line and is charged for every selected segment.
- A service with an explicit duration is charged once, but the customer must buy `ceil(duration / segment minutes)` segments. The entire purchased segment window is reserved before the staff buffer.
- A service without an explicit duration is charged once per selected segment.
- An existing nomination cannot increase its segment quantity. Additional time is a new order.
- Tips snapshot staff/store percentages and amounts. Omitting a staff member forces a 0/100 allocation.
- Final multi-staff confirmation checks every staff member again in one transaction. A past start or a conflict returns the order to `needs_reschedule` instead of silently starting in the past.
- 訂單明細刪除端點屬 API 業務流程；呼叫前須確認 API 連線帳號與權限設定。若直接以受限 SQL 帳號維護資料，則不得執行相同的 `DELETE` 語句。
