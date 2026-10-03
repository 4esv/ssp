# Architecture

## Goal

The user draws a pedal circuit and presses Run.
The tool shows the frequency curve and the voltages.
The tool plays a guitar through the circuit the user drew.
The user exports the parts list and the schematic to build the pedal.

Hearing the circuit is a core feature, not an extra. The CLI writes the audio to a file. The web app plays it.

## Projects

| Project | Role |
|---|---|
| `Ssp.Core` | Loads circuits, runs analyses, and makes results. It references the engine packages. |
| `Ssp.Cli` | The `ssp` command. It reads files, calls `Ssp.Core`, and writes text or JSON. |
| `Ssp.Web` | A static Blazor WebAssembly app. It calls `Ssp.Core` in the browser through a simulation host. |

`Ssp.Cli` and `Ssp.Web` do not reference each other.
Only `Ssp.Core` references the engine packages.

## Data flow

1. The user gives a netlist (`.cir`) and, as an option, a layout file (`<name>.layout.toml`).
2. `Runner.Run` loads the netlist into an engine circuit.
3. `Runner.Run` sets the overrides from `RunOptions`.
4. `Runner.Run` checks the circuit with the diagnostic rules. An error from the loader, the overrides or the rules stops the pipeline here. The analyses do not run.
5. `Runner.Run` runs the operating point. A solver failure gives a diagnostic and stops the pipeline.
6. `Runner.Run` runs the frequency response and the impedance over the sweep from `RunOptions`.
7. `Runner.Run` returns one `RunResult`. It holds each analysis, the diagnostics and the time of each section in milliseconds. An analysis that did not run is null.
8. `Ssp.Core` renders an audio clip through the circuit.
9. `Ssp.Cli` writes the result as text or JSON and the audio as WAV. `Ssp.Web` shows the result and plays the audio.

## No backend

The web app is a set of static files. No server runs simulations.
All simulations run in the browser of the user.

## Deploy

The `web` workflow (`.github/workflows/web.yml`) deploys the web app to https://ssp.aesv.io on each push to `master`.
The workflow publishes `src/Ssp.Web` and copies `publish/wwwroot` to the static host with `rsync --delete` over SSH.
The workflow pins the host key with `ssh-keyscan` before the copy.

The workflow reads each host detail from repository secrets:

| Secret | Use |
|---|---|
| `SSP_DEPLOY_HOST` | The SSH host. Required. |
| `SSP_DEPLOY_KEY` | The private SSH key. Required. |
| `SSP_DEPLOY_PORT` | The SSH port. Optional. The default is 22. |
| `SSP_DEPLOY_USER` | The SSH user. Optional. |

When `SSP_DEPLOY_HOST` or `SSP_DEPLOY_KEY` is absent, the workflow skips the deploy step.
The workflow copies into the root of the remote target. The host maps that root to the site folder.

## Headers

The host must send these headers with each response:

| Header | Value |
|---|---|
| `Cross-Origin-Opener-Policy` | `same-origin` |
| `Cross-Origin-Embedder-Policy` | `require-corp` |

These headers make the page cross-origin isolated. The browser gives `SharedArrayBuffer` only to an isolated page.
The host configuration sets the headers. The workflow and the repository do not set them.

## Host boundary

The UI calls `ISimulationHost` in `Ssp.Web.Hosting`. The UI does not call `Ssp.Core` directly.
The interface has `Run`, `Render`, `Sweep` and `Versions`. Each method returns a `Task`.
`Program.cs` registers the host for dependency injection. Components get the host with `@inject`.
`InProcessSimulationHost` calls `Ssp.Core` on the calling thread. In the browser, a long run blocks the page.
`InProcessSimulationHost.Render` throws `NotSupportedException` until `Ssp.Core` has a transient render (#7).
No `.razor` file names `Ssp.Core`.

## Worker boundary (planned)

Long simulations will run in a Web Worker so that the page continues to respond.
`WorkerSimulationHost` will implement `ISimulationHost`. The UI will not change.
