// Original malformed PNG fixture, no external image data. MIT; see LICENSE.
using PdfSharp.Drawing;
using Xunit;

namespace FreePDF.RegressionTests;

public class PngFailureTests
{
    [Fact]
    public void Impossible_chunk_allocation_preserves_out_of_memory_exception()
    {
        // A valid 1x1 grayscale IHDR, followed by an IDAT length exceeding the CLR's
        // maximum array length. The runtime rejects it without allocating 2 GiB.
        byte[] png = { 137,80,78,71,13,10,26,10, 0,0,0,13, 73,72,68,82,
            0,0,0,1, 0,0,0,1, 8,0,0,0,0, 58,126,155,85,
            127,255,255,255, 73,68,65,84 };
        Assert.Throws<OutOfMemoryException>(() => XImage.FromStream(new MemoryStream(png)));
    }

    [Fact]
    public void Invalid_image_still_reports_unsupported_format()
    {
        Assert.Throws<InvalidOperationException>(() => XImage.FromStream(new MemoryStream(new byte[32])));
    }
}
