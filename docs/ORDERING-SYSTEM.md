# Customer ordering system

> **直接 SQL 維護帳號限制：** 提供給直接 SQL 操作的帳號僅可查詢、新增、修改，沒有刪除資料或資料表的權限。API 的點餐／後台刪除端點依 API 部署連線帳號與業務授權運作，不受此文件的直接 SQL 帳號限制。請先閱讀 [資料庫帳號權限限制](DATABASE-PERMISSIONS.md)。

## Deployment order

1. Apply `db/migrations/20260830_01_ordering_system.sql`, then `db/migrations/20260830_02_business_hours_and_order_transitions.sql`, `db/migrations/20260830_03_tip_presets.sql`, and finally `db/migrations/20260831_01_business_day_override.sql` to the target MySQL/MariaDB database. The later migrations add cross-day business windows, immutable nomination snapshots, pure-companionship mode, attached service add-on orders, four configurable tip presets, and the audited business-day override used by management testing.
2. Set `OrderingToken__Secret` to a random secret of at least 32 characters. Keep it stable across deployments; changing it invalidates all current-day customer links.
3. Set `OrderingToken__PublicWebBaseUrl` to the customer order page, for example `https://www-dev.marchgroup.net/order` while the Web is in the test environment.
4. Merge the release into `main`. The API workflow may build `dev` and pull requests for verification, but it deploys only from `main` to the single production IIS environment using `API_DEPLOY_PATH` and `API_HEALTHCHECK_URL`. There is currently no API test-environment deployment.

The API repository does not require `DEV_API_DEPLOY_PATH` or `DEV_API_HEALTHCHECK_URL`; those variables are intentionally unused because no separate API test host exists.

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
- `/api/admin/orders/{id}/reschedule` moves an expired/conflicting start time back into the queue. It remains future-only and is intended for orders that have not yet been served. For an expired order this is the backend emergency reactivation path: the original meal-credit deduction is restored before the order returns to `submitted`; it fails safely if the session no longer has enough credit. Existing nominations cannot have their segment quantity extended.
- `/api/admin/orders/{id}/backfill-served` handles an expired order that was actually served before staff confirmation was recorded. It supports `in_service` (the original reservation must still have time remaining) and `completed` (actual start/end may be in the past). The nominated staff member may backfill a single-staff order; manager/developer accounts may backfill multi-staff orders. Completed backfills create historical, non-blocking busy records; all operations require a reason, reverse the expired meal-credit refund transactionally, and write history/audit records.
- `/api/admin/ordering-settings` controls meal credit, base nomination fee, four tip preset amounts, 20-minute segment configuration and reminder/escalation/expiry thresholds. Only manager/developer accounts can read or update these settings.
- `/api/admin/ordering-settings/business-day-override` reads or replaces the single global business-day override. Only manager/developer accounts can use it. The request must include a business date, a 0–24 hour start/end window, a non-empty reason, and an automatic expiry from 1 to 1,440 minutes. This is intentionally global (it is not limited to a test session), so the admin UI displays a production warning and the API writes an audit record.
- `/api/admin/ordering-settings/business-day-override/disable` immediately turns off the global override through a soft state update; it never deletes the row. An expired row is treated as inactive and can be re-enabled with a new TTL.
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
- The business-day override changes the business-date context returned to customer/admin flows and order validation while it is enabled. It does not rewrite existing `BUSINESS_PERIODS` snapshots or simulate the wall clock; the short TTL and audit trail are the safety boundary for production management tests.
- 訂單明細刪除端點屬 API 業務流程；呼叫前須確認 API 連線帳號與權限設定。若直接以受限 SQL 帳號維護資料，則不得執行相同的 `DELETE` 語句。
