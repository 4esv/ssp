# Architecture

## Goal

The user draws a pedal circuit and presses Run.
The tool shows the frequency curve and the voltages.
The tool plays a guitar through the circuit the user drew.
The user builds a virtual amp from circuit blocks and tries it with a clip.
The user arranges the panels in a dock layout.
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

## MCP tools

`ssp mcp` serves the CLI tools to agents over MCP on standard input and output. No other transport exists.
Each tool calls the matching command, so its text is the text of the command.

| Tool | Arguments | Returns |
|---|---|---|
| `run` | `file`, `set` (optional) | The result JSON. The same as `ssp run --json`. |
| `explain` | `file`, `set` (optional) | The plain-English report. The same as `ssp run`. |
| `sweep` | `file`, `set` | The series JSON. The same as `ssp sweep --json`. |
| `render` | `file`, `input`, `output`, `oversample` (optional), `ir` (optional) | A line that names the output file. The same as `ssp render`. |

A command that writes to the error stream, for example when a file does not exist, gives a tool error.

## No backend

The web app is a set of static files. No server runs simulations.
All simulations run in the browser of the user.

## Deploy

The `web` workflow (`.github/workflows/web.yml`) deploys the web app to https://ssp.aesv.io on each push to `main`.
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

## Smoke check

After the copy, the workflow runs `scripts/smoke-live.sh "$SSP_LIVE_URL"`. `SSP_LIVE_URL` is a repository variable, for example `https://ssp.aesv.io`.
When the variable is unset, the workflow skips the check and prints a notice. The check runs only when the deploy ran.
The script uses curl. It checks these items and prints each failing URL:

- `/` returns 200 and contains `<title>ssp</title>`.
- `/editor` and `/gallery` return 200.
- The first fingerprinted `_framework/dotnet.*.js` named in the page returns 200.
- The `Cross-Origin-Opener-Policy` and `Cross-Origin-Embedder-Policy` headers are present.

The host must serve `index.html` for each path that is not a file (SPA fallback). Without it, `/editor` returns 404.
`scripts/test-smoke-live.sh` proves the check: it must fail on a server with no fallback and pass on a server with one.

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

## Worker boundary (done)

Long simulations run in a Web Worker so that the page continues to respond.
`WorkerSimulationHost` implements `ISimulationHost`. `Program.cs` registers it. The UI did not change.

| Part | Role |
|---|---|
| `Hosting/WorkerSimulationHost.cs` | Calls the page module through `IJSRuntime` and reads the JSON that comes back. |
| `wwwroot/js/simulation-worker-client.js` | Starts the worker and posts the netlist and the input samples. The input moves to the worker as a transferred `Float64Array`. |
| `wwwroot/js/simulation-worker.js` | Starts a second .NET runtime with `dotnet.create()` and calls the `[JSExport]` methods. |
| `Hosting/WorkerExports.cs` | The `[JSExport]` methods. Each one returns JSON. |

The worker uses the boot API of the `wasmbrowser` template on the `_framework` files of Ssp.Web. It does not use a second project.
The page gives the worker the fingerprinted URL of `dotnet.js` from its import map, because a worker has no import map.
The worker sets `globalThis.dotnetSidecar = true` before it imports `dotnet.js`. Without the flag, `dotnet.js` takes a worker that has `onmessage` for a runtime thread, and the start never ends.

