using System.Buffers.Binary;
using System.Buffers.Text;
using System.IO.Compression;
using System.Text;

namespace Ssp.Web.Sharing;

/// <summary>Puts a netlist in a URL hash and gets it back: UTF-8, deflate in a zlib wrapper, base64url.</summary>
public static class ShareCodec
{
    static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Encode(string netlist)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize))
        {
            zlib.Write(Utf8.GetBytes(netlist));
        }
        return Base64Url.EncodeToString(output.ToArray());
    }

    /// <exception cref="FormatException">The hash is not a netlist from <see cref="Encode"/>.</exception>
    public static string Decode(string hash)
    {
        try
        {
            var bytes = Base64Url.DecodeFromChars(hash);
            using var zlib = new ZLibStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            var text = output.ToArray();
            // NOTE: A cut deflate stream decodes without an error. The Adler-32 trailer finds the cut.
            if (bytes.Length < 6 || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(^4)) != Adler32(text))
            {
                throw new InvalidDataException("The checksum does not match.");
            }
            return Utf8.GetString(text);
        }
        catch (Exception e) when (e is FormatException or InvalidDataException or DecoderFallbackException)
        {
            throw new FormatException("The share link is damaged. It does not contain a netlist.", e);
        }
    }

    static uint Adler32(ReadOnlySpan<byte> data)
    {
        const uint Mod = 65521;
        uint a = 1, b = 0;
        foreach (var x in data)
        {
            a = (a + x) % Mod;
            b = (b + a) % Mod;
        }
        return (b << 16) | a;
    }
}
