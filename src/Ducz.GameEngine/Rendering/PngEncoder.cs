using System.Buffers.Binary;
using System.IO.Compression;

namespace Ducz.Rendering;

/// <summary>
/// Minimal PNG writer (RGBA8, non-interlaced) so procedural textures such as
/// checkerboards can be embedded in exported files without extra dependencies.
/// </summary>
public static class PngEncoder
{
    /// <summary>Encodes raw RGBA8 pixels (row-major, top-left first) as a PNG file.</summary>
    public static byte[] Encode(int width, int height, byte[] rgbaPixels)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Image size must be positive.");
        if (rgbaPixels.Length < width * height * 4)
            throw new ArgumentException("Pixel buffer too small for the given size.");

        // Filtered scanlines: filter byte 0 (None) + raw row.
        var raw = new byte[height * (width * 4 + 1)];
        for (int y = 0; y < height; y++)
        {
            int dst = y * (width * 4 + 1);
            raw[dst] = 0;
            Buffer.BlockCopy(rgbaPixels, y * width * 4, raw, dst + 1, width * 4);
        }

        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(raw, 0, raw.Length);
            compressed = output.ToArray();
        }

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // color type: RGBA
        ihdr[10] = 0;  // compression
        ihdr[11] = 0;  // filter
        ihdr[12] = 0;  // interlace
        WriteChunk(png, "IHDR", ihdr);
        WriteChunk(png, "IDAT", compressed);
        WriteChunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        uint crc = Crc32(typeBytes, 0xFFFFFFFF);
        crc = Crc32(data, crc) ^ 0xFFFFFFFF;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] data, uint crc)
    {
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
