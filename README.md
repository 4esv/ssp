# ssp

ssp is a guitar pedal circuit simulator that runs in your browser and in your terminal.
You draw a pedal circuit, press Run, and see the frequency curve and the voltages.
Then you play a guitar through the circuit you drew, so you can hear your idea before you buy parts.

Both interfaces use the [SpiceSharp](https://github.com/SpiceSharp/SpiceSharp) engine.
Most features are not available yet. See [docs/features.md](docs/features.md) for the status of each one.

## Install

You need the .NET 10 SDK.

```sh
git clone https://github.com/4esv/ssp.git
cd ssp
dotnet pack src/Ssp.Cli -c Release -o artifacts
dotnet tool install --global ssp --add-source artifacts
ssp --version
```

## Use

Write a circuit as a SPICE netlist:

```spice
* ssp:title RC low-pass
* ssp:input in
* ssp:output out
V1 in 0 AC 1
R1 in out 10k
C1 out 0 10n
.end
```

Run the circuit:

```sh
ssp run lowpass.cir
```

The `run` command is not available yet. See [docs/features.md](docs/features.md) for the status of each feature.

## Documentation

- [docs/README.md](docs/README.md): index of all documents.
- [CONTRIBUTING.md](CONTRIBUTING.md): how to contribute.
- [AGENTS.md](AGENTS.md): commands and rules for coding agents.

## License

MIT. See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
