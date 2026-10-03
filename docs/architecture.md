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
| `Ssp.Web` | A static Blazor WebAssembly app. It calls `Ssp.Core` in the browser. |

`Ssp.Cli` and `Ssp.Web` do not reference each other.
Only `Ssp.Core` references the engine packages.

## Data flow

1. The user gives a netlist (`.cir`) and, as an option, a layout file (`<name>.layout.toml`).
2. `Ssp.Core` loads the netlist into an engine circuit.
3. `Ssp.Core` runs the analyses and the diagnostic rules.
4. `Ssp.Core` renders an audio clip through the circuit.
5. `Ssp.Core` returns one result object.
6. `Ssp.Cli` writes the result as text or JSON and the audio as WAV. `Ssp.Web` shows the result and plays the audio.

## No backend

The web app is a set of static files. No server runs simulations.
All simulations run in the browser of the user.

## Worker boundary (planned)

Long simulations will run in a Web Worker so that the page continues to respond.
The UI will call a simulation host interface. The UI will not call `Ssp.Core` directly.
