namespace Ssp.Core.Audio;

/// <summary>Decoded audio. Samples are in [-1, 1), one array per channel.</summary>
public sealed record WavData(int SampleRate, double[][] Channels);
