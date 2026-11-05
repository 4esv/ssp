# 0002: Static WebAssembly app

## Status

Accepted. The publish setting changed in [0005](0005-untrimmed-web-build.md).

## Context

The web app must simulate a circuit without a server.
A Blazor WebAssembly publish is a set of static files, and it runs in a phone browser.

## Decision

Make the web interface a static Blazor WebAssembly app with no backend.

## Consequences

- Any static file host serves the app.
- All simulations run on the device of the user. The speed depends on that device.
- A long simulation needs a Web Worker, so that the page continues to respond.
