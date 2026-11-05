# 0001: Depend on the engine. Do not fork it.

## Status

Accepted.

## Context

This repository started as an unmodified fork of SpiceSharp.
Upstream is active. A pedal circuit needs a circuit engine, not a changed one.

## Decision

Use the engine as NuGet packages: SpiceSharp 3.2.3, SpiceSharp-Parser 3.4.1,
SpiceSharpBehavioral 3.2.0 and SpiceSharpParser.CustomComponents 0.1.2.
Do not keep the engine source in this repository.

## Consequences

- An engine fix comes from an upstream release. Dependabot proposes the updates.
- An engine change that ssp needs goes to upstream as a pull request.
- Only `Ssp.Core` references the engine packages.
