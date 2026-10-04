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

Run `scripts/bench.sh` to measure the real-time factor of `clipper-bjt-si.cir`. It runs `ssp render` on 10 s of audio (`BENCH_SECONDS` changes the length), prints the factor, and appends one row to the table in [Results](#results). The wall time includes the process start and the JIT.

## Results

The real-time factors below were measured before `clipper-bjt-si.cir` was written. They are not measured on it yet.

| Platform | Method | Sample rate | Real-time factor |
|---|---|---|---|
| Native .NET 9, Apple Silicon | Adaptive trapezoidal | 44.1 kHz | 8.2x |
| Native .NET 9, Apple Silicon | Fixed trapezoidal | 44.1 kHz | 8.3x |
| Native .NET 9, Apple Silicon | Fixed Euler | 44.1 kHz | 7.7x |
| Native .NET 9, Apple Silicon | Fixed trapezoidal | 176.4 kHz (4x oversample) | 3.2x |
| Browser (WebAssembly) | - | 44.1 kHz | see [Browser](#browser) |
| Native .NET 10, Darwin arm64 | Fixed trapezoidal | 44.1 kHz | 7.2x |
| Native .NET 10, Darwin arm64 | Fixed trapezoidal, one Newton iteration | 44.1 kHz | 10.9x |

### Row format

Each row has four columns: Platform, Method, Sample rate, Real-time factor.
`scripts/bench.sh` writes `Native .NET <major>, <OS> <architecture>`, `Fixed trapezoidal`, `44.1 kHz`, and the factor with one decimal and an `x` suffix.

## One Newton iteration per sample

`tests/Ssp.Core.Tests/Spikes/OneIterationTransient.cs` has a custom time-step loop with one Newton iteration for each sample.
For each sample, the loop loads, factors and solves one time. It accepts the solution without a convergence check.
It does not use `TransientMaxIterations=1`. That setting only fails the convergence.

After the solve, the loop loads the circuit one more time, but it does not factor or solve again.
This load sets the charge and current states from the new solution.
Without it, `Accept` keeps the states of the previous sample. The output then increases by approximately 1.9 times for each sample, and it is NaN after 112 samples.

The row "Fixed trapezoidal, one Newton iteration" in [Results](#results) is from `OneIterationTransient.MeasureRealTimeFactor`, not from `scripts/bench.sh`.
The test calls the loop in the test process, after the JIT. The wall time does not include the process start.

| Item | Value |
|---|---|
| Machine | Apple M3 Pro, macOS |
| Runtime | Native .NET 10, Release, warm JIT |
| Input | Sine, 0.3 V peak, 440 Hz, 44.1 kHz, 10 s |
| Runs | 5. The values are the median, with the range. |

| Loop | Real-time factor |
|---|---|
| One Newton iteration | 10.9x (8.7 to 11.7) |
| `Analyses.Render`, oversample 1, same runs | 6.5x (4.7 to 6.9) |

| Accuracy against `Analyses.Render` (last 100 ms of 200 ms) | Value |
|---|---|
| Output peak, one Newton iteration | 0.638715 V |
| Output peak, `Analyses.Render` | 0.638671 V |
| Peak error | 0.0069 % |
| Maximum difference of one sample | 124.5 mV |

The maximum difference occurs when the output changes polarity. The loop is late on these edges.

### Decision: reject

- The loop is 1.7 times faster than `Analyses.Render`. In the browser, `Analyses.Render` has a real-time factor of 0.29x (see [Browser](#browser)). A speed increase of 1.7 times does not make it real time.
- The difference of one sample is 124.5 mV, which is 19 % of the peak. The peak error is small, but the shape of the edges changes.
- The loop does not do a convergence check. If a circuit does not converge in one iteration, the output is incorrect and there is no error.
- The loop must load the circuit again after the solve, because of the order of `Load` and `Accept` in SpiceSharp 3.2.3. The loop depends on internal behavior of SpiceSharp.

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

## Worker

`WorkerHostTests` renders through the Web Worker of the published Ssp.Web, clicks the page during the render, and records the payload size.
Run it with `scripts/playwright.sh`. The test log shows the values at detailed verbosity.

The conditions are the same as in [Browser](#browser): the same machine, browser, runtime, circuit and input. The render is 3 s of audio, oversample 1.
The render time is from the post to the reply in the page. It includes the start of the second runtime in the worker.
The click response is from the click to the click handler in the page.

| Item | Value |
|---|---|
| Runs | 5. The values are the median, with the range. |
| Render of 3 s of audio | 10414 ms (10373 to 10579) |
| Click response during the render | 3.6 ms (3.1 to 7.9) |

| Payload | Size |
|---|---|
| Request: netlist | 1121 bytes |
| Request: input samples, `Float64Array`, transferred | 1058400 bytes (8 bytes for each sample) |
| Response: output samples, JSON | 2563886 bytes (19.4 bytes for each sample) |

The payload sizes are the same in each run.
A test with the render on the page thread fails: the page handles the click only after the render.

## Try in the virtual amp

Try renders the bundled clip (2.0 s) through the preset chain of the virtual amp, in Chromium, oversample 1.
The render time is from the click on Try to `sspClipBuffer`. The cabinet IR convolution is included.
The longest block is the longest long task in the page (`PerformanceObserver`, entry type `longtask`). The longest gap between two animation frames agrees with it.
The measurement is one run for each build. Chromium is headless, Playwright, on the same machine as [Browser](#browser).

| Build | Render time | Longest main-thread block | Longest frame gap |
|---|---|---|---|
| Before #208 (convolution on the page thread, no progress) | 17.9 s | 1144 ms | 1153 ms |
| After #208 (convolution in the worker, progress, Cancel) | 18.7 s | 331 ms | 348 ms |

The render time does not change. Faster solving is out of scope. The status showed `Rendering…` for the whole run before, and shows `Rendering 1.1 of 2.0 s at 1x oversample.` at 10 s now.
Cancel at 8 s: `Cancelled.` showed 38 ms after the click, and a second Try after the cancel rendered again.
The 331 ms block that is left is the reading of the render result (JSON) and the output on the page thread.

## Fallback to variable steps

A drawn fuzz into the virtual amp stopped the render with `TimestepTooSmallException` at t = 0.540 s (#210).
The chain is [`circuits/fixtures/fuzz-drawn.cir`](../circuits/fixtures/fuzz-drawn.cir), `gain-variable-tl072`, `tone-baxandall-passive` and `power-9v`, composed with `Chain.Compose`.
At the failing step, the node that changes most is `Xgain.o`, the op-amp output of the gain stage (0.127 V in the last iteration). The fixed step cannot get smaller, so Newton does not converge.
The engine's variable trapezoidal method, with the step at most 1 / (fs * oversample), converges. Its smallest step on this input is 1.3e-10 s.

`Analyses.Render` now tries fixed steps first. If a fixed step does not converge, it renders the full input again with variable steps and interpolates the output at the sample times.
If the variable steps also do not converge, it throws `InvalidOperationException` with the time and the node that changes most.

The values are the wall time of `ssp render`, with the process start and the JIT.

| Item | Value |
|---|---|
| Machine | Apple M3 Pro, macOS |
| Runtime | Native .NET 10 (SDK 10.0.401), Release |
| Before | Commit df04cd3b |
| Runs | 3. The values are the median, with the range. |

| Render | Before | After |
|---|---|---|
| Chain, `clip.wav` (2 s), oversample 1 | Fails at t = 0.540 s after 0.66 s (0.65 to 0.67) | 4.47 s (4.35 to 5.66), 0.45x real time |
| Chain, `clip.wav` (2 s), oversample 2 | Fails at t = 0.540 s after 0.85 s (0.83 to 0.85) | 4.75 s (4.65 to 4.75), 0.42x real time |
| `clipper-bjt-si.cir`, sine 0.3 V 440 Hz (10 s), oversample 1 | 1.31 s (1.28 to 1.33) | 1.27 s (1.24 to 1.32) |

The clipper does not use the fallback. Its output RMS (0.590) and peak (0.641) are the same before and after.
The chain output after the change has an RMS of 0.117 and a peak of 0.720 of full scale.
