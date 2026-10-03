# Plan: issue #6, WAV read and write

1. Write tests/Ssp.Core.Tests/WavTests.cs (round-trip 16/24-bit, mono/stereo, 8-bit and float rejection). Run it. It fails (no code). Commit.
2. Add src/Ssp.Core/Audio/WavData.cs: sample rate and per-channel double samples in [-1, 1).
3. Add src/Ssp.Core/Audio/Wav.cs: Read(Stream), Write(Stream, WavData, int bitDepth). No package.
4. Run `dotnet test -c Release`.
5. Set the "WAV read and write" row in docs/features.md to done.
6. Push issue-6, open the PR against master.

Out of scope: sample rate conversion, render, CLI commands.
Done: WavTests pass, no change to Directory.Packages.props.
