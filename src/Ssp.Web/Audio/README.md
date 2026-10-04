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

## Use

The clip player in the editor (`src/Ssp.Web/Components/ClipPlayer.razor`) reads this file from the assembly.
