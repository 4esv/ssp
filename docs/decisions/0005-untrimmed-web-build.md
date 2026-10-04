# 0005: Untrimmed web build

## Status

Accepted. Changes the "Publish with trimming on" decision of [0002](0002-static-wasm.md).

## Context

The solver runs in the browser, and the time of the drawn fuzz Try is the main cost for the user.
The measure in `docs/benchmarks.md` (3 runs each, same machine, one after the other) shows that the untrimmed publish is faster:
the drawn fuzz Try took 32.6 s on average trimmed and 29.1 s untrimmed (-10.8 %).
The download grows from 8.6 MB to 10.6 MB with Brotli.

## Decision

Set `PublishTrimmed` to `false` in `src/Ssp.Web/Ssp.Web.csproj`.
AOT is out of scope (#218).

## Consequences

- The Try of the drawn fuzz is 3.5 s faster and the preset chain 0.8 s faster.
- The first load downloads 2.0 MB more (+23.5 %). It is cached after that.
- The build no longer finds code that trimming would break. Trim warnings are no longer a check.
- Trimming can come back, for only the assemblies that are safe, if the download size matters more than the time.
