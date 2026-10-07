# 0007: No AOT compilation for the web build

## Status

Rejected. Keeps the untrimmed publish of [0005](0005-untrimmed-web-build.md) and the static app of [0002](0002-static-wasm.md).

## Context

The Try of the virtual amp is about 25 times slower in the browser than `ssp render` (#218).
`dotnet publish` recommends the `wasm-tools` workload. The issue asks to measure AOT before adding it, and to stop if the gain is below 2 times.

AOT does not build without IL trimming: the publish fails with `AOT is not supported without IL trimming (PublishTrimmed=true required)`.
So the measurement is AOT with trimming, against the untrimmed baseline that [0005](0005-untrimmed-web-build.md) chose on purpose.

The numbers are measured on an Apple silicon Mac (arm64, macOS), Chromium headless through Playwright, serving the published `wwwroot` the way `scripts/playwright.sh` does.
The preset chain time is the `Try wall time` line of `VirtualAmpLiveChainTests.TryOnThePresetChainFinishesWithinTheLimit`; the drawn fuzz time is the line of `TryOnTheDrawnFuzzPlaysThroughTheAmp`. The runs are one after the other, never at the same time.

| Item | Before (untrimmed, no AOT) | AOT (trimmed) | Ratio |
|---|---|---|---|
| Preset chain Try wall time, 3 runs | 9.1 s, 9.1 s, 9.2 s (mean 9.1 s) | 5.1 s, 5.1 s, 5.2 s (mean 5.1 s) | 1.78x |
| Drawn fuzz Try wall time, 2 runs | 30.8 s, 31.0 s (mean 30.9 s) | 13.2 s, 12.9 s (mean 13.1 s) | 2.37x |
| Publish time | 28 s | 228 s | 8.1x |
| Brotli download (sum of the `.br` files in `wwwroot`) | 13,771,208 bytes (13.77 MB) | 19,205,495 bytes (19.21 MB) | +39.5 % |
| Publish size (`du -sh wwwroot`) | 77 MB | 133 MB | +73 % |

The acceptance metric of the issue is the preset chain at 2 times or more. It is 1.78 times.
The drawn fuzz is 2.37 times, but it is not the named metric.

## Decision

Do not turn on AOT. Leave `RunAOTCompilation` out of `src/Ssp.Web/Ssp.Web.csproj` and keep `PublishTrimmed` false.

The value is below the effort:

- The preset chain gains less than the 2 times the issue asked for.
- The download grows by 39.5 % with Brotli, and the first load is the cost that a new user pays.
- The publish grows 8.1 times, which would slow every CI run that publishes the web app; CI would also need the `wasm-tools` workload installed.
- AOT needs `PublishTrimmed=true`, which would reverse the deliberate untrimmed decision of [0005](0005-untrimmed-web-build.md). That reversal must pay on its own; it does not here.

## Consequences

- The shipped build does not change, so `docs/benchmarks.md` is not changed by this record.
- The Try of the virtual amp stays about 25 times slower in the browser than `ssp render`. Faster solving in `Ssp.Core` is the lever that is left (out of scope for #218).
- AOT can come back if the preset chain time or the download cost changes. The method and the numbers above are the comparison to beat.
