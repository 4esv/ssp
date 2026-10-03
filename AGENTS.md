# Agents

This file gives commands and rules for coding agents.

## Commands

| Task | Command |
|---|---|
| Build | `dotnet build -c Release` |
| Test | `dotnet test -c Release` |
| Update golden files | `UPDATE_GOLDEN=1 dotnet test` |
| Run the CLI | `dotnet run --project src/Ssp.Cli -- --version` |
| Run the web app | `dotnet run --project src/Ssp.Web` |
| Publish the web app | `dotnet publish src/Ssp.Web -c Release -o publish` |
| List ready issues | `scripts/ready-issues.sh` |

The build treats warnings as errors. A change with a warning fails CI.

## Layout

| Path | Contents |
|---|---|
| `src/Ssp.Core` | Circuit loading, analysis, and results. No UI code. |
| `src/Ssp.Cli` | The `ssp` command. |
| `src/Ssp.Web` | The static Blazor WebAssembly app. |
| `tests/` | One xunit project for each `src` project. |
| `circuits/fixtures` | Netlists for tests. |
| `models` | Device models with provenance headers. |
| `docs` | Documents. `docs/decisions` has the decision records. |

The JSON schema for run results will be in `docs/schema/run-result.schema.json`.

## Rules

- Do one issue in each session.
- Write the failing test first. See [CONTRIBUTING.md](CONTRIBUTING.md).
- Do not edit golden files by hand. Use `UPDATE_GOLDEN=1`.
- Do not change package versions in `Directory.Packages.props` unless the issue says so.
- Do not change `global.json` unless the issue says so.
- Do not add a model without a provenance header.
- Do not write a number in `docs/benchmarks.md` that you did not measure.
- Do not add a server or a backend to `src/Ssp.Web`.
