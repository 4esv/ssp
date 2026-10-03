# Benchmarks

This file records measured simulation speed. Record only numbers that you measured.
Real-time factor = seconds of audio simulated / seconds of wall time.

## Circuit

A discrete clipper: a BJT gain stage into a silicon diode clipper.
The netlist is [`circuits/fixtures/clipper-bjt-si.cir`](../circuits/fixtures/clipper-bjt-si.cir).

| Stage | Parts |
|---|---|
| Gain stage | BJT common-emitter booster. Gummel-Poon model, generic textbook values, BF 300. |
| Coupling | 1 uF capacitor. |
| Clipper | Antiparallel silicon diode pair over 10k. |
| Low-pass filter | 4k7 series resistor, 10n capacitor. |
| Load | 100k. |

## Conditions

| Item | Value |
|---|---|
| Input | Sine, 0.3 V peak, 440 Hz |
| Time step | 1/44100 s (44.1 kHz) |
| Runtime | Native .NET 9, Apple Silicon, warm JIT |
| Output peak | 0.639 V (`Analyses.Render` in the .NET 10 test run, oversample 1 and 4, last 100 ms of 200 ms) |

The benchmark program is not in the repository yet. `scripts/bench.sh` will add it.

## Results

The real-time factors below were measured before `clipper-bjt-si.cir` was written. They are not measured on it yet.

| Platform | Method | Sample rate | Real-time factor |
|---|---|---|---|
| Native .NET 9, Apple Silicon | Adaptive trapezoidal | 44.1 kHz | 8.2x |
| Native .NET 9, Apple Silicon | Fixed trapezoidal | 44.1 kHz | 8.3x |
| Native .NET 9, Apple Silicon | Fixed Euler | 44.1 kHz | 7.7x |
| Native .NET 9, Apple Silicon | Fixed trapezoidal | 176.4 kHz (4x oversample) | 3.2x |
| Browser (WebAssembly) | - | 44.1 kHz | see [Browser](#browser) |

## Browser

`EngineSpeedTests` renders 1 s of audio through `clipper-bjt-si.cir` in Chromium and records the wall time.
Run it with `scripts/playwright.sh`. The test log shows the times at detailed verbosity.

The test publishes a small WebAssembly page that calls `Analyses.Render` through one `[JSExport]` method.
Ssp.Web has no render entry point yet. The runtime is the same Mono WebAssembly runtime (interpreter, no AOT).
The page times the call with `performance.now()`. The time includes the marshalling of the samples.
The browser output agrees with native `Analyses.Render` to 1 uV on each sample.

| Item | Value |
|---|---|
| Machine | Apple M3 Pro, macOS |
| Browser | Chrome for Testing 153.0.8010.12, headless (Playwright 1.63.0) |
| Runtime | .NET 10 Mono WebAssembly, interpreter, no `wasm-tools` workload |
| Method | `Analyses.Render`, fixed trapezoidal, oversample 1 |
| Input | Sine, 0.3 V peak, 440 Hz, 44.1 kHz |
| Runs | 5. The values are the median, with the range. |

| Render | Wall time | Real-time factor |
|---|---|---|
| 1 s of audio, first call | 3660 ms (3625 to 3720) | 0.27x |
| 1 s of audio, second call | 3410 ms (3381 to 3419) | 0.29x |
| 0.1 s of audio | 366 ms (360 to 377) | 0.27x |

The browser renders slower than real time. The wall time is linear in the length: 0.1 s takes 0.11 of the time of 1 s.
