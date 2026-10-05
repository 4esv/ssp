#!/usr/bin/env python3
"""Writes the bundled samples in src/Ssp.Web/Audio, made from first principles (#276).

Plucked-string samples are Karplus-Strong. The test signals are exact. No third-party recording is used.
Output: 44.1 kHz, 16-bit PCM, mono. The string samples peak at 0.05 of full scale, a pickup level (see
make-clip.py). The sine is at -20 dBFS. The script uses only the Python standard library and gives the same
bytes on each run. clip.wav stays the work of scripts/make-clip.py.

Usage: scripts/make-samples.py [output-directory]
"""
import hashlib
import math
import random
import struct
import sys
import wave

FS = 44_100
PICKUP = 0.05
DECAY = 0.996
SMOOTH = 6


def pluck(freq, seconds, rng, decay=DECAY, bright=SMOOTH):
    period = round(FS / freq)
    ring = [rng.uniform(-1.0, 1.0) for _ in range(period)]
    # NOTE: Smoothed noise is a softer pick. The circuit solver needs input without large steps.
    for _ in range(bright):
        ring = [0.5 * (ring[i] + ring[i - 1]) for i in range(period)]
    scale = max(abs(v) for v in ring)
    ring = [v / scale for v in ring]
    out = []
    for i in range(int(seconds * FS)):
        j = i % period
        out.append(ring[j])
        ring[j] = decay * 0.5 * (ring[j] + ring[(j + 1) % period])
    return out


def mix_notes(notes, seconds, rng, **kw):
    mix = [0.0] * int(seconds * FS)
    for start, freq, length in notes:
        begin = int(start * FS)
        for i, v in enumerate(pluck(freq, min(length, seconds - start), rng, **kw)):
            if begin + i < len(mix):
                mix[begin + i] += v
    return mix


def strum(start, freqs, gap=0.012):
    return [(start + gap * i, f, 2.0) for i, f in enumerate(freqs)]


def pluck_note(rng):
    return mix_notes([(0.0, 164.81, 1.0)], 1.0, rng)


def chord(rng):
    return mix_notes(strum(0.0, [82.41, 123.47, 164.81, 196.00, 246.94, 329.63]), 1.0, rng)


def palm_muted_riff(rng):
    # NOTE: A palm mute is a fast decay. Eighth notes on E2, with a G2 and an A2 between.
    seq = [82.41, 82.41, 98.00, 82.41, 82.41, 110.00, 82.41, 98.00]
    notes = [(0.25 * i, f, 0.22) for i, f in enumerate(seq)]
    return mix_notes(notes, 2.0, rng, decay=0.97, bright=3)


def arpeggio(rng):
    # NOTE: A clean picked C major arpeggio, up and back down.
    seq = [130.81, 164.81, 196.00, 261.63, 329.63, 261.63, 196.00, 164.81]
    notes = [(0.25 * i, f, 1.0) for i, f in enumerate(seq)]
    return mix_notes(notes, 2.0, rng)


def bass_note(rng):
    return mix_notes([(0.0, 41.20, 1.0)], 1.0, rng, decay=0.9985, bright=10)


def sine(_):
    return [math.sin(2 * math.pi * 440 * i / FS) for i in range(FS)]


def sweep(_):
    # Log sweep 20 Hz to 20 kHz over 2 s.
    seconds, f0, f1 = 2.0, 20.0, 20_000.0
    k = math.log(f1 / f0)
    return [math.sin(2 * math.pi * f0 * seconds / k * (math.exp(k * i / (seconds * FS)) - 1)) for i in range(int(seconds * FS))]


def noise(rng):
    return [rng.uniform(-1.0, 1.0) for _ in range(FS)]


def click(_):
    # One unit sample at 0.1 s.
    out = [0.0] * FS
    out[FS // 10] = 1.0
    return out


# name: (file, shown name, builder, peak, fade)
SAMPLES = [
    ("pluck", "Plucked note", pluck_note, PICKUP, True),
    ("chord", "Chord", chord, PICKUP, True),
    ("riff", "Palm-muted riff", palm_muted_riff, PICKUP, True),
    ("arpeggio", "Clean arpeggio", arpeggio, PICKUP, True),
    ("bass", "Bass note", bass_note, PICKUP, True),
    ("sine", "Sine 440 Hz", sine, 0.1, False),
    ("sweep", "Sweep 20 Hz-20 kHz", sweep, PICKUP, False),
    ("noise", "White noise", noise, PICKUP, False),
    ("click", "Click", click, PICKUP, False),
]


def write(path, samples, peak, fade):
    top = max(abs(v) for v in samples)
    if fade:
        n = int(0.005 * FS)
        for i in range(n):
            samples[i] *= i / n
            samples[-1 - i] *= i / n
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(FS)
        w.writeframes(b"".join(struct.pack("<h", round(v / top * peak * 32767)) for v in samples))


def main(out):
    for name, _, build, peak, fade in SAMPLES:
        path = f"{out}/{name}.wav"
        samples = build(random.Random(276))
        write(path, samples, peak, fade)
        with open(path, "rb") as f:
            print(f"{name}.wav {len(samples) / FS:.1f} s sha256 {hashlib.sha256(f.read()).hexdigest()}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "src/Ssp.Web/Audio")
