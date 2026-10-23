# ssp

ssp designs and simulates guitar pedal circuits.
It has two interfaces: a command line tool and a static web app.
Both interfaces use the [SpiceSharp](https://github.com/SpiceSharp/SpiceSharp) engine.

## Install

You need the .NET 10 SDK.

```sh
git clone https://github.com/4esv/ssp.git
cd ssp
dotnet pack src/Ssp.Cli -c Release -o artifacts
dotnet tool install --global ssp --add-source artifacts
ssp --version
```

If .NET comes from Homebrew, `ssp` can fail with "You must install or update .NET".
Set `DOTNET_ROOT` and run it again:

```sh
export DOTNET_ROOT="$(brew --prefix dotnet)/libexec"
```

## Five-minute tour

Run these commands from the root of the repository.

1. Run a circuit. `ssp run` prints the DC voltages, the small-signal (AC) frequency response, and the impedances.
   An AC analysis is linear, so it does not show clipping.

   ```sh
   ssp run circuits/fixtures/rc-lowpass.cir
   ```

2. Change a part with `--set`. Or run one part at several values with `sweep`.

   ```sh
   ssp run circuits/fixtures/rc-lowpass.cir --set R1=4k7
   ssp sweep circuits/fixtures/rc-lowpass.cir --set R1=1k,4k7,10k
   ```

3. Render audio through a circuit. `render` is a transient analysis. Use it to hear and measure clipping.

   ```sh
   ssp render circuits/library/fuzz-transistor-diode.cir --in src/Ssp.Web/Audio/riff.wav --out fuzz.wav
   ```

4. Open the web app at <https://ssp.aesv.io>. Draw a circuit, press Run, and play a clip through it.

## Documentation

- [docs/cli.md](docs/cli.md): every command and option.
- [docs/web.md](docs/web.md): the web app.
- [docs/README.md](docs/README.md): index of all documents.
- [CONTRIBUTING.md](CONTRIBUTING.md): how to contribute.
- [AGENTS.md](AGENTS.md): commands and rules for coding agents.

The documents are also at <https://ssp.aesv.io/docs/>.

## License

MIT. See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
