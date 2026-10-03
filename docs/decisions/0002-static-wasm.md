# 0002: Static WebAssembly app

## Status

Accepted.

## Context

The web interface must run simulations without a server.
A trimmed Blazor WebAssembly publish had 0 trim warnings and was 7.8 MB with Brotli.
An operating point analysis ran correctly in a phone browser (Vout = 0.6666 V).

## Decision

Make the web interface a static Blazor WebAssembly app with no backend.
Publish with trimming on.

## Consequences

- Any static file host can serve the app.
- All simulations run on the device of the user. Speed depends on that device.
- Long simulations need a Web Worker so that the page continues to respond.
