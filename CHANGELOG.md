# Changelog

This file records the notable changes to this project.

## Rule

- Write one section for each release.
- Write the section in the release pull request.
- Follow [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), version 1.1.0.
- Follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html), version 2.0.0.
- Decision record [0008](docs/decisions/0008-versioning.md) fixes this rule.

Before the first release, the commit log and the merged pull requests record the changes.

## Release checklist

The owner does these five steps for each release.

1. Merge the release pull request. It bumps `Version` in `Directory.Build.props` and adds the section for this release.
2. Run `dotnet pack src/Ssp.Cli -c Release -o artifacts`.
3. Tag the merge commit `v<version>`. Create a GitHub release, and attach the tool package to it.
4. Run `scripts/check-release.sh <tag>`. The script proves that the tag, the package version and `ssp --version` agree. Run `dotnet test -c Release`. It must pass, including the schema lock.
5. Open the live site. The footer must show `ssp <version>`.

## [Unreleased]

No release yet.
