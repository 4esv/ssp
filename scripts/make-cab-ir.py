#!/usr/bin/env python3
"""Writes models/ir/cab-1x12.wav, a synthetic 1x12 guitar cabinet impulse response.

The IR is an impulse passed through a fixed chain of RBJ biquad filters. It is not a
measurement of a real cabinet. Output: 44.1 kHz, 24-bit PCM, mono, 2048 samples,
peak magnitude response 0 dB. The script uses only the Python standard library and
gives the same bytes on each run.

Usage: scripts/make-cab-ir.py [output.wav]
"""
import cmath
import math
import struct
import sys
import wave

FS = 44_100
LENGTH = 2048
FADE = 256

# (type, frequency Hz, Q, gain dB)
CHAIN = [
    ("highpass", 75.0, 0.7, 0.0),   # closed-back low cut
    ("peak", 110.0, 1.4, 4.0),      # cone and box resonance
    ("peak", 450.0, 1.0, -3.0),     # low-mid scoop
    ("peak", 2500.0, 1.2, 5.0),     # cone presence peak
    ("lowpass", 4800.0, 0.8, 0.0),  # cone break-up roll-off
    ("lowpass", 6500.0, 0.6, 0.0),  # second pole pair for a steep top end
]


def biquad(kind, f0, q, gain_db):
    """RBJ audio EQ cookbook coefficients, normalized so that a0 = 1."""
    w0 = 2 * math.pi * f0 / FS
    cw, sw = math.cos(w0), math.sin(w0)
    alpha = sw / (2 * q)
    if kind == "highpass":
        b = [(1 + cw) / 2, -(1 + cw), (1 + cw) / 2]
        a = [1 + alpha, -2 * cw, 1 - alpha]
    elif kind == "lowpass":
        b = [(1 - cw) / 2, 1 - cw, (1 - cw) / 2]
        a = [1 + alpha, -2 * cw, 1 - alpha]
    elif kind == "peak":
        amp = 10 ** (gain_db / 40)
        b = [1 + alpha * amp, -2 * cw, 1 - alpha * amp]
        a = [1 + alpha / amp, -2 * cw, 1 - alpha / amp]
    else:
        raise ValueError(kind)
    return [x / a[0] for x in b], [x / a[0] for x in a]


def run(b, a, x):
    y = []
    x1 = x2 = y1 = y2 = 0.0
    for x0 in x:
        y0 = b[0] * x0 + b[1] * x1 + b[2] * x2 - a[1] * y1 - a[2] * y2
        y.append(y0)
        x2, x1, y2, y1 = x1, x0, y1, y0
    return y


def peak_gain(h):
    """Largest magnitude of the frequency response on a log grid from 20 Hz to 20 kHz."""
    best = 0.0
    for i in range(400):
        f = 20 * (1000 ** (i / 399))
        w = -2j * math.pi * f / FS
        best = max(best, abs(sum(v * cmath.exp(w * n) for n, v in enumerate(h))))
    return best


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "models/ir/cab-1x12.wav"
    h = [1.0] + [0.0] * (LENGTH - 1)
    for stage in CHAIN:
        h = run(*biquad(*stage), h)
    for n in range(FADE):
        h[LENGTH - FADE + n] *= 0.5 * (1 + math.cos(math.pi * (n + 1) / FADE))
    g = peak_gain(h)
    h = [v / g for v in h]
    if max(abs(v) for v in h) >= 1:
        raise SystemExit("IR peak sample is 1 or more; it does not fit 24-bit PCM.")

    scale = 2 ** 23
    frames = b"".join(
        struct.pack("<i", max(-scale, min(scale - 1, round(v * scale))))[:3] for v in h
    )
    with wave.open(out, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(3)
        w.setframerate(FS)
        w.writeframes(frames)
    print(f"{out}: {LENGTH} samples, {FS} Hz, peak sample {max(abs(v) for v in h):.4f}, gain scale {1 / g:.6f}")


if __name__ == "__main__":
    main()
