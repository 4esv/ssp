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

## Variable steps in the browser

The chain of [Fallback to variable steps](#fallback-to-variable-steps) renders `clip.wav` (2.0 s, 44.1 kHz, 88200 samples), oversample 1, in the worker of the published site (Chromium, headless, Playwright, Release, not AOT) and natively (.NET 10, Release), on an Apple M-series Mac (#219).
Before is commit f70d95d3.

### Profile before the change

The values are from `tran.Statistics` of each method, one run.

| Method | Browser | Native | Accepted steps | Newton iterations | Browser Load / Solve |
|---|---|---|---|---|---|
| Fixed steps, to the failure at t = 0.540 s | 9.17 s | 0.52 s | 23814 | 69767 | 6.29 s / 1.05 s |
| Variable steps, all 2.0 s | 144.8 s | 3.71 s | 408988 | 1001553 | 88.4 s / 14.9 s |

The three largest costs:

1. Newton iterations. The browser takes 131 to 147 µs for each iteration with all methods, and native takes 3.3 to 7.5 µs. The browser time is in proportion to the iteration count. Load (the device equations, most of them the behavioral sources of the TL072 model) is 61 % of the time in the browser and natively.
2. Steps for each sample. The variable method takes 4.63 steps and 11.3 iterations for each sample. The fixed method takes 1 step and 2.9 iterations. The cause is the input `Pwl`: it sets a breakpoint at each sample, and after each breakpoint the engine cuts the next step to a tenth of the sample period and sets the integration order to 1. A larger truncation tolerance (`TrTol` 20 to 100) does not change the count (4.43 to 4.46).
3. The fixed-step start that does not converge: 9.2 s in the browser before the variable render starts again from 0.

The progress callback is not a cost: 25 calls for 2.0 s.

### Change

If a fixed step does not converge, the input is a line through the samples with no breakpoints, and a trapezoidal method with variable steps of at most half a sample period lands on each sample time.
A sample time does not cut the step or the order.

| Item | Before | After |
|---|---|---|
| Browser, worker `render` of the full chain, call to result, 3 runs | 154 s (sum of the profile above, not measured end to end) | 81.4 s (81.2 to 81.5) |
| Native, `Analyses.Render`, one run | 4.23 s | 2.42 s |
| Solver steps, 0.50 s to 0.75 s of the clip (`TheVariableStepRenderTakesFewStepsForEachSample`) | 53497 | 25558 |
| Browser, variable part only | 144.8 s, 1001553 iterations | 73.3 s, 511198 iterations |

Variable steps of at most one sample period take 52.4 s in the browser (370381 iterations), but the output differs from before by -57.8 dBFS RMS.

Output against before (the 88200 samples of the full chain, native):

| Item | Value |
|---|---|
| RMS difference | 0.00079, -62.0 dBFS |
| Largest difference | 0.061, -24.3 dBFS, at sample 50032 (1.134 s) |
| Samples that differ by more than -60 dBFS | 5006 of 88200 |
| Samples that differ by more than -40 dBFS | 48 |
| Before against a reference with steps of 1/16 sample | -78.4 dBFS RMS, -43.8 dBFS largest |
| After against the same reference | -61.7 dBFS RMS, -24.1 dBFS largest |

The largest differences are in the fast edges of the gain stage. A change of `TrTol` from 7 to 20 with the old method also moves the largest difference to -40.7 dBFS.

The goal of 30 s in the browser is not met. At 140 µs for each iteration, 30 s is 214000 iterations, 2.4 for each sample. The fixed method alone takes 2.9 for each sample on this chain.

## Cheaper op-amp clamps

The TL072, LM308 and JRC4558 macro-models (`OpAmpModel`) had a soft max inside a soft min for each clamp. Each clamp now has one `sqrt` for each limit and no nested terms (#223).
The low output limit is a node (`Blo`), so that the low limit does not go above the high limit in the source stepping of the operating point.
The fit values of the provenance header do not change: DC gain, GBW, slew rate and rail drop stay within 1 % (`OpAmpCostTests`).

Before is commit f6486869. Native, .NET 10, Release, Apple M-series Mac. The input is 0.5 s of `clip.wav` (samples 22050 to 44099), oversample 1.
The clean chain is the preset amp: `gain-variable-tl072.cir`, `tone-baxandall-passive.cir`, `power-9v.cir`. The fuzz chain is `fuzz-drawn.cir` in front of the same chain.
Load is `LoadTime` of `tran.Statistics` divided by the Newton iterations, the median of 15 runs. The linear row replaces the TL072 with a linear stage (no behavioral sources) and shows the Load of the rest of the chain.

| Item | Before | After | Change |
|---|---|---|---|
| Expression nodes for each model, with the derivatives | 1209 | 447 | -63 % |
| Clean chain, Load for each iteration | 1.82 µs | 1.20 µs | -34 % |
| Clean chain, Load of the TL072 (minus the linear row, 0.63 µs) | 1.19 µs | 0.57 µs | -52 % |
| Clean chain, Newton iterations | 44098 | 44098 | 0 |
| Clean chain, render wall time | 107 ms | 80 ms | -25 % |
| Fuzz chain, Load for each iteration | 2.26 µs | 1.64 µs | -27 % |
| Fuzz chain, Newton iterations | 134008 | 133637 | -0.3 % |
| Fuzz chain, render wall time | 452 ms | 357 ms | -21 % |

The iterations for each sample do not change. The gain is in the cost of each Load. The browser was not measured.

Output against before:

| Chain | RMS difference | Largest difference | Crest factor before | Crest factor after |
|---|---|---|---|---|
| Clean | -263.6 dBFS | -251.8 dBFS | 3.962 | 3.962 |
| Fuzz | -77.4 dBFS | -42.4 dBFS | 5.249 | 5.248 |

The clean output is the same to the precision of the numbers. The fuzz output differs where the clamps conduct, because the rounded corners of the two clamp forms are not the same.

## Default clip of 1 s

The bundled clip is cut from 2.0 s to 1.0 s (#222). The browser render time is proportional to the clip length.
The measurement is the `Try wall time` line of `VirtualAmpLiveChainTests.TryOnThePresetChainFinishesWithinTheLimit`: Playwright, headless Chromium, the preset chain of the Virtual amp, the bundled clip, the published site, one run for each clip, on the same machine. The time is from the click on Try to a final status.

| Item | Before (2.0 s clip) | After (1.0 s clip) |
|---|---|---|
| Try wall time, preset chain | 20.0 s | 10.1 s |

The speed-up is 1.98 times. The 81 s of the drawn fuzz in the issue is a different chain, and it is not measured here.

## Drawn fuzz in the worker, second pass

The chain of [Fallback to variable steps](#fallback-to-variable-steps) renders the bundled clip (1.0 s, 44100 samples), oversample 1, in a Web Worker in headless Chromium, with no Playwright and no page UI (#219).
Before is commit 8883ee4f (#221, #222 and #223 merged). Apple M-series Mac, Release, not AOT. Each value is the median of 3 runs, with the range. The time is from the post of the request to the result.

Two workers are measured:

- Bench worker: a small WebAssembly app, not trimmed, that calls `Analyses.Render` in a worker.
- App worker: the published `src/Ssp.Web` (trimmed), with `js/simulation-worker.js` and `WorkerExports.Render`, as the Try button calls it.

### Answers

1. The fixed-step attempt does not stop at 0.54 s on the 1 s clip. It stops after 264 steps (6 ms of clip) and 807 Newton iterations. In the bench worker, the default render took 31.2 s (30.9 to 31.3), and a render that starts on variable steps took 30.8 s (30.5 to 31.2). The fixed attempt costs about 0.4 s.
2. The worker now keeps the netlist texts that needed variable steps. The next render of the same text starts on variable steps. The saving is the fixed attempt, about 0.4 s on this clip. A knob change changes the text, so it pays the attempt again.
3. #223 did not slow the browser. The old model took 36.9 s (36.9 to 37.2) in the bench worker, and the new model 31.2 s (30.9 to 31.3). The iterations are the same (293992 and 293135).
4. The trimmed app worker took 36.3 s (35.3 to 36.6). The same app published with `PublishTrimmed=false` took 30.6 s and 31.3 s (2 runs), for 10.6 MB of Brotli files against 8.6 MB. A trim root for `SpiceSharp`, `SpiceSharpBehavioral`, `Ssp.Core` or `System.Linq.Expressions` did not change the time (35.1 to 36.7 s).

### Fewer Newton iterations

The variable steps take 2.2 steps and 6.6 iterations for each sample. The step size is at its upper limit (half a sample) for most steps.
The values are native, the full clip, variable steps. The difference is against the output of the commit before, in dBFS.

| Change | Steps | Iterations | RMS difference | Largest difference |
|---|---|---|---|---|
| None | 97454 | 292276 | | |
| Upper step limit 1 sample | 76764 | 243979 | -52.3 | -23.0 |
| Upper step limit 1/1.5 sample | 95331 | 292292 | -55.4 | -22.9 |
| `TrTol` 20 | 89716 | 268448 | -56.5 | -22.1 |
| `RelTol` 0.005 | 97273 | 272191 | -61.3 | -25.8 |
| `RelTol` 0.01 | 97578 | 261715 | -60.6 | -25.8 |
| `RelTol` 0.02 | 97250 | 249114 | -58.0 | -24.8 |
| `AbsTol` 1e-10 | 97564 | 292546 | -62.9 | -27.6 |

A small change of any tolerance moves the output by about -62 dBFS RMS, because the fast edges of the fuzz move. `RelTol` 0.01 is the only change with a gain of more than 5 % and a difference below -60 dBFS. The variable steps now use it. The fixed steps do not change.

Output against before, native, 44100 samples: RMS difference -60.6 dBFS, largest difference -25.8 dBFS at 0.918 s. 2346 samples differ by more than -60 dBFS, and 46 by more than -40 dBFS. The output RMS is 0.19565 before and 0.19568 after.

### Result

| Item | Before | After |
|---|---|---|
| Newton iterations (browser) | 293135 | 262274 |
| Bench worker, default render | 31.2 s (30.9 to 31.3) | 28.4 s (28.2 to 28.6) |
| Bench worker, start on variable steps | 30.8 s (30.5 to 31.2) | 28.0 s (27.9 to 28.1) |
| App worker, `WorkerExports.Render` | 36.3 s (35.3 to 36.6) | 32.0 s (31.5 to 32.7) |

In the app worker, runs 2 and 3 start on variable steps from the cache.
The goal of 30 s in the app worker is not met. The trimmed publish adds about 5 s. The cost that is left is the time of each Newton iteration in the browser (about 107 µs, against 4.5 µs natively). In the profile of #221, 61 % of it is Load, most of it the behavioral sources of the op-amp model.

The 154 s of the issue comment came from the Try button. The app worker takes 36 s for the same chain and clip, so most of the 154 s is not in the worker render. It is not measured here.

## Untrimmed web build (#231)

The web app is published with `PublishTrimmed` set to `false`.
Measured on 2026-10-04 in Chromium with Playwright, through `scripts/playwright.sh`-style serving of the published `wwwroot` (python `http.server`). The runs are one after the other, never at the same time. The two builds differ only in `PublishTrimmed`.

| Item | Trimmed | Untrimmed |
|---|---|---|
| Drawn fuzz Try wall time, 3 runs | 32.4 s, 32.7 s, 32.7 s (mean 32.6 s) | 29.1 s, 29.1 s, 29.0 s (mean 29.1 s) |
| Preset chain Try wall time, 3 runs | 9.8 s, 9.8 s, 9.7 s (mean 9.8 s) | 9.1 s, 9.0 s, 9.0 s (mean 9.0 s) |
| Brotli download (sum of the `.br` files in `wwwroot`) | 8,618,412 bytes (8.6 MB) | 10,643,988 bytes (10.6 MB) |

The drawn fuzz time drops by 10.8 %. The limit of the issue is 10 %. The preset chain drops by 7.8 %. The download grows by 2.0 MB (23.5 %).

## Gallery

`GalleryTimingTests` clicks the Gallery link on the home page of the published site and times it in the page (`performance.now()`).
The time ends at the first animation frame and timer turn after the gallery list is in the DOM, so the main thread is free again.
Run it with `scripts/playwright.sh`. The test log shows the time at detailed verbosity, and the test fails above 1000 ms (#174).

| Item | Value |
|---|---|
| Machine | Apple M3 Pro, macOS |
| Browser | Chromium, headless (Playwright) |
| Build | Release, not AOT |
| Runs | 3 for each build |

| Build | Click to first interaction |
|---|---|
| Previews rendered in the page (loader, placer and renderer on the main thread) | 16281, 16358, 16285 ms |
| Previews are committed SVG files (`wwwroot/previews`, `<img>`) | 13, 14, 15 ms |

## Edit in the netlist box (#245)

`LiveRunTimingTests` changes one value in the Transistor fuzz starter (`R4 n6 0 1k` to `2.2k`) in the netlist box of the published site, and records the gaps between animation frames until the live result shows. A gap is a time when the page did not answer.
The edit gap is the longest gap that starts less than 200 ms after the edit: the render of the edit, and the schematic redraw. The solve gap is the longest gap after that: the live solve and the render of its result.
Run it with `scripts/playwright.sh`. The test log shows the times at detailed verbosity, and the test fails when the edit gap is 100 ms or more.

| Item | Value |
|---|---|
| Machine | Apple M3 Pro, macOS |
| Browser | Chromium, headless (Playwright) |
| Build | Release, not AOT |
| Date | 2026-10-04 |

| Build | Edit gap | Solve gap | Edit to result |
|---|---|---|---|
| Before (master at 5af83451) | 597, 631 ms | 92, 96 ms | 691, 722 ms |
| After | 60, 69, 66, 57 ms | 107, 102, 99, 97 ms | 402, 402, 385, 384 ms |

Where the edit gap went before, from Stopwatch timers in the edit render and a Chromium trace of the same edit:

| Step | Time |
|---|---|
| Render of the editor page for the edit (all of it is .NET code; the browser layout is 2 ms, the style recalculation under 1 ms) | about 600 ms |
| `AutoPlacer.Place` in the schematic editor (the text edit dropped the layout) | about 240 ms |
| `AutoPlacer.Place` again, in the Download SVG button: `SchematicExport` made the SVG on each render | about 270 ms |
| Three `NetlistLoader.Load` calls (schematic, calculators, compare) | about 60 ms |
| The render of the panels after the schematic | about 46 ms |

After the change:

- The netlist box redraws the schematic 100 ms after the last key. The schematic, the calculators, the compare panel and the Download SVG button share one load of each netlist.
- A text edit that keeps the parts and their nodes (a value, a comment) keeps the layout and does not call the placer. A text edit that adds a part keeps the places of the other parts, and places only the new part.
- The Download SVG button makes the SVG when it is clicked.
- A live solve on the main thread and the render of its result are two browser tasks.

The solve gap is the live solve on the main thread: 90 to 94 ms in .NET for this circuit. It is not in the limit. Moving it to the worker did not pay (#244).
