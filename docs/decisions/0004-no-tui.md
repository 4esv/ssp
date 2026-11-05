# 0004: No TUI

## Status

Accepted.

## Context

ssp has two kinds of users: people and agents.
Agents need text input and JSON output. People need a schematic and plots.
A terminal user interface gives neither of these well.

## Decision

Make a command line tool and a web app. Do not make a terminal user interface.

## Consequences

- The command line tool writes plain text or JSON. It has no interactive screens.
- All interactive work happens in the web app.
