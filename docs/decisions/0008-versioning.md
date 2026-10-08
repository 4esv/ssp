# 0008: Versioning and releases before 1.0

## Status

Proposed. This record fixes the versioning gap that `CHANGELOG.md:7` names.

The owner accepts or rejects this record in a comment on [#307](https://github.com/4esv/ssp/issues/307).

## Context

The repo has no release and no `ssp` tag.
Every project carries the version `0.1.0` (`Directory.Build.props:9`).
`CHANGELOG.md:5` records both facts.

The CLI packs as a dotnet global tool.
`PackAsTool` is true and the command name is `ssp` (`src/Ssp.Cli/Ssp.Cli.csproj:6-7`).
`README.md:14-16` gives the pack, install and check steps.
`ssp --version` prints `ssp <version>`, then one line for each engine package (`src/Ssp.Cli/Program.cs:295-305`, `src/Ssp.Core/EngineInfo.cs:16-28`).
`EngineInfo` removes the `+<commit>` build metadata from the version (`src/Ssp.Core/EngineInfo.cs:32-38`).
`tests/Ssp.Cli.Tests/VersionTests.cs:14-15` checks that text.

The web app deploys on each push to `master`.
`.github/workflows/web.yml:3-5` triggers the `deploy` job (`:16`).
The job publishes `src/Ssp.Web` (`:26`), copies `publish/wwwroot` to the static host (`:41-65`) and smoke-checks the live site (`:67-76`).
The web app prints the same version lines in its footer (`src/Ssp.Web/Pages/Home.razor:17-18`).
The MCP server reports the same first line (`src/Ssp.Cli/McpServerHost.cs:19`).
So three surfaces show one version number today.

The result JSON is a published contract with its own version.
`docs/schema/run-result.schema.json:4` sets the version to `1.1.0`.
The code holds the same value (`src/Ssp.Core/RunResult.cs:22`) and writes it as `schemaVersion` (`src/Ssp.Core/RunResultJson.cs:25`).
The schema rejects an unknown property (`docs/schema/run-result.schema.json:9`).
A test holds one SHA-256 hash for each released schema version (`tests/Ssp.Core.Tests/RunResultJsonTests.cs:14-18`).
The hash covers the whole schema text, so each change to the schema text needs a new version (`tests/Ssp.Core.Tests/RunResultJsonTests.cs:35-46`, `:110-113`).
The schema version therefore moves on its own track. It is not the product version.

The netlist is the source of truth (`docs/file-format.md:9`; [0003](0003-netlist-source-of-truth.md)).
The layout file is optional (`docs/file-format.md:10`).
The file format document is a draft (`docs/file-format.md:3`).

`CHANGELOG.md:3` says the changelog is not maintained.
`CHANGELOG.md:7` promises Keep a Changelog and Semantic Versioning from the first release.
It also says: "No decision record fixes the versioning scheme yet."
`CHANGELOG.md:9` says the commit log and the merged pull requests are the record until the first release.

No tag belongs to ssp. The 33 tags in the repo belong to the upstream engine.
For example, `v3.2.3` points to commit `34484fa6`, and the author of that commit is a SpiceSharp maintainer.
No tag name holds `ssp`.
(A git ref is not a worktree file. This fact comes from `git for-each-ref refs/tags` and `git show -s`.)

CI checks each pull request and each push to `master` (`.github/workflows/ci.yml:3-6`).
The steps are restore, build and test (`:26-30`), run each circuit in `circuits/library/` (`:32-40`) and publish the web app (`:42-49`).
CI does not pack the tool. Nothing in `.github/workflows/` runs `dotnet pack`.

`CONTRIBUTING.md:71-78` requires Simplified Technical English in documents and pull requests.
This record follows that rule.

## Question 1: What does 1.0 mean for ssp?

1.0 is the day the text contracts stop moving.
The bar is a list of checks. Each check names a file or a command.

The bar:

1. Every `core` row in `docs/features.md` is `done`.
   Today every `core` row is `done` (`docs/features.md:7-26`, `:44`, `:47-49`, `:51-54`, `:58-60`, `:62`, `:64`).
2. The four CLI commands exist and their tests pass: `run`, `sweep`, `render` and `mcp` (`src/Ssp.Cli/Program.cs:31-34`; `docs/features.md:27-30`).
3. The file format is frozen. `docs/file-format.md:3` no longer says `Status: draft`, and the compatibility rule of this record is in that document.
   At the time of writing, `circuits/` holds 47 netlists and 19 layouts. All of them load.
4. The result schema is at 1.x and locked. The schema version is `1.1.0` or later (`docs/schema/run-result.schema.json:4`), and the hash test passes (`tests/Ssp.Core.Tests/RunResultJsonTests.cs:110-113`).
5. The release check of question 5 passes. The tag version equals the packed version.

The editor is not part of the bar. The owner's standing directive puts UI/UX last ([0006](0006-working-format.md), Context).
The web app deploys on each push (`.github/workflows/web.yml:3-5`), so it does not need a tag.

### Options

| Option | What 1.0 means | Cost | Risk |
|---|---|---|---|
| A (recommended) | The CLI and the engine contracts freeze. The editor stays open. | Low. The list above is the work. | The tag says 1.0 while the editor is unfinished (`docs/features.md:36` in progress, `:66` planned). The release notes and the README name the CLI as the 1.0 surface. |
| B | The whole product, including the editor, is ready. | High. The editor needs the hierarchy work of [0006](0006-working-format.md) and the parts of `docs/features.md:66`. | The tag moves far out. The version carries no information until then. This is close to the "it feels ready" bar that the issue rejects. |
| C | No 1.0. Publish only the schema version. | Zero. | A user cannot tell whether the netlist parser and the CLI are stable. |

Recommendation: **option A**.

## Question 2: What does the version version?

One property holds the version for every project (`Directory.Build.props:9`).
So the assembly of `Ssp.Core`, the CLI package and the web app carry one number today.
The CLI is the packed artifact (`src/Ssp.Cli/Ssp.Cli.csproj:6-7`).
The web app deploys from `master` (`.github/workflows/web.yml:3-5`).

The relation between the two is: **the web app version is the source version of the deployed master commit**.
The web app is not tagged and not released.
The tag names the CLI release only.
The footer of the live site states the version of the code that is live (`src/Ssp.Web/Pages/Home.razor:17-18`).
A user compares that line with the tag list.

### Options

| Option | Rule | Cost | Risk |
|---|---|---|---|
| A (recommended) | One version for the source tree. The tag and the CLI package carry it. The web app prints it. | Low. No change to `Directory.Build.props:9`. | The live site prints a version that has no tag between two releases. The footer still names the source commit's version. |
| B | A separate version for the CLI and for the web app. | Medium. `Directory.Build.props:9` splits, and `Ssp.Core` needs a third answer. | The numbers skew. The two front ends share one engine and one source tree. |
| C | Version `Ssp.Core` alone. Both front ends float. | Medium. | The installed tool version does not follow the engine version. The user cannot tell what the tool contains. |

Recommendation: **option A**.

## Question 3: SemVer or something else?

Keep Semantic Versioning. `CHANGELOG.md:7` already promises it.

Before 1.0 the number carries no promise. Semantic Versioning says major version zero is for initial development, and anything may change.
`0.1.0` (`Directory.Build.props:9`) states that honestly.

"Breaking" has one meaning for each contract:

| Contract | A breaking change | An additive change | Evidence |
|---|---|---|---|
| The netlist (input) | A valid netlist now gives an error. ssp drops an element or reads a value differently. | A new directive, a new part or a new model. | `docs/file-format.md:9`, `docs/file-format.md:12-30` |
| The layout file (input) | A valid layout now gives an error. A position moves without a reason. | A new key with a default. | `docs/file-format.md:10`, `docs/file-format.md:44-59` |
| The command line | A command, an option or an exit code is removed or renamed. The meaning of an option changes. Text that a script reads changes form. | A new command, a new option or a new report line. | `src/Ssp.Cli/Program.cs:31-34`, `src/Ssp.Cli/Program.cs:40-51` |
| The run-result JSON (output) | A field is removed, renamed or retyped. | A new field. A field becomes optional. | `docs/schema/run-result.schema.json:8-12` |

Two rules tie the contracts together:

- A break in any contract raises the tool major version.
- A schema major raises the tool major in the same release.

A corrected simulation value is a fix, not a break. The release notes name it.

The netlist contract is not frozen yet. `docs/file-format.md:3` says `Status: draft`.
Until the freeze (stage 4), a 0.x change to the format is allowed. After the freeze, the table above applies.

The schema version is a separate track. It moves on each change to the schema text, because the hash test covers the whole text (`tests/Ssp.Core.Tests/RunResultJsonTests.cs:35-46`).
A reader pins to `schemaVersion` (`docs/schema/run-result.schema.json:12`).

## Question 4: How and when is the CHANGELOG maintained?

One section for each release, written at release time.
`CHANGELOG.md:7` already promises Keep a Changelog, and that document means one section for each release.
The author of the release pull request writes the section.

`CHANGELOG.md:3` says the changelog is not maintained. The first release replaces that sentence with the rule above.
Until the first release, `CHANGELOG.md:9` stays correct: the commit log and the merged pull requests are the record.

### Options

| Option | Rule | Cost | Risk |
|---|---|---|---|
| A (recommended) | One entry for each release, in the release pull request. | Low. One step in the release checklist. | A human can skip the step. The release check of question 5 fails when the section is absent. |
| B | One entry for each merged pull request. | Medium. Almost every merge conflicts on the file. A CI check is needed to enforce it. | Merge churn. The file stops being a release list. |
| C | No file. The git log and the GitHub release notes are the record. | Zero. | The repo contradicts `CHANGELOG.md:7`. A user has no ordered list of changes. |

Recommendation: **option A**.

## Question 5: What are the release mechanics?

The recommended release step, done by the owner:

1. Merge a release pull request. It bumps `Version` (`Directory.Build.props:9`) and adds the CHANGELOG section.
2. Run `dotnet pack src/Ssp.Cli -c Release -o artifacts` (`README.md:14`).
3. Tag the merge commit `v<version>` and create a GitHub release. Attach the tool package to the release.
4. Run the release check. It proves the release:
   - the tag equals `v` plus the `Version` value (`Directory.Build.props:9`);
   - the packed package version equals the tag;
   - `ssp --version` prints `ssp <version>` (`src/Ssp.Core/EngineInfo.cs:27-28`);
   - `dotnet test` passes, including the schema lock (`tests/Ssp.Core.Tests/RunResultJsonTests.cs:110-113`).
5. Check the live site. The footer shows `ssp <version>` (`src/Ssp.Web/Pages/Home.razor:17-18`).

The web app is not part of the release. The `web` workflow deploys it on the master push (`.github/workflows/web.yml:3-5`, `:63-65`) and smoke-checks it (`:67-76`).

### Options

| Option | Who does it | Cost | Risk |
|---|---|---|---|
| A (recommended for the first release) | The owner, by hand, with the check script. | Low. One script and one checklist. | A human step can be skipped. The check script reduces that risk. |
| B | A GitHub Actions job on a tag push or on `workflow_dispatch`. It packs, tests and creates the release. | Medium. A new workflow and a release token. | Workflow maintenance. The tag push and the `web` deploy can race. |
| C | Publish to NuGet.org as well. | High. An account, a secret and a permanent namespace. | An artifact that cannot be withdrawn. The repo has no NuGet setup today. Value: a plain `dotnet tool install --global ssp` (`README.md:15`). |

Recommendation: **option A** for the first release, **option B** for the release after it, **option C** after 1.0.

## Decision

Recommended policy:

- **1.0 is the day the text contracts freeze**: the netlist, the layout, the CLI and the run-result schema. The editor is not part of the bar.
- **One version covers the source tree.** The tag and the CLI package carry it. The web app deploys from `master` and prints the same number.
- **Semantic Versioning.** A break in any contract raises the major.
- **One CHANGELOG section for each release**, written in the release pull request.
- **The owner releases by hand**: pack, tag and GitHub release, checked by a script.

## Staged plan

Each stage is one issue. The repo works after each stage.

### Stage 1: the version check

Add a test. It reads `Version` (`Directory.Build.props:9`) and asserts that the value is valid Semantic Versioning and that `EngineInfo.ProductVersion` (`src/Ssp.Core/EngineInfo.cs:11`) reports it, with the build metadata removed (`src/Ssp.Core/EngineInfo.cs:32-38`).

Check: `dotnet test` passes. An invalid version fails the test.

### Stage 2: the release check script

Add `scripts/check-release.sh <tag>`.
It asserts that the tag is `v` plus the `Version` value (`Directory.Build.props:9`), that `dotnet pack` writes a package with that version (`README.md:14`) and that `ssp --version` prints `ssp <version>` (`src/Ssp.Core/EngineInfo.cs:27-28`).

Check: the script passes with the correct tag and fails with a wrong tag. The pull request shows both runs.

### Stage 3: the CHANGELOG rule

Put the per-release rule in `CHANGELOG.md` and replace the "not maintained" sentence (`CHANGELOG.md:3`). Add the release checklist.

Check: `CHANGELOG.md:3` no longer says the changelog is not maintained. The rule names this record.

### Stage 4: freeze the file format

Remove `Status: draft` (`docs/file-format.md:3`). Add the compatibility rule of question 3.

Check: `docs/file-format.md` has no `Status: draft` line. `dotnet test` passes, and every circuit in `circuits/library/` runs (`.github/workflows/ci.yml:32-40`).

### Stage 5: release 1.0.0

The bar of question 1 holds. Bump the version (`Directory.Build.props:9`), add the CHANGELOG section, tag the commit and publish the GitHub release with the tool package.

Check: the script of stage 2 passes on the tag. The GitHub release holds the tool package. The live site prints the matching version in the footer (`src/Ssp.Web/Pages/Home.razor:17-18`).

## Consequences

- The gap that `CHANGELOG.md:7` names is closed by this record.
- One number covers the source tree. The CLI packs it, the tag names it and the web app prints it.
- The schema version stays a separate track, enforced by the hash test (`tests/Ssp.Core.Tests/RunResultJsonTests.cs:110-113`).
- Before 1.0 the version carries no compatibility promise. The `draft` status of `docs/file-format.md:3` stays until stage 4.
- The editor stays out of the 1.0 bar. `docs/features.md:36` and `docs/features.md:66` can stay open.
- This record changes no product code, no workflow and no version number.
