# Web app

The web app is at <https://ssp.aesv.io>. It is a static page. It runs the simulation in your browser, and it sends no circuit to a server.
These documents are at <https://ssp.aesv.io/docs/>.

The home page links to the Editor, the Gallery and the Docs. The Gallery lists the circuits of the library. A link in the Gallery opens that circuit in the Editor.

## The editor

The editor has panels: Netlist, Schematic, Knobs, Results, Plots, Calculators, DC overlay, Compare, Clip, Monitor and Virtual amp.
On a narrow screen the schematic fills the screen. The panel bar at the bottom opens one other panel over it.
The netlist is the source of truth. The schematic is a drawing of it. A change in the schematic changes the netlist, and a change in the netlist redraws the schematic.

The name of the project and the Projects list are at the top. See [Projects and files](#projects-and-files).

### Add a part

There are two ways to add a part.

1. Tap a part in the palette ("Add a part"). The part appears at the centre of the canvas.
2. Tap a pin of a part on the canvas. Small "+" marks show the free directions around the pin. Tap a "+". A menu lists the parts, Wire, Ground and Connect to a pin. The part you pick joins that pin.

With a pin active, you can use keys instead of taps. Press an arrow key to choose a direction. Then press a letter:

| Key | Part |
|---|---|
| `r` | Resistor |
| `c` | Capacitor |
| `e` | Electrolytic capacitor |
| `h` | Inductor |
| `d` | Diode |
| `z` | Zener diode |
| `k` | Schottky diode |
| `l` | LED |
| `q` | NPN transistor |
| `n` | PNP transistor |
| `j` | N-channel JFET |
| `y` | P-channel JFET |
| `p` | Pot |
| `b` | Battery |
| `s` | Source |
| `i` | Jack in |
| `o` | Jack out |
| `w` | Wire |
| `g` | Ground |

The palette also has switches and Blocks. Blocks are ready-made stages (a gain stage, a clipper, a tone control) and your saved blocks. Pick a block, then tap an empty spot, a wire or a junction to insert it.

### Wire two pins

1. Tap the first pin.
2. Tap a "+", and pick Connect to a pin in the menu.
3. Tap the second pin.

Or choose the Wire tool, tap a pin, and pick a direction.

The Ground tool, or Ground in the menu, ties a pin to ground (node 0).
A circuit needs one node tied to ground.

### Jack in and Jack out

A Jack in marks the input of the circuit. A Jack out marks the output. They write the `* ssp:input` and `* ssp:output` lines of the netlist.
Tap a pin first. Then tap Jack in or Jack out in the palette, or pick it in the menu of the pin.
Clip, Monitor and the Virtual amp need a Jack in. The Virtual amp also needs a Jack out.

### Select, move and edit

- Tap a part to select it. Its value shows in a box. Type a new value and press Enter. The value uses the same syntax as `--set` in the [CLI](cli.md#number-syntax).
- Drag a part to move it.
- A selected part shows the actions Delete, Rotate, Flip and Duplicate. A long press on a part opens them on a touch screen.
- With a part selected, press `r` to rotate, `h` or `v` to flip, an arrow key to move, and Delete to remove it.
- Choose Select and drag a box to select a group. The group actions are Delete, Copy, Cut and Save as block. Paste shows after a copy.
- Tap a wire to select it. A box shows the name of its net. Type a new name and press Enter.
- Zoom with the `-` and `+` buttons or keys. Press `0` to fit the circuit.
- Undo and Redo are in the toolbar. New starts an empty circuit. Undo brings the old circuit back.

The problems list on the canvas names each problem in the circuit. Tap a problem to select its part.
While the netlist has an error, the palette and the edit tools are off. Fix the error in the Netlist panel.

### Run and results

Press Run (Ctrl+Enter or Cmd+Enter). The Results panel shows the diagnostics and the DC voltage of each node. The schematic shows the voltage on each net.
An edit solves the circuit again by itself, a short time after the last change. The toolbar shows "Solving…" during the solve and "Stale" when the last edit does not solve. The results then stay from the last good run.

The run is the same small-signal analysis as `ssp run`. It does not show clipping. Use Clip to hear it.

### Plots

The Plots panel shows the frequency response (magnitude in dB and phase), the impedance and the noise density. The axes are logarithmic in frequency.

Compare plots the gain for a list of values of one part. Pick a part, type values such as `1k, 4k7, 10k`, and press Compare. Freeze keeps a result on the plot while you change the circuit.

Knobs shows a slider for each `* ssp:knob` line and a button group for each switch. Moving a slider runs the circuit again.

### Play a clip

The Clip panel renders a sample through the circuit and plays it. It is a transient analysis, like `ssp render`, so a clipper clips.

1. Pick a sample, or choose "Load a WAV file...".
2. Press Play clip. Run also starts a clip when the run has no errors.
3. Press Render again to render again. Press Cancel to stop a render. Press Download WAV to save the result.

The clip plays through a cabinet impulse response when the sample rate fits it.
A render takes seconds. Change the circuit and the panel says "Out of date". Play old plays the old render.

The Monitor panel plays the input of your audio device through the circuit. Press Start monitor. It is not real time. The panel shows the latency.

### Virtual amp

The Virtual amp panel plays a sample through a chain of blocks, one for each stage of an amp.
Pick a block for each stage and set the knobs. Press Try to hear the chain.
"Use my circuit as the pedal" puts the circuit of the editor first in the chain. It needs a Jack in and a Jack out.

### Share links

Share link (in the Export menu) copies a link that holds the netlist. The text of the netlist is in the part of the address after `#`, so no server stores it.
Open the link and the editor loads that circuit as a new project.

### Exports

The Export menu in the toolbar has:

| Item | Result |
|---|---|
| Share link | A link that holds the netlist. |
| Download SVG | The schematic as an SVG file. |
| Download PDF | The schematic and the parts list as a PDF file. |
| Parts CSV | The parts list as a CSV file. |
| Parts | The parts list on screen, with a search link for each part. |

### Projects and files

The editor saves each project in your browser. The Projects list opens, duplicates and deletes projects.
Export .cir saves the netlist as a file. Import .cir loads a netlist file.

### Shortcuts

| Action | Keys |
|---|---|
| Run | Ctrl+Enter or Cmd+Enter |
| Undo | Ctrl+Z or Cmd+Z |
| Redo | Ctrl+Shift+Z or Cmd+Shift+Z |
| Share link | Ctrl+Shift+L or Cmd+Shift+L |
| Download SVG | Ctrl+Shift+E or Cmd+Shift+E |
| Command palette | Ctrl+K or Cmd+K |
| Shortcut sheet | ? |
