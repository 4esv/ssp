# 0003: The netlist is the source of truth

## Status

Accepted.

## Context

Agents and engineers know SPICE netlists. Other SPICE tools read netlists.
A schematic needs positions, but a netlist has no positions.

## Decision

Store the circuit as a SPICE netlist (`.cir`).
Store schematic positions in a separate layout file (`<name>.layout.toml`).
Store ssp settings as `* ssp:` comment lines in the netlist.

## Consequences

- Other SPICE tools can read ssp circuits.
- A circuit without a layout file is valid. The web app places the parts automatically.
- The layout file must agree with the netlist. ssp reports an error when it does not.
