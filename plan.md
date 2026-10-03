# Plan: issue #26 Playwright harness
1. Add `Microsoft.Playwright` (version already pinned) to `tests/Ssp.Web.Tests`.
2. Add `tests/Ssp.Web.Tests/Playwright/SmokeTests.cs`: loads `SSP_BASE_URL`, expects the page to show "SpiceSharp 3.2.3". Skipped when `SSP_BASE_URL` is unset, so `dotnet test` in the `ci` jobs is unchanged.
3. Add `scripts/playwright.sh`: publish `src/Ssp.Web`, serve its `wwwroot`, run the Playwright tests, stop the server.
4. Add job `playwright` (ubuntu-latest only) to `.github/workflows/ci.yml`; it calls `scripts/playwright.sh`. The `ci` job is not changed.
5. Add the test command to `docs/README.md`.
Proof: run `scripts/playwright.sh` locally (exit 0); mutate the expected text to see it fail.
Out of scope: feature tests, deploy, `ci` job changes.
