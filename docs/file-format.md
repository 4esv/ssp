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
| `* ssp:knob <part> <taper> <pos>` | A potentiometer, its taper, and its position. The netlist holds two resistors, `<part>_1` (top to wiper) and `<part>_2` (wiper to bottom). `Pot.Apply` keeps their sum and sets `<part>_2` to the share of the total that the taper gives. A missing resistor, an unknown taper, or a position outside 0 to 1 gives an error diagnostic. |
| `* ssp:part <ref> <part-id>` | The part in the parts table that a reference uses. This mapping wins over the default mapping by kind and model name. An unknown part id gives an error diagnostic. A reference with no part gives a warning. |

Rules:

- A directive line starts with `* ssp:` at the start of the line. Leading white space is allowed.
- `<pos>` is a number. For `knob`, it is the fraction of the turn, from 0 (0%) to 1 (100%).
- `<taper>` of a `knob` is `linear`, `log` or `revlog`. At the half-way position, the share below the wiper is 50% for `linear`, 10% for `log` and 90% for `revlog`.
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

## LTspice import

`AscImporter.ToNetlist` makes a netlist from the text of an LTspice `.asc` file. `AscImporter.Import` gives the same netlist and the diagnostics.

| `.asc` line | Result |
|---|---|
| `WIRE x1 y1 x2 y2` | Connects the two points. |
| `FLAG x y <name>` | Gives the name to the net at the point. `0` is ground. |
| `SYMBOL <name> x y <orientation>` | One element. The pins come from `PinTable.Builtin`. |
| `SYMATTR InstName <ref>` | The reference of the element. |
| `SYMATTR Value <value>` | The value or model name of the element. |
| `TEXT x y <align> <size> !<directive>` | A SPICE line, for example `.model`. |

Rules:

- The symbols are `res`, `cap`, `ind`, `diode`, `npn`, `pnp`, `njf` and `opamp`. Any other symbol gives a diagnostic with severity error that names the symbol. The symbol is not imported.
- The orientation is `R0`, `R90`, `R180`, `R270`, `M0`, `M90`, `M180` or `M270`. Any other orientation gives a diagnostic with severity error.
- A symbol with no `InstName` gives a diagnostic with severity error.
- A wire end, a pin or a flag on a wire connects to that wire, also between the ends of the wire.
- Flags with the same name are one net. A net with no flag gets the name `N001`, `N002` and so on, in the order of the pins.
- The netlist has the elements in file order, then the directives, then `.end`.
- Other lines, such as `WINDOW`, `SHEET` and `TEXT` comments, are ignored.

### Layout import

`AscImporter.ToLayout` makes a layout from the text of an LTspice `.asc` file. Write it with `Layout.Write` to get the layout file.

- Each symbol that the netlist import includes gets one `part` entry with the same reference. Other symbols get no entry.
- The layout has no wires.
- `x` and `y` are in LTspice units. They are the LTspice position of the ssp symbol origin: the first pin for `res`, `cap`, `ind`, `diode`, `npn`, `pnp` and `njf`, and the point between the two inputs for `opamp`.
- LTspice turns clockwise and the layout turns counter-clockwise. An `M` orientation sets `flip = true`.
- The LTspice `res`, `cap`, `ind` and `diode` symbols are vertical at `R0`. The ssp symbols are horizontal at rotation 0. Thus these symbols get 270 degrees more rotation:

| Orientation | `res`, `cap`, `ind`, `diode` | `npn`, `pnp`, `njf`, `opamp` |
|---|---|---|
| `R0` | 270 | 0 |
| `R90` | 180 | 270 |
| `R180` | 90 | 180 |
| `R270` | 0 | 90 |
| `M0` | 270, flip | 180, flip |
| `M90` | 180, flip | 90, flip |
| `M180` | 90, flip | 0, flip |
| `M270` | 0, flip | 270, flip |
