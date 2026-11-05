# Models

This folder holds the device models. Each model is written from datasheet values (clean-room rule).

## Provenance header

Each model file starts with these comment lines:

```spice
* provenance: <datasheet title>, <manufacturer>, <datasheet URL>
* fit: <the datasheet points that the parameters match>
* author: <name>
* license: MIT
```

A model file without this header fails review.

## Model files

The name is the value after `.model` or `.subckt`.

| File | What it is | Model or subcircuit | Pin order | Source |
| --- | --- | --- | --- | --- |
| `models/bjts.lib` | Two silicon NPN small-signal transistors, the 2N3904 and the 2N5088. | `Q2N3904`, `Q2N5088` | — | 2N3904 datasheet, onsemi; 2N5088 datasheet, onsemi. |
| `models/diodes-si.lib` | The 1N4148 silicon switching diode. The part id `1N914` uses it too. | `1N4148` | — | 1N4148 datasheet, Vishay (document 81857). |
| `models/diodes.lib` | A 5.1 V Zener diode and a small-signal Schottky diode. | `DZ_5V1`, `DSCHOTTKY` | — | Generic values. No single datasheet. Values are UNVERIFIED. |
| `models/jfets.lib` | An N-channel and a P-channel small-signal JFET. | `JN`, `JP` | — | Generic values. No single datasheet. Values are UNVERIFIED. |
| `models/jfet-2n5458.lib` | The 2N5458 N-channel JFET, mid-range values. | `J2N5458` | — | 2N5457/2N5458/2N5459 datasheet, onsemi. Values are UNVERIFIED. |
| `models/led.lib` | Five 5 mm LED colors. | `LED_RED`, `LED_YELLOW`, `LED_GREEN`, `LED_BLUE`, `LED_WHITE` | — | Generic values. No single datasheet. Values are UNVERIFIED. Nearest published family: Kingbright 5 mm LEDs. |
| `models/opamp-jrc4558.cir` | A dual operational amplifier. | `JRC4558` | `inp inn out vcc vee` | NJM4558 datasheet, Nisshinbo Micro Devices (JRC). |
| `models/opamp-lm308.cir` | A single operational amplifier. | `LM308` | `inp inn out vcc vee` | LM108/LM208/LM308 datasheet, National Semiconductor. |
| `models/opamp-tl072.cir` | A dual JFET-input operational amplifier. | `TL072` | `inp inn out vcc vee` | TL07xx datasheet, Texas Instruments. |
| `models/transformer.cir` | An ideal two-winding transformer. | `transformer` | `p1 p2 s1 s2` | No manufacturer part. |
| `models/vactrol.cir` | A vactrol. It is an LED and a photoresistor in one package. | `vactrol` | `a k p1 p2` | Generic. No single datasheet. Values are UNVERIFIED. Nearest published families: Silonex and PerkinElmer vactrols. |

### Subcircuit pins

The op-amp subcircuits use the pins `inp inn out vcc vee`: the non-inverting input, the inverting input, the
output, the positive supply rail and the negative supply rail.

The transformer uses `p1 p2` for the primary winding and `s1 s2` for the secondary winding.
The vactrol uses `a k` for the LED anode and cathode, and `p1 p2` for the photoresistor terminals.

The transformer and the vactrol also take parameters:

- `transformer`: `n` (the turns ratio Ns/Np), `lp` (the primary inductance), `k` (the coupling).
- `vactrol`: `tau` (the lag time constant), `rdark` (the dark resistance), `gain` (the conductance per mA of
  LED current).

The defaults are in the `.subckt` line.

## Get the models

The package has no models. The models and the sample audio are in the repository.
Download one model into the same folder as your netlist:

```sh
curl -fsSLO https://raw.githubusercontent.com/4esv/ssp/main/models/opamp-tl072.cir
```

## Use a model

Write `.include <path>` in your netlist. A relative path is relative to the directory of the netlist file,
not to the directory where you run `ssp run`. Quotes around the path are optional. An absolute path also works.

```spice
* ssp:title TL072 unity-gain buffer
* ssp:input in
* ssp:output out
.include models/opamp-tl072.cir
V1 in 0 DC 1 AC 1
Vcc vcc 0 DC 15
Vee vee 0 DC -15
X1 in out out vcc vee TL072
Rl out 0 10k
.END
```

Save the netlist as `buffer.cir` in the repository root, then run it from any directory:

