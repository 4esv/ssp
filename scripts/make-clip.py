#!/usr/bin/env python3
"""Writes src/Ssp.Web/Audio/clip.wav, a synthetic plucked-string guitar clip.

The clip is a Karplus-Strong string model. It plays the open E-minor chord notes in turn,
then the chord. It is not a recording. Output: 44.1 kHz, 16-bit PCM, mono, 1.0 s.
The script builds 2.0 s of music and keeps the first 1.0 s (#222).
The peak is 0.05 of full scale, a pickup level. A larger level makes the transient solver fail
in circuits/fixtures/clipper-bjt-si.cir. The script uses only the Python standard library and
gives the same bytes on each run.

Usage: scripts/make-clip.py [output.wav]
"""
import random
import struct
import sys
import wave

FS = 44_100
SECONDS = 2.0
CUT = 1.0
PEAK = 0.05
DECAY = 0.996
SMOOTH = 6
FADE_IN = 0.005
FADE_OUT = 0.01

# (start s, frequency Hz) of each pluck: E2, B2, E3, G3, B3, E4, then all as a strum.
NOTES = [(0.00, 82.41), (0.18, 123.47), (0.36, 164.81), (0.54, 196.00), (0.72, 246.94), (0.90, 329.63)]
STRUM = [(1.10 + 0.012 * i, f) for i, (_, f) in enumerate(NOTES)]


def pluck(freq, seconds, rng):
    period = round(FS / freq)
    ring = [rng.uniform(-1.0, 1.0) for _ in range(period)]
    # NOTE: Smoothed noise is a softer pick than raw noise. The circuit solver needs input without large steps.
    for _ in range(SMOOTH):
        ring = [0.5 * (ring[i] + ring[i - 1]) for i in range(period)]
    scale = max(abs(v) for v in ring)
    ring = [v / scale for v in ring]
    out = []
    for i in range(int(seconds * FS)):
        j = i % period
        out.append(ring[j])
        ring[j] = DECAY * 0.5 * (ring[j] + ring[(j + 1) % period])
    return out


def main(path):
    rng = random.Random(45)
    mix = [0.0] * int(SECONDS * FS)
    for start, freq in NOTES + STRUM:
        begin = int(start * FS)
        for i, v in enumerate(pluck(freq, SECONDS - start, rng)):
            mix[begin + i] += v
    mix = mix[: int(CUT * FS)]
    peak = max(abs(v) for v in mix)
    for i in range(int(FADE_IN * FS)):
        mix[i] *= i / (FADE_IN * FS)
    fade = int(FADE_OUT * FS)
    for i in range(fade):
        mix[-1 - i] *= i / fade
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(FS)
        w.writeframes(b"".join(struct.pack("<h", round(v / peak * PEAK * 32767)) for v in mix))


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "src/Ssp.Web/Audio/clip.wav")
