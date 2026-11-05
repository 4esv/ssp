# ssp

ssp designs and simulates guitar pedal circuits.
It has a command line tool and a static web app. Both use the
[SpiceSharp](https://github.com/SpiceSharp/SpiceSharp) engine.
The web app is at <https://ssp.aesv.io>.

The CLI and the file formats are stable from version 1.0.0. The web app is not released.
See [CHANGELOG.md](CHANGELOG.md) and decision record [0008](docs/decisions/0008-versioning.md).

## Install

You need the .NET 10 SDK.

```sh
git clone https://github.com/4esv/ssp.git
cd ssp
dotnet pack src/Ssp.Cli -c Release -o artifacts
dotnet tool install --global ssp --add-source artifacts
ssp --version
```

Run one circuit:

```sh
ssp run circuits/fixtures/rc-lowpass.cir
```

The run prints the DC voltages, the frequency response and the impedances.

The package holds the tool only. The models and the sample audio are in the repository.
If .NET comes from Homebrew, see the install note in the [CLI reference](docs/cli.md#install).

## Documentation

- [docs/index.md](docs/index.md): a five-minute tour.
- [docs/cli.md](docs/cli.md): every command and option.
- [docs/web.md](docs/web.md): the web app.
- [docs/file-format.md](docs/file-format.md): netlists, directives and layout files.
- [models/README.md](models/README.md): the device models.
- [docs/README.md](docs/README.md): index of all documents.
- [CONTRIBUTING.md](CONTRIBUTING.md): how to contribute.
- [AGENTS.md](AGENTS.md): commands and rules for coding agents.

## License

MIT. See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