`Render` and `Versions` run in the worker. `Run` and `Sweep` stay on the calling thread: they take milliseconds, and Ssp.Web has no reader for the run result JSON yet.
`WorkerHostTests` clicks the page during a render of about 10 s and measures the response. The [benchmarks](benchmarks.md#worker) have the numbers and the payload size.

### Worker block size

The worker renders each `Render` request as one block: the full input buffer. The page sends one message and gets one message back.

The reasons:

- `Analyses.Render` keeps no solver state between calls. Each call starts from the DC operating point.
  A render in smaller blocks restarts the circuit at each block edge. The output then has a step, and the coupling capacitors charge again for about 50 ms.
- The cost has no fixed part to amortize. In Chromium, 0.1 s of audio takes 366 ms and 1 s takes 3410 ms ([benchmarks](benchmarks.md#browser)).
- The browser renders at 0.29x real time. Live streaming is not possible, so the worker gives the finished buffer.

A smaller block will be useful for progress and cancel. That needs a render that continues from a saved state.

## Live monitor

The live monitor plays the microphone through the circuit. The panel shows the label "monitor, not real time" and the measured latency in milliseconds.

The monitor block is 512 samples (`MonitorSession.ChunkSamples`). The page captures one chunk of that size, the worker gives back one output chunk of the same size, and the page plays it.
The monitor does not use the worker block above. That block is the full buffer, because `Analyses.Render` keeps no solver state.
`MonitorSession` keeps the state: it runs one long `Transient.Run` and reads the input from a `RingBufferWaveform`. The output has no step at a chunk edge.

Limits:

- The monitor is not real time. `Analyses.Render` runs at 0.29x real time in the browser ([benchmarks](benchmarks.md#browser)). Nobody has measured the monitor, but a slower render makes the chunks wait in a queue.
  The queue holds 8 chunks. The page drops the oldest chunk when the queue is full, and shows the number of dropped chunks.
- The latency is the time from the first captured sample of a chunk to the start of its playback. It includes the 512-sample capture, the queue, the render, and the audio output. It grows when the render is slower than real time.
- A gap in the output is audible when a chunk arrives late.
- The session runs for at most one hour of audio.
- The monitor uses the sample rate of the audio device and does not oversample.
- The microphone input goes to the `ssp:input` node. A netlist without that node cannot start the monitor.
- Echo cancellation, noise suppression and automatic gain are off. Use headphones, or the output feeds back into the microphone.
- One monitor runs at a time. A render in the worker waits for the monitor chunk in progress.
- There are no native audio drivers and no plugin formats.


## Schematic symbols

`SchematicRenderer` turns a circuit into schematic elements and draws each element with a symbol from `Symbols`.

| Kind | Symbol | Pins, in node order |
|---|---|---|
| `resistor` | US zigzag | 1, 2 |
| `pot` | Zigzag with a wiper arrow | top, wiper, bottom |
| `capacitor` | Two parallel plates | 1, 2 |
| `electrolytic` | Straight plate with `+`, curved plate | +, - |
| `inductor` | Coil | 1, 2 |
| `diode` | Triangle and bar | anode, cathode |
| `led` | Diode with two light arrows | anode, cathode |
| `npn`, `pnp` | Base, collector, emitter. Emitter arrow out for NPN, in for PNP. The substrate pin is hidden. | C, B, E, S |
| `njf`, `pjf` | Channel bar. Gate arrow in for N-channel, out for P-channel. | D, G, S |
| `opamp` | Triangle, `-` input above `+` input, output at the apex | in-, in+, out |
| `opamp5` | Op-amp with supply pins | in+, in-, out, V+, V- |
| `battery`, `source`, `isource` | Cell with `+` mark, circle with a sine, circle with a current arrow | +, - |
| `ground` | Three bars | 1 |
| `rail` | T bar with the supply voltage | 1 |
| `transformer`, `vactrol` | Coils with a core, LED over an LDR | as in `models/` |

Rules:

- The kind comes from the part row first, then from the component type. A capacitor of 1u or more is `electrolytic`, because a netlist has no polarity flag.
- A subcircuit instance is one element. The flattened parts `X1.*` are not drawn. An instance of a known op-amp model is `opamp5`.
- A pot pair `P_1` and `P_2` is one `pot` named `P`. Its value is the total resistance.
- A DC voltage source from ground to a node or reference that starts with `vcc`, `vee`, `vdd`, `vss`, `vbat`, `v+` or `v-` is a `rail`.
- A pin on ground gets a ground symbol that always points down.
- The reference is above the symbol and the value is below it, in plain units (`100k`, `10n`, `1u`). Labels are outside the rotated group, so they stay upright.
- Rotation and flip apply to the whole symbol. Pins are on the 10-unit grid in every orientation. A symbol with two pins has them at (0, 0) and (60, 0).
- `SymbolArrow` is the direction of the diode, BJT and JFET arrows. Tests check it in all eight orientations.
