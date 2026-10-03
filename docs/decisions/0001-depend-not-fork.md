# 0001: Depend on the engine. Do not fork it.

## Status

Accepted.

## Context

This repository started as a fork of SpiceSharp. The fork had no changes from upstream.
Upstream is active. All pedal features can live outside the engine.

## Decision

Use the engine as NuGet packages: SpiceSharp 3.2.3, SpiceSharp-Parser 3.4.1, SpiceSharpBehavioral 3.2.0, SpiceSharpParser.CustomComponents 0.1.2.
Remove the engine source from this repository.

## Consequences

- Engine fixes come from upstream releases. Dependabot proposes the updates.
- An engine change that ssp needs goes to upstream as a pull request.
- Only `Ssp.Core` references the engine packages.
