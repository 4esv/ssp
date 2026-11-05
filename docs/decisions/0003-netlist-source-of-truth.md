# 0003: The netlist is the source of truth

## Status

Accepted. [0006](0006-working-format.md) keeps this decision and changes the editor.

## Context

Agents and engineers know SPICE netlists. Other SPICE tools read netlists.
A schematic needs positions, and a netlist has no positions.

## Decision

Store the circuit as a SPICE netlist (`.cir`).
Store the schematic positions in a separate layout file (`<name>.layout.toml`).
Store the ssp settings as `* ssp:` comment lines in the netlist.

## Consequences

- Other SPICE tools read ssp circuits.
- A circuit with no layout file is valid. The web app places the parts.
- The layout file must agree with the netlist. ssp reports an error when it does not.
