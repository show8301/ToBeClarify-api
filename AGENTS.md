# Repository instructions

## Development deployment checks

- The API has no separate test host. The `dev` branch remains build-only and must skip all automated test suites unless the user explicitly requests testing for that run.
- Keep `dev` verification limited to restore, build, publish-artifact validation, deployment status, and operational availability checks; do not invoke unit, integration, E2E, or browser tests by default.
- Only `main` deploys the API to the production host and runs its configured HTTP health check.
