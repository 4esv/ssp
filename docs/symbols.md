# Symbols

One row for each kind in `Symbols.Kinds`. The palette icon and the canvas drawing are the same markup, `Symbols.For(kind).Svg`. `SymbolTests` fails when a kind in `SchematicEdits.Kinds` has no symbol, when two kinds share one drawing, or when a palette icon differs from its symbol.

The text of IEC 60617 was not at hand when this table was written. A verdict is against the usual guitar-pedal drawing convention. Where a row says UNVERIFIED, the standard text was not read. Axel: please confirm or correct these rows.

Verdict: correct, wrong (fixed in #277), doubtful.

| Kind | Symbol | Verdict | Standard or convention | Source of the drawing |
|---|---|---|---|---|
| `resistor` | US zigzag | correct | IEC uses a box; the US zigzag is the usual pedal convention. UNVERIFIED against IEC 60617 | Own drawing |
| `pot` | Zigzag with a wiper arrow at the track | correct | Variable resistor with a wiper arrow. UNVERIFIED | Own drawing |
| `capacitor` | Two straight plates | correct | Non-polarised capacitor | Own drawing |
| `electrolytic` | Straight plate with `+`, curved plate | correct | Polarised capacitor: curved plate is the negative one | Own drawing |
| `inductor` | Four arcs | correct | Coil | Own drawing |
| `diode` | Triangle and bar | correct | Triangle points from anode to cathode | Own drawing |
| `led` | Diode with two arrows leaving it | correct | Arrows point away for an emitter | Own drawing |
| `npn` | Bar, collector, emitter with an arrow out | correct | Emitter arrow out for NPN | Own drawing |
| `pnp` | Same, emitter arrow in | correct | Emitter arrow in for PNP | Own drawing |
| `njf` | Channel, gate arrow in | correct | Gate arrow in for N-channel | Own drawing |
| `pjf` | Channel, gate arrow out | correct | Gate arrow out for P-channel | Own drawing |
| `opamp` | Triangle, `-` above `+` | correct | Inverting input on top | Own drawing |
| `opamp5` | Same, with supply pins | correct | Supply pins at the top and bottom | Own drawing |
| `battery` | Cell: long plate (positive) and short plate, `+` mark | wrong, fixed | Cell is a long and a short plate. It was a circle with `+`, the same as an AC source. The value label shows the voltage | Own drawing |
| `source` | Circle with one sine period | wrong, fixed | AC source is a circle with a sine. It was a circle with `+` and `-` | Own drawing |
| `vsource` | Removed | wrong, fixed | A netlist source is now `battery` (DC only) or `source` (AC or waveform) | n/a |
| `isource` | Circle with a current arrow | correct | Current source. UNVERIFIED | Own drawing |
| `jack-in` | Mono 1/4 inch jack: tip spring on the pin, sleeve contact to ground, label `IN` | wrong, fixed | The palette used the resistor zigzag. The canvas used a ring | Own drawing, usual pedal drawing |
| `jack-out` | Same, label `OUT` | wrong, fixed | Same | Own drawing |
| `ground` | Three bars, narrowing | doubtful | Signal ground and earth have two symbols in IEC 60617. The editor draws one. UNVERIFIED, not changed | Own drawing |
| `rail` | Bar on a stem, label is the voltage | doubtful | Supply rail (9 V). UNVERIFIED | Own drawing |
| `transformer` | Two coils with two core lines | correct | Iron-core transformer | Own drawing |
| `switch-1` | Contact circles and a lever | correct | Single-throw switch | Own drawing |
| `switch-2` | Common and two throws | correct | Single-pole double-throw | Own drawing |
| `switch-3` | Common and three throws | correct | Single-pole three-throw | Own drawing |
| `vactrol` | LED above, resistor below, light arrows | doubtful | No single standard for an opto-coupled resistor. UNVERIFIED | Own drawing |

## Open questions

- Ground: draw earth and signal ground as two symbols? Out of scope here; it needs a new kind.
- Resistor: keep the US zigzag, or switch to the IEC box?
