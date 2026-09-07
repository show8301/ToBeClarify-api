# API audit fixes — 2026-09-08

The public reservation UNION now sorts by the projected `StaffId` and `StartsAt` aliases. The old physical column names caused runtime SQL errors and broke Web live synchronization.

Settlement inputs and attendance backfill now reject staff IDs that do not exist before creating a settlement run. This complements the Web correction from account IDs to staff IDs. No schema migration or historical data rewrite is required; a read-only audit found no orphan settlement inputs.

Reading a previously unsaved settlement date returns an in-memory draft instead of inserting a `system`-owned row. Actual write operations continue to create the run with the authenticated actor. Overview reads also validate the date and positive session number.

Verification: Release build passed with zero warnings/errors, and the corrected reservation SQL executed against the existing database as a SELECT-only count query. No automated test suite or business mutation was executed for DEV validation. The `dev` branch remains build-only; production requires promotion to `main` because there is no separate API test host.

Git commits and pushes use verified GitHub identity `nick800608` (24969952).
