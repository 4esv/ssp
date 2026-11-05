# 0005: Untrimmed web build

## Status

Accepted. It changes the trimmed publish of [0002](0002-static-wasm.md).

## Context

The solver runs in the browser, and the Try of the drawn fuzz is the cost that the user feels.
[Benchmarks](../benchmarks.md) compare the two publishes on one machine, three runs each, one after the other:

| Item | Trimmed | Untrimmed |
|---|---|---|
| Drawn fuzz Try, mean of 3 runs | 32.6 s | 29.1 s |
| Preset chain Try, mean of 3 runs | 9.8 s | 9.0 s |
| Brotli download | 8.6 MB | 10.6 MB |

## Decision

Set `PublishTrimmed` to `false` in `src/Ssp.Web/Ssp.Web.csproj`.
Do not use AOT ([0007](0007-wasm-aot.md)).

## Consequences

- The drawn fuzz Try is 10.8 % faster and the preset chain 7.8 % faster.
- The first load downloads 2.0 MB more (23.5 %). The browser caches it after that.
- The build no longer finds the code that trimming would break. Trim warnings are not a check.
- Trimming can come back for the assemblies that are safe, if the download size matters more than the time.
