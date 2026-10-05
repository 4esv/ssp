# Bundled audio

## clip.wav

- provenance: synthetic. `scripts/make-clip.py` writes this file from a Karplus-Strong string model. It plays the open E-minor chord notes in turn, then the chord. It is not a recording of a real guitar.
- format: 44.1 kHz, 16-bit PCM, mono, 1.0 s, peak 0.05 of full scale (-26 dBFS), a pickup level. A larger level makes the transient solver fail in the clipper fixture.
- sha256: `8b430c87cbdc9c08f11413b160a17522a0760496ecc1bc36d07a5b11191a9150`
- cut: `scripts/make-clip.py` builds 2.0 s of the model, keeps the first 1.0 s (the six single notes, to the end of the E4 ring, with no strum), and then scales the peak to 0.05. The last 10 ms fade to zero in a linear ramp, so the end does not click. The browser render time is proportional to the clip length, so the clip is 1 s (#222). The cut is at 1.0 s, 0.1 s after the last note starts; a note start is not at 1.0 s.
- author: 4esv
- license: CC0 1.0 Universal

### License

To the extent possible under law, the author has waived all copyright and related or neighboring rights to `clip.wav`. This work is published under the Creative Commons CC0 1.0 Universal Public Domain Dedication: <https://creativecommons.org/publicdomain/zero/1.0/>.

## Samples from scripts/make-samples.py (#276)

Each sample is synthetic. `scripts/make-samples.py` writes it from first principles. No third-party recording is used. Format: 44.1 kHz, 16-bit PCM, mono. The script gives the same bytes on each run. Author: 4esv. License: CC0 1.0 Universal (see the license text above).

### pluck.wav: Plucked note

- provenance: Karplus-Strong string, one note, E3 (164.81 Hz). 1.0 s.
- sha256: `32c569c669e353e7805765e1b088b03e1b93fedfbcee0961050f017a7f4ea52a`

### chord.wav: Chord

- provenance: Karplus-Strong, an open E minor chord strummed from E2 to E4, 12 ms between strings. 1.0 s.
- sha256: `2789367ff41e7de44ffb6773e452b782d2ec2d6b09d40823c7f7c2c98cdba2cd`

### riff.wav: Palm-muted riff

- provenance: Karplus-Strong with a fast decay (0.97), eight eighth notes on E2, G2 and A2 at 0.25 s steps. 2.0 s.
- sha256: `659d7a4fc1220a677931644df908a4b52946cde7bfe82e4f082db7835f907ba8`

### arpeggio.wav: Clean arpeggio

- provenance: Karplus-Strong, a C major arpeggio up and down, 0.25 s per note, notes ring. 2.0 s.
- sha256: `78ec49a0e4bf2d810d5051672cb4ed4913d3654f5090381f1567e906ce0f748a`

### bass.wav: Bass note

- provenance: Karplus-Strong, E1 (41.20 Hz), slow decay (0.9985), a softer pick. 1.0 s.
- sha256: `f91cc22801bd22c28bf7574e81ead9a1bef0e4067260c292f9d45dedde814ec4`

### sine.wav: Sine 440 Hz

- provenance: A computed sine, 440 Hz, peak 0.1 of full scale (-20 dBFS). 1.0 s.
- sha256: `11a6af9c2a7d7746579e9d4716f7ff224773253ab095dd23eff72c92407ccb4a`

### sweep.wav: Sweep 20 Hz-20 kHz

- provenance: A computed logarithmic sine sweep, 20 Hz to 20 kHz, peak 0.05. 2.0 s.
- sha256: `2ec15793975b7244625dc428de3666021615964722a6c5c958237ecee503f33d`

### noise.wav: White noise

- provenance: Uniform noise from a seeded generator, peak 0.05. 1.0 s.
- sha256: `4a37cf8ccbb78f9e953a99fafb3732f05a9d84adc744c36e96cac1b22148e97a`

### click.wav: Click

- provenance: One full-scale sample at 0.1 s, scaled to a peak of 0.05, then silence. 1.0 s.
- sha256: `e3bc0da85c25dd8b4518cb62c735d0c478b391c29edf1c46914727cbd8603090`

## Use

The clip player in the editor (`src/Ssp.Web/Components/ClipPlayer.razor`) reads these files from the assembly. `clip.wav` is the default sample, named "Bundled clip".
