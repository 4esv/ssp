# File format

Status: draft.

A circuit has two files:

| File | Contents | Required |
|---|---|---|
| `<name>.cir` | A SPICE netlist. This file is the source of truth. | Yes |
| `<name>.layout.toml` | Schematic positions for the parts in the netlist. | No |

## Directives

ssp reads directives from comment lines in the netlist. Other SPICE tools ignore these lines.

| Directive | Meaning |
|---|---|
| `* ssp:title <text>` | The title of the circuit. |
| `* ssp:input <node>` | The node that receives the input signal. |
| `* ssp:output <node>` | The node that gives the output signal. |
| `* ssp:knob <part> <taper> <pos>` | A potentiometer, its taper, and its position. |
| `* ssp:part <ref> <part-id>` | The part in the parts table that a reference uses. This mapping wins over the default mapping by kind and model name. An unknown part id gives an error diagnostic. A reference with no part gives a warning. |

Rules:

- A directive line starts with `* ssp:` at the start of the line. Leading white space is allowed.
- `<pos>` is a number.
- An unknown directive, or a directive with the wrong arguments, gives a diagnostic with severity warning. The line is ignored.
- If a directive is given more than once, the last `title`, `input` or `output` is used. Every `knob` and `part` is kept.

## Example

```spice
* ssp:title RC low-pass
* ssp:input in
* ssp:output out
V1 in 0 AC 1
R1 in out 10k
C1 out 0 10n
.end
```

## Layout file

The layout file holds the schematic position of each reference and the points of each wire. It is TOML.

```toml
[[part]]
ref = "R1"
x = 40.0
y = -20.0
rotation = 90
flip = false

[[wire]]
net = "out"
points = [[40.0, 0.0], [80.0, 0.0]]
```

| Table | Key | Meaning |
|---|---|---|
| `part` | `ref` | A reference in the netlist. Required. |
| `part` | `x`, `y` | The position. Numbers. Required. |
| `part` | `rotation` | Degrees. A multiple of 90. Default 0. |
| `part` | `flip` | `true` or `false`. Default `false`. |
| `wire` | `net` | The net that the wire belongs to. Required. |
| `wire` | `points` | A list of `[x, y]` points. At least two. Required. |

Rules:

- A file that is not valid TOML, or that has a missing or wrongly typed key, is an error. `Layout.Read` throws `InvalidDataException`.
- `Layout.Validate` checks the layout against the netlist. Each of these gives a diagnostic with severity error: a reference that is not in the netlist, a reference that is placed more than once, a rotation that is not a multiple of 90, a net that is not in the netlist, a wire with fewer than two points.
- References and nets match without regard to case.
- A reference with no `part` entry is allowed.
- `Layout.Write` gives the same text for the same layout.
