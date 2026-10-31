using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace FreePDF.Lab;

public sealed record Raster(int Width, int Height, byte[] Rgb);
public static class Pixels
{
    public static Raster ReadPpm(string path)
    {
        if (new FileInfo(path).Length > 80_000_000) throw new InvalidDataException("Raster exceeds 80 MB limit.");
        var bytes = File.ReadAllBytes(path); var p = 0;
        string Token()
        {
            while (p < bytes.Length)
            {
                if (bytes[p] == '#') { while (p < bytes.Length && bytes[p] != '\n') p++; }
                else if (bytes[p] <= 32) p++;
                else break;
            }
            var start = p;
            while (p < bytes.Length && bytes[p] > 32 && bytes[p] != '#') p++;
            if (p == start) throw new InvalidDataException("Truncated PPM header.");
            return Encoding.ASCII.GetString(bytes, start, p - start);
        }
        if (Token() != "P6" || !int.TryParse(Token(), out var width) || !int.TryParse(Token(), out var height) || Token() != "255")
            throw new InvalidDataException("Expected 8-bit RGB PPM.");
        if (width < 1 || height < 1 || (long)width * height > 25_000_000) throw new InvalidDataException("Invalid raster dimensions.");
        if (p >= bytes.Length || bytes[p] > 32) throw new InvalidDataException("Missing PPM separator.");
        var separator = bytes[p++];
        if (separator == '\r' && p < bytes.Length && bytes[p] == '\n') p++;
        var count = checked(width * height * 3);
        if (bytes.Length - p != count) throw new InvalidDataException("PPM pixel length mismatch.");
        return new Raster(width, height, bytes.AsSpan(p).ToArray());
    }

    public static (PixelMetrics Metrics, Raster Difference) Compare(Raster a, Raster b, int tolerance)
    {
        if (a.Width != b.Width || a.Height != b.Height)
            return (new PixelMetrics(b.Width, b.Height, false, 0, 0, 0, 0), b);
        var diff = new byte[b.Rgb.Length]; long changed = 0; double squares = 0; var max = 0;
        for (var i = 0; i < diff.Length; i += 3)
        {
            var delta = 0;
            for (var c = 0; c < 3; c++) { var d = Math.Abs(a.Rgb[i + c] - b.Rgb[i + c]); delta = Math.Max(delta, d); squares += d * d; max = Math.Max(max, d); }
            if (delta > tolerance) { changed++; diff[i] = 227; diff[i + 1] = 44; diff[i + 2] = 68; }
            else { var gray = (byte)(220 + (b.Rgb[i] + b.Rgb[i + 1] + b.Rgb[i + 2]) * 35 / 765); diff[i] = diff[i + 1] = diff[i + 2] = gray; }
        }
        return (new PixelMetrics(b.Width, b.Height, true, changed, changed * 100d / (b.Width * b.Height), Math.Sqrt(squares / b.Rgb.Length), max), new Raster(b.Width, b.Height, diff));
    }

    public static void WritePng(string path, Raster raster)
    {
        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, raster.Width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), raster.Height); header[8] = 8; header[9] = 2;
        Chunk(file, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, true))
            for (var y = 0; y < raster.Height; y++) { z.WriteByte(0); z.Write(raster.Rgb, y * raster.Width * 3, raster.Width * 3); }
        Chunk(file, "IDAT", compressed.ToArray()); Chunk(file, "IEND", []);
    }
    static void Chunk(Stream stream, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, data.Length); stream.Write(number);
        var name = Encoding.ASCII.GetBytes(type); stream.Write(name); stream.Write(data);
        uint crc = 0xffffffff;
        foreach (var b in name.Concat(data)) { crc ^= b; for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); stream.Write(number);
    }

    public static void SelfTest()
    {
        var a = new Raster(2, 1, [0, 10, 20, 255, 255, 255]);
        var b = new Raster(2, 1, [0, 18, 29, 255, 255, 255]);
        var (m, _) = Compare(a, b, 8);
        if (m.ChangedPixels != 1 || m.ChangedPercent != 50 || m.MaximumDelta != 9 || Math.Abs(m.RootMeanSquareError - Math.Sqrt(145d / 6)) > 1e-9) throw new Exception("Pixel metrics failed.");
        if (Compare(a, a, 0).Metrics.ChangedPixels != 0 || Compare(a, new Raster(1, 1, [0, 0, 0]), 0).Metrics.SameDimensions) throw new Exception("Dimensions/equality failed.");
        var folder = Path.Combine(Path.GetTempPath(), "freepdf-pixels-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var ppm = Path.Combine(folder, "test.ppm"); File.WriteAllBytes(ppm, Encoding.ASCII.GetBytes("P6\n# comment\n2 1\n255\n").Concat(a.Rgb).ToArray());
            if (!ReadPpm(ppm).Rgb.SequenceEqual(a.Rgb)) throw new Exception("PPM decoding failed.");
            File.WriteAllBytes(ppm, Encoding.ASCII.GetBytes("P6\n1 1\n255\n").Concat(new byte[] { 10, 32, 35 }).ToArray());
            if (!ReadPpm(ppm).Rgb.SequenceEqual(new byte[] { 10, 32, 35 })) throw new Exception("PPM binary whitespace failed.");
            File.WriteAllBytes(ppm, Encoding.ASCII.GetBytes("P6\n1 1\n255\n"));
            try { ReadPpm(ppm); throw new Exception("Truncated raster accepted."); } catch (InvalidDataException) { }
            var png = Path.Combine(folder, "test.png"); WritePng(png, a); var bytes = File.ReadAllBytes(png);
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(33, 4));
            using var zip = new ZLibStream(new MemoryStream(bytes, 41, length), CompressionMode.Decompress); using var decoded = new MemoryStream(); zip.CopyTo(decoded);
            if (!decoded.ToArray().SequenceEqual(new byte[] { 0 }.Concat(a.Rgb))) throw new Exception("PNG payload failed.");
        }
        finally { Directory.Delete(folder, true); }
        Console.WriteLine("Pixel comparison, dimensions, PPM parsing and PNG encoding checks passed.");
    }
}
