using System.Buffers.Binary;
using System.Text;

namespace Ssp.Core.Audio;

/// <summary>Reads and writes PCM WAV files. 16-bit and 24-bit, mono and stereo.</summary>
public static class Wav
{
    const int PcmTag = 1;
    const int FloatTag = 3;
    const int ExtensibleTag = 0xFFFE;

    public static WavData Read(Stream stream)
    {
        var br = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (ReadId(br) != "RIFF") throw new InvalidDataException("Not a WAV file: missing RIFF header.");
        br.ReadUInt32();
        if (ReadId(br) != "WAVE") throw new InvalidDataException("Not a WAV file: missing WAVE id.");

        int tag = 0, channels = 0, rate = 0, bits = 0;
        var haveFormat = false;
        while (true)
        {
            var id = ReadId(br);
            var size = br.ReadUInt32();
            if (id == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("WAV fmt chunk is too short.");
                tag = br.ReadUInt16();
                channels = br.ReadUInt16();
                rate = br.ReadInt32();
                br.ReadInt32();
                br.ReadUInt16();
                bits = br.ReadUInt16();
                var rest = (long)size - 16;
                if (tag == ExtensibleTag && rest >= 10)
                {
                    br.ReadBytes(8); // cbSize, valid bits, channel mask
                    tag = br.ReadUInt16();
                    rest -= 10;
                }
                Skip(br, rest + (size & 1));
                haveFormat = true;
            }
            else if (id == "data")
            {
                if (!haveFormat) throw new InvalidDataException("WAV data chunk comes before the fmt chunk.");
                CheckSupported(tag, bits, channels);
                return Decode(br.ReadBytes((int)Math.Min(size, int.MaxValue)), rate, channels, bits / 8);
            }
            else
            {
                Skip(br, (long)size + (size & 1));
            }
        }
    }

    public static void Write(Stream stream, WavData data, int bitDepth)
    {
        if (bitDepth != 16 && bitDepth != 24)
            throw new NotSupportedException($"Unsupported WAV format: {bitDepth}-bit PCM. Use 16 or 24.");
        var channels = data.Channels.Length;
        if (channels is not (1 or 2))
            throw new NotSupportedException($"Unsupported WAV format: {channels} channels. Use 1 or 2.");
        var frames = data.Channels[0].Length;
        if (data.Channels.Any(c => c.Length != frames))
            throw new ArgumentException("All channels must have the same length.", nameof(data));

        var bytes = bitDepth / 8;
        var blockAlign = channels * bytes;
        var dataSize = checked(frames * blockAlign);
        var buf = new byte[44 + dataSize];
        var s = buf.AsSpan();
        "RIFF"u8.CopyTo(s);
        BinaryPrimitives.WriteInt32LittleEndian(s[4..], 36 + dataSize);
        "WAVEfmt "u8.CopyTo(s[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(s[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(s[20..], PcmTag);
        BinaryPrimitives.WriteInt16LittleEndian(s[22..], (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(s[24..], data.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(s[28..], data.SampleRate * blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(s[32..], (short)blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(s[34..], (short)bitDepth);
        "data"u8.CopyTo(s[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(s[40..], dataSize);

        var scale = Math.Pow(2, bitDepth - 1);
        var max = scale - 1;
        var pos = 44;
        for (var i = 0; i < frames; i++)
        {
            for (var c = 0; c < channels; c++)
            {
                var v = (int)Math.Clamp(Math.Round(data.Channels[c][i] * scale), -scale, max);
                for (var b = 0; b < bytes; b++) buf[pos++] = (byte)(v >> (8 * b));
            }
        }
        stream.Write(buf);
    }

    static void CheckSupported(int tag, int bits, int channels)
    {
        if (tag == FloatTag) throw new NotSupportedException($"Unsupported WAV format: IEEE float, {bits}-bit.");
        if (tag != PcmTag) throw new NotSupportedException($"Unsupported WAV format: format tag {tag}.");
        if (bits is not (16 or 24)) throw new NotSupportedException($"Unsupported WAV format: {bits}-bit PCM. Use 16 or 24.");
        if (channels is not (1 or 2)) throw new NotSupportedException($"Unsupported WAV format: {channels} channels. Use 1 or 2.");
    }

    static WavData Decode(byte[] raw, int rate, int channels, int bytes)
    {
        var frames = raw.Length / (channels * bytes);
        var data = new double[channels][];
        for (var c = 0; c < channels; c++) data[c] = new double[frames];
        var scale = Math.Pow(2, bytes * 8 - 1);
        var pos = 0;
        for (var i = 0; i < frames; i++)
        {
            for (var c = 0; c < channels; c++)
            {
                int v = bytes == 2
                    ? BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(pos))
                    : (raw[pos] | raw[pos + 1] << 8 | (sbyte)raw[pos + 2] << 16);
                data[c][i] = v / scale;
                pos += bytes;
            }
        }
        return new WavData(rate, data);
    }

    static string ReadId(BinaryReader br) => Encoding.ASCII.GetString(br.ReadBytes(4));

    static void Skip(BinaryReader br, long count)
    {
        if (count > 0) br.BaseStream.Seek(count, SeekOrigin.Current);
    }
}
