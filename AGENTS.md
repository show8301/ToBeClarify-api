# Repository instructions

## Development deployment checks

- The API has no separate test host. The `dev` branch remains build-only and must not run E2E tests.
- Keep `dev` verification limited to restore, build, publish-artifact validation, and other non-E2E checks.
- Only `main` deploys the API to the production host and runs its configured HTTP health check.
