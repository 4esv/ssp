# Fixtures

This folder contains netlists for tests.

## Rules

- Name each file `<topic>-<variant>.cir`. Example: `rc-lowpass.cir`, `bjt-ce-bias.cir`.
- Put a `* ssp:title` line in each file.
- If a test checks a hand calculation, write the calculation in comment lines at the top of the file.
- Use only models from `models/` or models that you define in the file.

Tests read fixtures with `Fixtures.Read("<file name>")`.
