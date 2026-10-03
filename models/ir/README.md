# Cabinet impulse responses

## cab-1x12.wav

- provenance: synthetic. `scripts/make-cab-ir.py` writes this file from a chain of RBJ biquad filters. It is not a measurement of a real cabinet.
- format: 44.1 kHz, 24-bit PCM, mono, 2048 samples (46 ms). The peak of the magnitude response is 0 dB.
- sha256: `1f28517cbaed2817a32001b412229d3ddfd407d11e877278f984fe6fcf26c36e`
- author: 4esv
- license: CC0 1.0 Universal

### License

To the extent possible under law, the author has waived all copyright and related or neighboring rights to `cab-1x12.wav`. This work is published under the Creative Commons CC0 1.0 Universal Public Domain Dedication: <https://creativecommons.org/publicdomain/zero/1.0/>.

## Use

```sh
ssp render file.cir --in a.wav --out b.wav --ir models/ir/cab-1x12.wav
```

The IR sample rate must be the same as the input sample rate.