```sh
ssp run <repository>/buffer.cir
```

The `.include` line loads `models/opamp-tl072.cir`. The `X1` line calls the `TL072` subcircuit with the pins
`inp inn out vcc vee`. The output follows the input (unity gain).

## Parts table

`models/parts.toml` holds the parts table. Each row has these fields: `id`, `kind`, `model`, `symbol`,
`footprint`, `buy_url`, `provenance`.

| id | kind | model | footprint |
| --- | --- | --- | --- |
| `1N4148` | diode | `1N4148` | DO-35 |
| `1N914` | diode | `1N4148` | DO-35 |
| `2N5458` | njf | `J2N5458` | TO-92 |
| `2N3904` | npn | `Q2N3904` | TO-92 |
| `2N5088` | npn | `Q2N5088` | TO-92 |
| `TL072` | opamp | `TL072` | DIP-8 |
| `LED-RED` | led | `LED_RED` | LED-5MM |
| `LED-YELLOW` | led | `LED_YELLOW` | LED-5MM |
| `LED-GREEN` | led | `LED_GREEN` | LED-5MM |
| `LED-BLUE` | led | `LED_BLUE` | LED-5MM |
| `LED-WHITE` | led | `LED_WHITE` | LED-5MM |
| `TRANSFORMER` | transformer | `transformer` | none |
| `VACTROL` | vactrol | `vactrol` | none |
| `ZENER-5V1` | zener | `DZ_5V1` | DO-35 |
| `SCHOTTKY` | schottky | `DSCHOTTKY` | DO-35 |
| `JFET-N` | njf | `JN` | TO-92 |
| `JFET-P` | pjf | `JP` | TO-92 |

### The `ssp:part` directive

Write `* ssp:part <ref> <id>` to map a netlist reference to a part id. The directive sets the symbol, the
footprint and the buy link for that reference.

```spice
* ssp:part D1 1N4148
V1 a 0 5
D1 a b DGEN
R1 b 0 1k
.MODEL DGEN D
.END
```

Here `D1` uses the `1N4148` part row and the `DGEN` model. The part row gives the footprint and the buy link.
The model gives the simulation. The schematic uses the symbol of the part id.

A `<ref>` that is not in the netlist gives a warning. An `<id>` that is not in `models/parts.toml` gives an
error. The editor and `ssp run` both report these problems.
Without the directive, the loader matches the part by kind and model name.

## Editor palette

The editor palette offers kinds, not part ids: resistor, capacitor, electrolytic, inductor, diode, zener,
schottky, led, npn, pnp, njf, pjf, pot, switch-1, switch-2, switch-3, battery, source, jack-in, jack-out.

A semiconductor kind places a default model. These ids match a palette kind's default model:

| Palette kind | Default model | Part id |
| --- | --- | --- |
| zener | `DZ_5V1` | `ZENER-5V1` |
| schottky | `DSCHOTTKY` | `SCHOTTKY` |
| led | `LED_RED` | `LED-RED` |
| njf | `JN` | `JFET-N` |
| pjf | `JP` | `JFET-P` |

The other ids are not on the palette: `1N4148`, `1N914`, `2N3904`, `2N5088`, `2N5458`, `TL072`, `TRANSFORMER`, `VACTROL`,
`LED-YELLOW`, `LED-GREEN`, `LED-BLUE`, `LED-WHITE`. The diode kind places a generic `DGEN` model and the npn kind
a generic `QNPN` model. Neither has a part id.

## Supply your own model

The `1N4148` and `1N914` parts use the `1N4148` model in `models/diodes-si.lib`. Other pedal parts, for
example `BC109`, are not bundled.

Write the model in your netlist, or add a `.lib` file and `.include` it. Write the values from the datasheet
(clean-room rule). Change the name and the values for your part. Do not copy model text from a manufacturer
file or from another simulation tool.

```spice
.model QNPN NPN (IS=1e-14 BF=200)
Q1 c b 0 QNPN
```

To ship a model in this repository, add the provenance header to the file.

## Cabinet impulse response

`models/ir/cab-1x12.wav` is a synthetic cabinet impulse response. It is not a measurement of a real cabinet.
See `models/ir/README.md` for the format and the license. Use it with `ssp render`:

```sh
ssp render file.cir --in a.wav --out b.wav --ir models/ir/cab-1x12.wav
```
