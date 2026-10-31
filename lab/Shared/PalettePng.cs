// Original synthetic palette PNG generator. MIT; see LICENSE.
using System.Text;

namespace FreePDF.Lab.Fixtures;

static class PalettePng
{
    internal static byte Alpha(int depth, int pixel) => (pixel % (depth == 1 ? 2 : 4)) switch {0 => 0, 1 when depth != 1 => 128, _ => 255};
    internal static byte[] Create(int depth, int width, int height, bool transparent)
    {
        using var png = new MemoryStream();
        png.Write(new byte[] {137,80,78,71,13,10,26,10}, 0, 8);
        byte[] Big(uint n) => new[] {(byte)(n >> 24),(byte)(n >> 16),(byte)(n >> 8),(byte)n};
        void Chunk(string name, byte[] data)
        {
            var size = Big((uint)data.Length); png.Write(size, 0, size.Length);
            var bytes = Encoding.ASCII.GetBytes(name).Concat(data).ToArray(); png.Write(bytes, 0, bytes.Length);
            uint crc = 0xffffffff;
            foreach (var b in bytes) { crc ^= b; for (var i=0;i<8;i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1; }
            var checksum = Big(~crc); png.Write(checksum, 0, 4);
        }
        Chunk("IHDR", Big((uint)width).Concat(Big((uint)height)).Concat(new byte[] {(byte)depth,3,0,0,0}).ToArray());
        Chunk("PLTE", depth == 1 ? new byte[] {255,0,0,0,0,255} : new byte[] {255,0,0,0,255,0,0,0,255,255,220,0});
        if (transparent) Chunk("tRNS", depth == 1 ? new byte[] {0} : new byte[] {0,128,255});
        var stride = (width * depth + 7) / 8;
        var raw = new byte[(stride + 1) * height];
        for (var y=0;y<height;y++) for (var x=0;x<width;x++)
            raw[y*(stride+1)+1+x*depth/8] |= (byte)(((y*width+x) % (depth == 1 ? 2 : 4)) << (8-depth-x*depth%8));
        // A small RFC 1950 stream with one uncompressed DEFLATE block.
        var len = raw.Length;
        var compressed = new List<byte> {0x78,0x01,0x01,(byte)len,(byte)(len>>8),(byte)~len,(byte)(~len>>8)};
        compressed.AddRange(raw); uint a=1,bSum=0;
        foreach (var b in raw) { a=(a+b)%65521; bSum=(bSum+a)%65521; }
        compressed.AddRange(Big((bSum<<16)|a));
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", new byte[0]);
        return png.ToArray();
    }
}
