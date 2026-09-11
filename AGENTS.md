# Repository instructions

## Environment definitions

- API has no separate test host; the `dev` branch only runs restore, build, and publish-artifact validation.
- The API production environment is deployed from `main` to the single production host.
- When a combined request says to deploy to the test environment, Web goes to `dev`; because there is no API test environment, the API goes through the normal production release flow from `main`.

## Development deployment checks

- The API has no separate test host. The `dev` branch remains build-only and must skip all automated test suites unless the user explicitly requests testing for that run.
- Keep `dev` verification limited to restore, build, publish-artifact validation, deployment status, and operational availability checks; do not invoke unit, integration, E2E, or browser tests by default.
- Only `main` deploys the API to the production host and runs its configured HTTP health check.
