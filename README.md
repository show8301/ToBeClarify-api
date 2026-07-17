# ToBeClarify API

.NET 10 / Dapper Client API for ToBeClarify, backed by MariaDB 11.2.

## Structure

- Client API routes use `/api/client/{resource}` and do not require JWT.
- Admin API routes use `/api/admin/{resource}` and require the `AdminOnly` authorization policy.
- This phase implements Client APIs only. Existing Admin placeholders and JWT configuration are intentionally unchanged.
- API request logging writes to MariaDB `API_LOGS`; if database logging fails, it appends to `Logs/fallback_yyyyMMdd.txt`.
- Database and API business timestamps use Taiwan standard time (`+08:00`).

## Database

The initial MariaDB 11.2 schema is in `Database/INITIAL_SCHEMA_MARIADB_11_2.SQL`. Select the target database before running it. The script creates 19 business tables plus `API_LOGS`; it does not create a migration-history table.

For an existing database created before the web mock-data integration, run `Database/ALTER_20260715_WEB_MOCK_SUPPORT.SQL` first. `Database/SEED_MOCK_DATA_MARIADB_11_2.SQL` contains the current Web mock data. Its stable IDs and upserts make it safe to rerun without creating duplicate rows.

Do not commit production credentials. Configure `ConnectionStrings__DefaultConnection` with an environment variable or a deployment secret.

## Client Routes

- `/api/client/home`
- `/api/client/site-settings`
- `/api/client/navigation-items`
- `/api/client/home-event-carousels`
- `/api/client/shop-rules`
- `/api/client/pricing-rules`
- `/api/client/staff-members`
- `/api/client/events`
- `/api/client/gallery-albums`
- `/api/client/guestbook/comments`
- `/api/client/menu`
- `/api/client/staff-reservations`
- `/api/client/rankings`

Guestbook write endpoints are limited per client IP. Internal fields such as reservation notes and guestbook user-token hashes are not exposed by Client DTOs.

## Local Build

This project targets .NET 10.

```bash
DOTNET_CLI_HOME="$PWD/.dotnet-home" NUGET_PACKAGES="$PWD/.dotnet-home/.nuget/packages" dotnet build
```

Install a .NET 10 SDK on the system if `dotnet --info` does not show a 10.x SDK.
# 本機 Connection String

API 啟動時會選擇性讀取根目錄的 `appsettings.Local.json`。此檔案已列入 `.gitignore`，可直接修改本機 connection string，不會納入 Git。

如需覆蓋本機檔案，仍可使用標準環境變數 `ConnectionStrings__DefaultConnection`。
