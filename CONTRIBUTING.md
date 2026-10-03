# Contributing

## Issues and pull requests

- Each pull request closes one issue.
- Start only an issue that is ready. Run `scripts/ready-issues.sh` to list ready issues.
- Use the task issue form. Fill in every field.

## Test first

1. Write the test from the "Test first" field of the issue.
2. Run the test. Make sure that it fails.
3. Commit the failing test.
4. Write the code that makes the test pass.
5. Put the link to the failing-test commit in the pull request.

A `kind:chore` issue has no "Test first" field. Show the acceptance evidence in the pull request.

## Commits

Use this format: `type(scope): summary`.

- Types: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `perf`.
- Scopes: `core`, `cli`, `web`, `models`, `docs`, `infra`.
- Write the summary in the imperative mood. Example: `feat(core): load a SPICE netlist`.

## Golden files

Some tests compare text to a golden file in `tests/Ssp.Core.Tests/Golden/`.
To update the golden files after an intended change:

1. Run `UPDATE_GOLDEN=1 dotnet test`.
2. Read the diff of each changed golden file.
3. Make sure that each change is intended.
4. Commit the golden files with the code change.

Do not edit a golden file by hand.

## Models

Write each model yourself from public datasheet values (clean-room rule).

- Do not copy model text from a manufacturer file.
- Do not copy model text from other simulation tools or their libraries.
- Put a provenance header at the top of each model file. See [models/README.md](models/README.md).

## Writing

Write documents, issues, and pull requests in ASD-STE100 Simplified Technical English.

- Keep each sentence under 20 words.
- Use the active voice.
- Write one instruction in each sentence.
- Use one term for one concept.
- Do not use marketing words.
