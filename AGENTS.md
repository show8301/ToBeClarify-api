# Repository instructions

## Environment definitions

- API has no separate test host; the `dev` branch only runs restore, build, and publish-artifact validation.
- The API production environment is deployed from `main` to the single production host.
- When a combined request says to deploy to the test environment, Web goes to `dev`; because there is no API test environment, the API goes through the normal production release flow from `main`.

## Development deployment checks

- The API has no separate test host. The `dev` branch remains build-only and must skip all automated test suites unless the user explicitly requests testing for that run.
- Keep `dev` verification limited to restore, build, publish-artifact validation, deployment status, and operational availability checks; do not invoke unit, integration, E2E, or browser tests by default.
- Only `main` deploys the API to the production host and runs its configured HTTP health check.

## Required migrations for combined releases

- Follow [the local DBA migration runbook](docs/DATABASE-MIGRATION-RUNBOOK.md) before publishing dependent API changes.
- Use the user-authorized account in the existing ignored local configuration; keep credentials out of source, logs, command arguments, and Git.
- Verify the target, required database-scoped permissions, migration state, backups, and business/data invariants. Stop on partial DDL failure; do not blindly rerun.
- Combined test releases publish API through main and Web through dev. Web main still requires user verification.
