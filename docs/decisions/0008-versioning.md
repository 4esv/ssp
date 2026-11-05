# 0008: Versioning and releases

## Status

Accepted.

## Context

ssp has two surfaces. The CLI is a dotnet global tool named `ssp`. The web app deploys from `main` on each
push and is not released.

- One `Version` property in `Directory.Build.props` covers every project.
- `ssp --version` prints the tool version, then one line for each engine package.
- The run-result JSON has its own schema version. A test holds one hash for each released schema text.
- The netlist and the layout file are text contracts. [0003](0003-netlist-source-of-truth.md) makes the netlist the source of truth.
- The changelog follows Keep a Changelog and Semantic Versioning from the first release.

## Decision

**1.0 is the day the text contracts freeze:** the netlist, the layout file, the CLI and the run-result
schema. The editor is not part of the bar
([0006](0006-working-format.md)).

- **One version covers the source tree.** The tag and the CLI package carry it. The web app prints the
  version of the deployed commit.
- **Semantic Versioning.** A break in any contract raises the major version. A schema major raises the tool
  major in the same release. A corrected simulation value is a fix, not a break, and the release notes name it.
- **One changelog section for each release**, written in the release pull request.
- **Releases are manual**: bump `Version`, add the changelog section, run
  `dotnet pack src/Ssp.Cli -c Release -o artifacts`, tag the merge commit `v<version>`, publish the GitHub
  release with the package, then run `scripts/check-release.sh <tag>`.

Before 1.0 the version carries no compatibility promise.

## Consequences

- The tag names the CLI release only. The web app is not tagged.
- The schema version moves on its own track, because the hash test covers the whole schema text.
- `ssp --version`, the footer of the site and the MCP server show one version number.
- A break in the netlist format, the layout format, the CLI or the JSON raises the major version of the tool.
