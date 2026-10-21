using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Tankstat.Seeder.Pictures;

/// <summary>
/// A made-up picture of a car, drawn without any image library: a sky, a road and a car in a colour of its own, as a PNG. Deterministic for a
/// seed, so the same seed draws the same pictures; what a vehicle gets when no photo of its model can be found, or with <c>--drawn-pictures</c>.
/// </summary>
public static class DrawnPicture
{
    public const int Width = 640, Height = 400;

    private static readonly (byte R, byte G, byte B)[] Paints =
    [
        (200, 40, 40), (30, 80, 170), (240, 240, 235), (25, 25, 30), (150, 155, 160), (20, 110, 70), (230, 150, 30), (110, 40, 120),
        (90, 60, 40), (40, 150, 190),
    ];

    public static byte[] Png(Random random)
    {
        var paint = Paints[random.Next(Paints.Length)];
        var sky = (R: (byte)random.Next(120, 180), G: (byte)random.Next(170, 210), B: (byte)random.Next(220, 250));
        var pixels = new byte[Width * Height * 3];

        void Set(int x, int y, (byte R, byte G, byte B) c)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            var i = (y * Width + x) * 3;
            (pixels[i], pixels[i + 1], pixels[i + 2]) = c;
        }

        // The sky, lighter towards the horizon, and the road below it.
        for (var y = 0; y < Height; y++)
        {
            var road = y > Height * 0.72;
            var t = y / (double)Height;
            var c = road
                ? ((byte)(70 - 20 * t), (byte)(72 - 20 * t), (byte)(78 - 20 * t))
                : ((byte)Math.Min(255, sky.R + 70 * t), (byte)Math.Min(255, sky.G + 45 * t), (byte)Math.Min(255, sky.B + 5 * t));
            for (var x = 0; x < Width; x++) Set(x, y, c);
        }

        // The body, its corners rounded, then the cabin narrowing towards the roof, with its windows.
        int left = (int)(Width * 0.16), right = (int)(Width * 0.84), top = (int)(Height * 0.48), bottom = (int)(Height * 0.68), corner = 18;
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var cx = Math.Clamp(x, left + corner, right - corner);
                var cy = Math.Clamp(y, top + corner, bottom - corner);
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= corner * corner) Set(x, y, paint);
            }
        var roof = (int)(Height * 0.32);
        var glass = ((byte)170, (byte)200, (byte)220);
        for (var y = roof; y < top; y++)
        {
            var share = (y - roof) / (double)(top - roof); // 0 at the roof, 1 where the cabin meets the body
            var from = (int)(Width * (0.40 - 0.10 * share));
            var to = (int)(Width * (0.64 + 0.08 * share));
            for (var x = from; x < to; x++)
            {
                var window = y > roof + 8 && x > from + 10 && x < to - 10 && Math.Abs(x - (from + to) / 2) > 4;
                Set(x, y, window ? glass : paint);
            }
        }

        // The wheels: a tyre and a hub each.
        var radius = (int)(Height * 0.09);
        foreach (var centre in new[] { (int)(Width * 0.31), (int)(Width * 0.69) })
        {
            var cy = bottom;
            for (var y = cy - radius; y <= cy + radius; y++)
                for (var x = centre - radius; x <= centre + radius; x++)
                {
                    var d = (x - centre) * (x - centre) + (y - cy) * (y - cy);
                    if (d <= radius * radius) Set(x, y, d <= radius * radius / 5 ? ((byte)170, (byte)170, (byte)175) : ((byte)20, (byte)20, (byte)22));
                }
        }

        return Encode(pixels);
    }

    /// <summary>The pixels (RGB, 8 bits each, row by row) as a PNG: one IDAT, no filtering, zlib-compressed.</summary>
    private static byte[] Encode(byte[] pixels)
    {
        var raw = new byte[(Width * 3 + 1) * Height];
        for (var y = 0; y < Height; y++) Buffer.BlockCopy(pixels, y * Width * 3, raw, y * (Width * 3 + 1) + 1, Width * 3); // filter byte 0: none

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(raw);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Height);
        header[8] = 8; // bits per sample
        header[9] = 2; // truecolour (RGB)

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        png.Write(typeBytes);
        png.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32([.. typeBytes, .. data]));
        png.Write(number);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
