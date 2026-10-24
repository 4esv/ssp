# Changelog

This file records the notable changes to this project.

## Rule

- Write one section for each release.
- Write the section in the release pull request.
- Follow [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), version 1.1.0.
- Follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html), version 2.0.0.
- Decision record [0008](docs/decisions/0008-versioning.md) fixes this rule.

## Release checklist

The owner does these five steps for each release.

1. Merge the release pull request. It bumps `Version` in `Directory.Build.props` and adds the section for this release.
2. Run `dotnet pack src/Ssp.Cli -c Release -o artifacts`.
3. Tag the merge commit `v<version>`. Create a GitHub release, and attach the tool package to it.
4. Run `scripts/check-release.sh <tag>`. The script proves that the tag, the package version and `ssp --version` agree. Run `dotnet test -c Release`. It must pass, including the schema lock.
5. Open the live site. The footer must show `ssp <version>`.

## [Unreleased]

No changes yet.

## [1.0.0] - 2026-10-24

The first release. The 1.0 surface is the `ssp` command and the engine: the netlist format, the layout file, the `ssp` directives, the result JSON schema 1.1.0 and the four commands. Decision record [0008](docs/decisions/0008-versioning.md) gives this rule. The web app is not part of the release. It deploys from `main` and can change at any time.

### Added

- `ssp run` gives the operating point, the frequency response and the input and output impedance of a SPICE netlist. It writes a plain-English report, or JSON with `--json`.
- `ssp sweep` runs a netlist once for each value of a part.
- `ssp render` sends a WAV file through the circuit. `--ir` convolves the output with an impulse response, for example a cabinet.
- `ssp mcp` gives the run, render, sweep and explain tools to an MCP client.
- `--set` changes a part value without an edit to the netlist.
- Diagnostics find structural, bias and signal faults. Each diagnostic gives the netlist line.
- Models with provenance headers in `models/`: the TL072, JRC4558 and LM308 op-amps, diodes, JFETs, an LED, a vactrol, a transformer and a cabinet impulse response.
- The public circuit library in `circuits/`.
- The web app at https://ssp.aesv.io: a schematic editor, live results, plots, knobs, compare, clip playback, a virtual amp and share by URL.
- The documents at https://ssp.aesv.io/docs/index.html.

### Fixed

- A netlist parse error gives plain words: text after `.END`, a `.subckt` with no `.ends`, and other syntax errors. The diagnostic gives the line once.
- The web app works on a phone, in portrait and in landscape: the help sheet does not cover the editor, every toolbar control is on the screen, tap targets are 44 px or more, an open panel leaves the circuit in view, and a netlist error has a button that opens the Netlist panel.
