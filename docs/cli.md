# CLI reference

The `ssp` command has four commands: `run`, `sweep`, `render` and `mcp`.
`ssp --help` lists them. `ssp --version` prints the version of ssp and of each library that it uses.

The examples on this page run from the root of the repository.

## Install

See [Install](index.md#install).
On a Homebrew .NET, `ssp` can fail. The error starts with:

```text
You must install or update .NET to run this application.
```

It also says `Framework: 'Microsoft.NETCore.App', version '10.0.0'`.
The system can find an old .NET in another folder first.
Set `DOTNET_ROOT` and run it again:

```sh
export DOTNET_ROOT="$(brew --prefix dotnet)/libexec"
```

## Exit codes

| Code | Meaning |
|---|---|
| 0 | The command finished. For `run` and `sweep`, no diagnostic has the severity error. |
| 1 | An error. A file is missing, an option is wrong, a value is not a number, a diagnostic has the severity error, or a render fails. |

An error prints one line on standard error. The only exception is a circuit with errors: `run` and `sweep` print the report, and `render` prints the message of each error.

## Number syntax

`--set` takes SPICE numbers. The text is not case sensitive.

| Form | Example | Value |
|---|---|---|
| Plain number | `10000`, `0.5`, `1e3` | The number. |
| Number and suffix | `10k`, `100n`, `5m` | The number times the scale of the suffix. |
| Position form | `4k7`, `2u2`, `1meg5` | The suffix marks the decimal point. `4k7` is 4.7k (4700). |

| Suffix | Scale |
|---|---|
| `t` | 1e12 |
| `g` | 1e9 |
| `meg` | 1e6 |
| `k` | 1e3 |
| `m` | 1e-3 (milli, not mega) |
| `u` | 1e-6 |
| `n` | 1e-9 |
| `p` | 1e-12 |
| `f` | 1e-15 |
| `mil` | 25.4e-6 |

A unit word after the number and the suffix is valid. The unit words are `ohm`, `f`, `h`, `v`, `a`, `s`, and `hz`. The netlist reader accepts the same words. Example: `10kohm` is 10000, and `47nF` is 47e-9.

Other letters after the number are not valid. `--set R1=10x` and `--set R1=abc` give an error. In a netlist, letters that are not a unit give a warning, and the run goes on. The warning names the line, the part, and the number that ssp used. Example: `C1 out 0 10x` gives the warning `C1 has the value 10x. ssp reads it as 10000000. The letters 'x' are not a unit.` (The netlist reader reads `x` as meg.)

A `--set` changes the value of a resistor, a capacitor, an inductor, or the DC value of a voltage or current source.
Any other part gives the error "Part X does not support a value override."
A reference that is not in the circuit gives "Unknown part reference X."

## ssp run

```
ssp run <file> [--json] [--set REF=VALUE]...
```

| Option | Meaning |
|---|---|
| `<file>` | The netlist. A relative `.include` path resolves against the folder of this file. |
| `--json` | Print the result as JSON. See [JSON output](#json-output). |
| `--set REF=VALUE` | Set a part value before the run. Use it again for more parts. |

`run` makes three small-signal analyses:

- **Operating point.** The DC voltage of each node, the current of each voltage source, and the power of each device.
- **Frequency response.** The gain and phase of each node, from 10 Hz to 100 kHz, 10 points for each decade. The report shows one point for each decade.
- **Impedance.** The input and output impedance, over the same range.

Each analysis is linear around the operating point. The signal is small, so a clipper or a fuzz does not clip.
To hear or measure clipping, use [`ssp render`](#ssp-render).

```sh
ssp run circuits/fixtures/rc-lowpass.cir
ssp run circuits/fixtures/rc-lowpass.cir --set R1=4k7 --set C1=47n
```

The first command prints a report with the sections Summary, Voltages, Response, Impedances and Diagnostics.
The Summary gives the peak of the output node and each frequency where it is 3 dB below the peak:

```
Summary
  The run completed all analyses.
  There are no errors and no warnings.
  Node out peaks at 0.00 dB at 10.0 Hz.
  Node out is 3 dB below its peak at 1.59 kHz.
```

A circuit with an error stops before the analyses. The report says "Not computed." and the command exits with 1:

```sh
ssp run circuits/fixtures/diag-no-ground.cir # rc=1
ssp run circuits/fixtures/rc-lowpass.cir --set R1=abc # rc=1
```

## ssp sweep

```
ssp sweep <file> --set REF=VALUE,VALUE,... [--json]
```

| Option | Meaning |
|---|---|
| `<file>` | The netlist. |
| `--set REF=VALUE,VALUE,...` | Required. The part and a list of values. Use one `--set`. |
| `--json` | Print the series as JSON. |

`sweep` runs the same analyses as `run` once for each value.
The text output has one report for each value, headed by a line such as `R1 = 4k7`.
The command exits with 1 if a run has a diagnostic with the severity error.

```sh
ssp sweep circuits/fixtures/rc-lowpass.cir --set R1=1k,4k7,10k
```

## ssp render

```
ssp render <file> --in <in.wav> --out <out.wav> [--oversample N] [--ir <ir.wav>]
```

| Option | Meaning |
|---|---|
| `<file>` | The netlist to render through. |
| `--in` | Required. The input WAV file. |
| `--out` | Required. The output WAV file. The folder must exist. |
| `--oversample N` | The number of solver steps for each sample. The default is 1. The value must be 1 or more. A higher value is slower and follows fast edges more closely. |
| `--ir <ir.wav>` | An impulse response, for example a speaker cabinet. The output is convolved with its first channel. It must have the same sample rate as the input. |

`render` is a transient analysis. It feeds the samples of the input to the `ssp:input` node. A netlist with no `ssp:input` line gives an error. It records the voltage of the `ssp:output` node (or `out`).
The nonlinear parts work, so a clipper clips.
Each channel of the input is rendered on its own.
The output is a 24-bit WAV file with the sample rate and the length of the input.
`render` runs the same checks as `run`. A circuit with an error prints the errors and exits with 1.

```sh
ssp render circuits/library/fuzz-transistor-diode.cir --in src/Ssp.Web/Audio/riff.wav --out fuzz.wav
ssp render circuits/library/fuzz-transistor-diode.cir --in src/Ssp.Web/Audio/riff.wav --out fuzz-cab.wav --ir models/ir/cab-1x12.wav
```

Use a sample WAV from the repository, or use your own 16-bit or 24-bit WAV file.
This command downloads a sample clip into the current folder:

```sh
curl -fsSLO https://raw.githubusercontent.com/4esv/ssp/main/src/Ssp.Web/Audio/riff.wav
```

The folder `src/Ssp.Web/Audio` has the sample clips. The file `models/ir/cab-1x12.wav` is a cabinet impulse response.

## ssp mcp

```
ssp mcp
```

`mcp` serves ssp to an agent over the Model Context Protocol. It reads requests on standard input and writes replies on standard output. No other transport exists.
It prints nothing else, so do not run it by hand. An MCP client starts it.

The server has four tools:

| Tool | Arguments | Same as |
|---|---|---|
| `run` | `file`, `set` (list, optional) | `ssp run --json` |
| `explain` | `file`, `set` (list, optional) | `ssp run` (the text report) |
| `sweep` | `file`, `set` (`REF=V1,V2,...`) | `ssp sweep --json` |
| `render` | `file`, `input`, `output`, `oversample` (optional), `ir` (optional) | `ssp render` |

To register the server, add it to the MCP configuration of the client. Most clients use this form:

```json
{
  "mcpServers": {
    "ssp": { "command": "ssp", "args": ["mcp"] }
  }
}
```

The client must find `ssp` on its `PATH`, and `DOTNET_ROOT` must be set if .NET comes from Homebrew.
The file name of the configuration depends on the client. See its documentation.

## JSON output

`ssp run --json` prints one object. The JSON Schema is [schema/run-result.schema.json](schema/run-result.schema.json).

| Field | Meaning |
|---|---|
| `$schema`, `schemaVersion` | The version of the schema. |
| `operatingPoint` | `nodeVoltages` (V), `sourceCurrents` (A) and `devicePowers` (W), each keyed by name. `null` if the run stopped earlier. |
| `frequencyResponse` | `frequencies` (Hz), and `magnitudeDb` and `phaseDegrees` for each node. `null` if the run stopped earlier. |
| `impedance` | `frequencies` (Hz), and `input` and `output` as `{ "re", "im" }` in ohms. `null` if the run stopped earlier. |
| `diagnostics` | A list of `{ "severity", "message", "line" }`. The severity is `info`, `warning` or `error`. |
| `timingsMs` | The time of each section in milliseconds. |

A value that is not finite is `null`.

`ssp sweep --json` prints `{ "part": "R1", "series": [...] }`.
Each item of `series` has `value` (the text that you gave), `number` (the value in SI units) and `result` (a run result as above).

```sh
ssp run circuits/fixtures/rc-lowpass.cir --json
ssp sweep circuits/fixtures/rc-lowpass.cir --set R1=1k,4k7 --json
```

## Problems

| Message | Cause and fix |
|---|---|
| You must install or update .NET to run this application. | .NET is from Homebrew, and the system found an old .NET in another folder first. Set `DOTNET_ROOT` as shown at the top of this page. |
| File X does not exist. | The path is wrong. |
| Override 'X' must have the form REF=VALUE. | Write `--set R1=4k7`. |
| Override 'X' has a value that is not a SPICE number. | See [Number syntax](#number-syntax). |
| Unknown part reference X. | The circuit has no part with that name. |
| Oversample N must be 1 or more. | Use `--oversample 1` or more. |
