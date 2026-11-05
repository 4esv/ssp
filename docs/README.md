# Documents

| Document | Contents |
|---|---|
| [index.md](index.md) | What ssp is, install, and a five-minute tour. |
| [cli.md](cli.md) | Every command and option of `ssp`, number syntax, JSON output and MCP. |
| [web.md](web.md) | The web app: the editor, clips, the amp, share links and exports. |
| [file-format.md](file-format.md) | The circuit file and the layout file. |
| [architecture.md](architecture.md) | Projects, data flow, and boundaries. |
| [benchmarks.md](benchmarks.md) | Measured simulation speed. |
| [schema/run-result.schema.json](schema/run-result.schema.json) | The JSON schema for a run result. |
| [decisions/](decisions/README.md) | Decision records, one page each. |

`models/README.md` documents the device models. [AGENTS.md](../AGENTS.md) has the commands and the rules
for coding agents. [CONTRIBUTING.md](../CONTRIBUTING.md) has the process.

Run `scripts/playwright.sh` for the browser tests. It publishes `src/Ssp.Web`, serves it, and runs the
Playwright tests in `tests/Ssp.Web.Tests/Playwright/`. The first run downloads Chromium.
