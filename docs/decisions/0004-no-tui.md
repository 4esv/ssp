# 0004: No TUI

## Status

Accepted.

## Context

ssp has two kinds of users: people and agents.
Agents need text input and JSON output. People need a schematic and plots.
A terminal UI gives neither of these well.

## Decision

Make a command line tool and a web app. Do not make a terminal UI.

## Consequences

- The command line tool writes plain text or JSON. It does not use interactive screens.
- All interactive work happens in the web app.
