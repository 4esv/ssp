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
| `* ssp:part <ref> <part-id>` | The part in the parts table that a reference uses. |

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

The layout file format is not specified yet.
