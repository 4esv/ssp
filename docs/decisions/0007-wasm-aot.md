# 0007: No AOT compilation for the web build

## Status

Rejected. It keeps the untrimmed publish of [0005](0005-untrimmed-web-build.md).

## Context

The Try of the virtual amp is about 25 times slower in the browser than `ssp render` (#218).
`dotnet publish` offers the `wasm-tools` workload. The issue asks for a measure of AOT before it is added,
and to stop below a gain of 2 times.

AOT does not build without IL trimming. The publish fails with
`AOT is not supported without IL trimming (PublishTrimmed=true required)`.
So the measure is AOT with trimming, against the untrimmed publish that
[0005](0005-untrimmed-web-build.md) chose on purpose.

The measures are on an Apple silicon Mac, with Chromium headless through Playwright, on the published
`wwwroot`, served the way `scripts/playwright.sh` serves it. The runs are one after the other.

| Item | Untrimmed, no AOT | AOT, trimmed | Ratio |
|---|---|---|---|
| Preset chain Try, mean of 3 runs | 9.1 s | 5.1 s | 1.78x |
| Drawn fuzz Try, mean of 2 runs | 30.9 s | 13.1 s | 2.37x |
| Publish time | 28 s | 228 s | 8.1x |
| Brotli download | 13.77 MB | 19.21 MB | +39.5 % |

The metric of the issue is the preset chain at 2 times or more. It is 1.78 times.

## Decision

Do not turn on AOT. Keep `RunAOTCompilation` out of `src/Ssp.Web/Ssp.Web.csproj` and keep `PublishTrimmed` false.

## Consequences

- The shipped build does not change, so [benchmarks](../benchmarks.md) does not change.
- The Try of the virtual amp stays about 25 times slower in the browser than `ssp render`.
  A faster solve in `Ssp.Core` is the lever that is left.
- The download would grow by 39.5 % with Brotli, and the publish by 8.1 times. CI would need the `wasm-tools`
  workload, and AOT needs `PublishTrimmed=true`, which would reverse the decision of
  [0005](0005-untrimmed-web-build.md).
- AOT can come back if the preset chain time or the download cost changes. The table above is the comparison to beat.
