# Bundled audio

## clip.wav

- provenance: synthetic. `scripts/make-clip.py` writes this file from a Karplus-Strong string model. It plays the open E-minor chord notes in turn, then the chord. It is not a recording of a real guitar.
- format: 44.1 kHz, 16-bit PCM, mono, 2.0 s, peak 0.05 of full scale (-26 dBFS), a pickup level. A larger level makes the transient solver fail in the clipper fixture.
- sha256: `1babe35a646dcc417a2f6b79a55be25543778b70d10f6c5206148e4e78e1c012`
- author: 4esv
- license: CC0 1.0 Universal

### License

To the extent possible under law, the author has waived all copyright and related or neighboring rights to `clip.wav`. This work is published under the Creative Commons CC0 1.0 Universal Public Domain Dedication: <https://creativecommons.org/publicdomain/zero/1.0/>.

## Use

The clip player in the editor (`src/Ssp.Web/Components/ClipPlayer.razor`) reads this file from the assembly.
