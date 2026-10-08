# 0006: The working format of a circuit

## Status

Accepted (option A), 2026-10-07. This record changes [0003](0003-netlist-source-of-truth.md).
The owner accepted via the orchestration decision form on 2026-10-07. The acceptance is recorded in a comment on [#280](https://github.com/4esv/ssp/issues/280).

Owner decision: Accepted (option A), 2026-10-07

## Context

An expert user gave feedback on 2026-10-05 (issue #280):

> IMO a Netlist should be an import/export format rather than the primary working format. Verilog would be preferable (although more complicated). Such an upgrade would improve the usability of the Schematic panel.

[0003](0003-netlist-source-of-truth.md) makes the SPICE netlist the source of truth.
The netlist holds the circuit. The layout file `<name>.layout.toml` holds the schematic positions.
`docs/file-format.md:9-10` states the two files.
The editor shows a Netlist tab beside the schematic (`src/Ssp.Web/Pages/Editor.razor:40`).
Each editor action writes the netlist text and the layout together (`src/Ssp.Web/Schematic/SchematicEdits.cs:13`).

The owner's standing directive for this repository:

- Engine and functional work first. UI/UX last.
- "if value < effort cost skip".

This record weighs each option against that directive.

## Question 1: What does "Verilog" mean here?

The owner's answer, verbatim:

> Verilog is a hardware description language used to model, simulate, and build digital electronic circuits like microchips and FPGAs. It was a requested user format.

Verilog proper is a **digital** hardware description language.
It describes logic levels, clocks and bit vectors. It has no concept of a voltage, a bias point or a `.model` card.
A guitar pedal is an analog circuit. A resistor, a capacitor and a transistor operate between two voltages.
Therefore Verilog proper cannot be the working format of an ssp circuit.

The near fits for analog circuits are Verilog-A and Verilog-AMS.
Verilog-A describes the **behaviour** of a device: an equation for a current. It models a transistor, not a board of parts.
Verilog-AMS adds both analog and digital behaviour. It is a modelling language, not a schematic exchange format.
Neither language has a place for a schematic position or a wire route.

What the owner meant: Verilog is a **requested export format**. A user asked for it.
The owner confirms the format is wanted. The owner does not say it is the working format.

This reading rules option C out as the working format.
Verilog stays a possible export target, in a later issue. It is not the source of truth.
Verilog-A stays out of scope. ssp runs SPICE, through the engine (`src/Ssp.Core/Netlist/NetlistLoader.cs:19`).

## Question 2: Where the netlist model blocks the editor

Four cases. Each case has file and line evidence.
For each case, the last line says if a better **file format** or a better **editor model** fixes it.

### 2.1 No positions

A SPICE netlist has no syntax for a coordinate.
ssp stores each position in the layout file (`docs/file-format.md:10`), as `PartPlacement` (`src/Ssp.Core/Layout/Layout.cs:12`), in the `part` tables (`src/Ssp.Core/Layout/Layout.cs:52-60`).
Every edit returns a netlist **and** a layout (`src/Ssp.Web/Schematic/SchematicEdits.cs:13`).
A project saves the two texts as two fields (`src/Ssp.Web/Projects/ProjectStore.cs:37`).
`Layout.Validate` reports an error when the layout names a reference that the netlist does not have (`src/Ssp.Core/Layout/Layout.cs:120-122`).

Verdict: a better **file format** fixes it. The editor model already holds positions.
The problem is that one circuit is two files, and the two files must agree.

### 2.2 No wire routes

A wire is a drawing, not an electrical element. The net name carries the electricity.
`WireRoute` holds a net name and a list of points (`src/Ssp.Core/Layout/Layout.cs:15`).
The editor adds a wire with no change to the netlist (`src/Ssp.Web/Schematic/SchematicEdits.cs:334-340`).
`Layout.Validate` reports an error when a wire names a net that the netlist does not have (`src/Ssp.Core/Layout/Layout.cs:135-137`).

Verdict: a better **file format** fixes it, together with 2.1. Put the wires in the working document.
The editor model already draws, moves and joins wires.

### 2.3 No hierarchy

The netlist can express hierarchy. `.subckt` blocks exist (`src/Ssp.Core/Parts/OpAmpModel.cs:64`, `src/Ssp.Core/Chains/Chain.cs:101`).
The editor cannot see inside one:

- The loader flattens each `X` line into parts named `X1.D1` (`src/Ssp.Core/Netlist/NetlistLoader.cs:56`). The layout places the instance `X1` (`src/Ssp.Core/Layout/Layout.cs:113`).
- The renderer "cannot see the .subckt pin names" (`src/Ssp.Web/Schematic/SchematicRenderer.cs:31`). It draws a symbol only for a known 5-pin op-amp model. Any other instance is a plain box (`src/Ssp.Web/Schematic/SchematicRenderer.cs:386-389`).
- The editor does not change the lines inside a `.subckt` block (`src/Ssp.Web/Schematic/SchematicEdits.cs:1371`, `src/Ssp.Web/Schematic/SchematicEdits.cs:1412`). It copies a block whole (`src/Ssp.Web/Schematic/SchematicEdits.cs:719-731`).

Verdict: a better **editor model** fixes it. The format already has `.subckt`.
The editor needs a hierarchy view and the pin names of the instance.

### 2.4 Jacks as comments

A jack is not a part. It is a marker (`src/Ssp.Web/Schematic/SchematicEdits.cs:33`).
Placing one writes a comment line: `* ssp:input <node>` or `* ssp:output <node>` (`src/Ssp.Web/Schematic/SchematicEdits.cs:199-208`).
The directive parser reads these comment lines (`src/Ssp.Core/Netlist/DirectiveParser.cs:6`, `src/Ssp.Core/Netlist/DirectiveParser.cs:10`, `src/Ssp.Core/Netlist/DirectiveParser.cs:42-48`).
A jack has no position of its own. Its symbol sits at the first pin on that node (`src/Ssp.Web/Components/SchematicEditor.razor:612-615`).

Verdict: a better **editor model** fixes the marker. The editor needs a terminal object that the user can move.
The format can carry a terminal, but the node name is portable. A new directive is not needed now.

A note on labels: a net label is a node name in the element lines, not a comment (`src/Ssp.Web/Schematic/SchematicEdits.cs:943-945`).
Every SPICE tool reads it. Labels are not a format problem.

## Question 3: The options

The count of the corpus: `circuits/` holds 67 files, including 46 `.cir` netlists, 19 `.layout.toml` layouts, one LTspice `.asc` and one README.
The golden corpus holds 229 files: 4 in `tests/Ssp.Core.Tests/Golden`, 2 in `tests/Ssp.Cli.Tests/Golden` and 223 in `tests/Ssp.Web.Tests/Golden`.

### Option A: keep the netlist, demote the Netlist tab, add `.subckt` hierarchy

What changes:

- The default dock layout drops the Netlist panel (`src/Ssp.Web/Pages/Editor.razor:239`). The Netlist panel becomes Import/Export. The owner opens it from the existing Export menu (`src/Ssp.Web/Pages/Editor.razor:52-58`) or from the command palette.
- The renderer reads the `.subckt` definition and draws the named pins of an instance (`src/Ssp.Web/Schematic/SchematicRenderer.cs:386-389`).
- The editor gets a hierarchy view: enter and leave a `.subckt` instance.

Cost: small for the Netlist panel. Medium for the hierarchy view. No format change.

Risk: low. The netlist stays the source of truth. No file migrates.

What breaks: only the tests that name the Netlist panel in the default layout (`tests/Ssp.Web.Tests/DockLayoutTests.cs:123`, `tests/Ssp.Web.Tests/EditorLayoutTests.cs:29`).
These tests change with the layout. Everything else keeps working:

| Part | Why it keeps working |
| --- | --- |
| CLI | It reads a netlist file and calls `Runner.Run` (`src/Ssp.Cli/Program.cs:39-42`, `src/Ssp.Core/Runner.cs:21`). |
| 46 fixtures and 19 layouts | The format does not change. |
| 229 golden files | The schematic goldens may change, for the new `.subckt` drawing. |
| Share links | The link holds the netlist text (`src/Ssp.Web/Pages/Editor.razor:342`, `src/Ssp.Web/Sharing/ShareCodec.cs:9-21`). |
| Projects (#173) | The store keeps the netlist and the layout (`src/Ssp.Web/Projects/ProjectStore.cs:37`). |
| KiCad export | It reads the circuit and the layout (`src/Ssp.Core/Export/KiCad.cs:26`). |
| LTspice import | It writes a netlist and a layout (`src/Ssp.Core/Import/LtSpice/AscImporter.cs:54`, `src/Ssp.Core/Import/LtSpice/AscImporter.cs:60`). |

Value against effort: it passes. The reported problem goes away, and the hierarchy work is functional, not cosmetic.

### Option B: a structural document as the working format

What changes: one document holds the parts, the pins, the nets, the wires, the positions and the sub-circuits.
The SPICE netlist becomes an import and an export. The sidecar file disappears.

Cost: very large. `Runner.Run` takes a netlist text (`src/Ssp.Core/Runner.cs:21`).
The engine is a SPICE parser (`src/Ssp.Core/Netlist/NetlistLoader.cs:12`, `src/Ssp.Core/Netlist/NetlistLoader.cs:19`).
The change touches the CLI, the 46 fixtures, the 19 layouts, the 229 goldens, the share codec (`src/Ssp.Web/Sharing/ShareCodec.cs:9-21`), the project store (`src/Ssp.Web/Projects/ProjectStore.cs:37`), the KiCad export (`src/Ssp.Core/Export/KiCad.cs:26`), the LTspice import (`src/Ssp.Core/Import/LtSpice/AscImporter.cs:54`), the block library (`src/Ssp.Web/Library/CircuitLibrary.cs:12`) and the chain composer (`src/Ssp.Core/Chains/Chain.cs:101-120`).

Risk: high. Every saved circuit is a netlist plus a layout today.
A migration must read both during the change. The engine boundary takes a netlist, so ssp must make one for each run.

Value against effort: skip **now**. The value is editor ergonomics. The engine work is not done.
The owner puts UI/UX last. Revisit this option after the engine work, in a new record.

### Option C: a Verilog-flavoured text format as the working format

What changes: the working file is Verilog or Verilog-A text. ssp parses it and writes it.

Cost: the largest. ssp has no Verilog parser. Verilog proper is digital (question 1).
A Verilog-A or Verilog-AMS parser and a translator to the engine model is a new project.

Risk: the highest. The format cannot hold a DC bias point or a SPICE `.model` card.
The ecosystem for analog pedal circuits is SPICE, not Verilog. The engine cannot read the result.

Value against effort: **skip**. Value < effort. Verilog stays a possible **export** format, for the user request.
It is not the working format.

## Decision

Recommended: **option A**. The owner accepted it on 2026-10-07.

Keep the SPICE netlist as the source of truth.
Move the Netlist panel out of the default layout. It becomes the Import/Export panel.
Add `.subckt` hierarchy to the editor.
Do not change the file format. Do not add a new working format.
Verilog stays a possible export format, in a later issue.

## Staged plan

Each stage is one issue. The app works after each stage. Each stage lists its check.

### Stage 1: demote the Netlist panel

Remove `"text"` from the default dock layout (`src/Ssp.Web/Pages/Editor.razor:239`).
Add "Netlist" to the Export menu (`src/Ssp.Web/Pages/Editor.razor:52-58`). The panel opens for import and export.
The panel keeps its text box and its tab.

Check: the default dock shows no Netlist tab. The Export menu opens the panel.
`tests/Ssp.Web.Tests/DockLayoutTests.cs` and `tests/Ssp.Web.Tests/EditorLayoutTests.cs` pass.

### Stage 2: show the pins of a `.subckt` instance

Read the `.subckt` definition of an instance (`src/Ssp.Web/Schematic/SchematicRenderer.cs:386-389`).
Draw a labelled box with the named pins, in the definition order.
The netlist and the layout do not change.

Check: an instance of a circuit block shows its pin names on the canvas.
`tests/Ssp.Web.Tests` passes. The schematic goldens show the pin names, after `UPDATE_GOLDEN=1`.

### Stage 3: enter and leave a `.subckt` instance

A tap on an instance enters its body. The canvas draws the body of the `.subckt`.
An edit writes to the lines inside the `.subckt` block (`src/Ssp.Web/Schematic/SchematicEdits.cs:1412`).

Check: edit a part inside a block. The circuit runs and the netlist reads back.

### Revisit, not now: option B

Open a new record for the structural document when the engine work is done and the owner starts the UI/UX pass.
The trigger is the start of the UI/UX pass. Do not open this issue before that.

## Consequences

- The netlist stays the source of truth. The two-file rule of [0003](0003-netlist-source-of-truth.md) stays.
- The layout file stays. The user does not see it.
- The user sees the schematic first, and the netlist text on request.
- A `.subckt` instance becomes readable on the canvas.
- The engine, the CLI, the file format and the corpus do not change.
- Verilog export stays open, as the owner's requested format. This record does not schedule it.
