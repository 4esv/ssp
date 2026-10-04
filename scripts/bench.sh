#!/usr/bin/env bash
# Measure the real-time factor of circuits/fixtures/clipper-bjt-si.cir and append one row to docs/benchmarks.md.
# Real-time factor = seconds of audio rendered / seconds of wall time of `ssp render`.
#
# Usage: scripts/bench.sh
# Env:   BENCH_SECONDS  length of the input audio in seconds (default 10)
# Needs: dotnet, python3.
#
# NOTE: the wall time includes the start of the process and the JIT. A longer BENCH_SECONDS makes that share smaller.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
seconds="${BENCH_SECONDS:-10}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

dotnet build "$root/src/Ssp.Cli" -c Release --nologo -v quiet >&2
dll="$(find "$root/src/Ssp.Cli/bin/Release" -name ssp.dll | head -1)"
[ -f "$dll" ] || { echo "ssp.dll not found after build" >&2; exit 1; }

runtime="$(dotnet --version | cut -d. -f1)"
platform="Native .NET $runtime, $(uname -s) $(uname -m)"

python3 - "$root" "$dll" "$seconds" "$tmp" "$platform" <<'PY'
import math, struct, subprocess, sys, time, wave

root, dll, seconds, tmp, platform = sys.argv[1:]
seconds = float(seconds)
rate = 44100
frames = round(seconds * rate)

# Input: sine, 0.3 V peak, 440 Hz, 16-bit.
with wave.open(f"{tmp}/in.wav", "wb") as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(rate)
    w.writeframes(b"".join(
        struct.pack("<h", round(0.3 * 32767 * math.sin(2 * math.pi * 440 * i / rate))) for i in range(frames)))

start = time.perf_counter()
subprocess.run(
    ["dotnet", dll, "render", f"{root}/circuits/fixtures/clipper-bjt-si.cir",
     "--in", f"{tmp}/in.wav", "--out", f"{tmp}/out.wav"],
    check=True)
wall = time.perf_counter() - start

factor = seconds / wall
print(f"Rendered {seconds:g} s of audio in {wall:.2f} s: real-time factor {factor:.1f}x")

# Same columns as the Results table: Platform | Method | Sample rate | Real-time factor.
row = f"| {platform} | Fixed trapezoidal | 44.1 kHz | {factor:.1f}x |"
path = f"{root}/docs/benchmarks.md"
lines = open(path).read().split("\n")
i = lines.index("## Results")
while not lines[i].startswith("|"):
    i += 1
while lines[i + 1].startswith("|"):
    i += 1
lines.insert(i + 1, row)
open(path, "w").write("\n".join(lines))
print(f"Appended to docs/benchmarks.md: {row}")
PY
