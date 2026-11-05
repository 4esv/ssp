# 0006: The working format of a circuit

## Status

Accepted (option A). It keeps [0003](0003-netlist-source-of-truth.md).

## Context

A user asked for Verilog as the working format of a circuit (issue #280).
Verilog is a digital hardware description language. It has no voltage, no bias point and no `.model` card.
Verilog-A models the behaviour of one device. Verilog-AMS adds digital behaviour to it.
Neither language holds a schematic position or a wire route. ssp simulates SPICE.
So Verilog cannot be the working format. It stays a possible export format.

The netlist model costs the editor in four places:

- A netlist has no positions and no wire routes. The layout file holds both, and the two files must agree.
- A netlist has `.subckt` hierarchy, but the editor cannot enter an instance.
- A jack is a comment line. It is not a part with a position.

## Decision

Keep the SPICE netlist as the source of truth. Do not change the file format.
Option A does three things:

1. Move the Netlist panel out of the default layout. It becomes the import and export panel.
2. Draw the named pins of a `.subckt` instance on the canvas.
3. Let the user enter and leave a `.subckt` instance.

Rejected: a structural document as the working format (option B).
It touches the engine boundary, the fixtures, the layouts, the golden files, the share links and every
export. The value is editor ergonomics, and the owner puts the editor last, so it comes back in a new record
after the engine work.

Rejected: a Verilog working format (option C). ssp has no Verilog parser, and the engine cannot read the result.

## Consequences

- The netlist stays the source of truth. The two-file rule of [0003](0003-netlist-source-of-truth.md) stays.
- The user sees the schematic first, and the netlist text on request.
- The CLI, the file format and the corpus do not change.
- Verilog export stays open. This record does not schedule it.
