# Documents

| Document | Contents |
|---|---|
| [architecture.md](architecture.md) | Projects, data flow, and boundaries. |
| [file-format.md](file-format.md) | The circuit file and the layout file. |
| [features.md](features.md) | Features and their status. |
| [benchmarks.md](benchmarks.md) | Measured simulation speed. |
| [decisions/](decisions/) | Decision records. |

## Browser tests

Run `scripts/playwright.sh`. It publishes `src/Ssp.Web`, serves it, and runs the Playwright tests in `tests/Ssp.Web.Tests/Playwright/`. The first run downloads Chromium.

## Decision records

- [0001: Depend on the engine. Do not fork it.](decisions/0001-depend-not-fork.md)
- [0002: Static WebAssembly app](decisions/0002-static-wasm.md)
- [0003: The netlist is the source of truth](decisions/0003-netlist-source-of-truth.md)
- [0004: No TUI](decisions/0004-no-tui.md)
