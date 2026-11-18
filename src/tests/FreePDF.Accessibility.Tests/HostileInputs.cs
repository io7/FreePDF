// Original synthetic inputs. Each one once made the accessibility scan use far more memory than its size. MIT.
using System.IO.Compression;
using System.Text;

namespace FreePDF.Accessibility.Tests;

internal static class HostileInputs
{
    internal static readonly string[] Names = { "q-flood", "nested-xmp", "long-form-name", "type0-chain", "shared-alt", "form-fan-out" };

    internal static byte[] Create(string name) => name switch
    {
        // 10 MB of page content after decompression; 7.13.0 held every operator and every saved state.
        "q-flood" => QFlood(5_000_000),
        // Each element's Value joined the text of all its descendants (quadratic).
        "nested-xmp" => NestedXmp(4000, 64 * 1024),
        // Every finding inside the form repeated the long name (fixed in 7.13.0).
        "long-form-name" => LongFormName(64 * 1024, 4000),
        // The font check followed each Type0 font into the next (fixed in 7.13.0).
        "type0-chain" => Type0Chain(20_000),
        // Reports repeated one shared alternative text for every element (fixed in 7.13.0).
        "shared-alt" => SharedAlternativeText(2000, 64 * 1024),
        // 4.2 million form invocations; bounded by MaxDecodedBytes.
        "form-fan-out" => FormFanOut(22),
        _ => throw new ArgumentException("Unknown input.", nameof(name))
    };

    const string Catalog = "<< /Type /Catalog /Pages 2 0 R /Lang (en) >>";
    const string Pages = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";

    static string Page(string resources = "") =>
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R " + resources + " >>";

    static byte[] QFlood(int count)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var block = A(String.Concat(Enumerable.Repeat("q\n", 50_000)));
            for (int i = 0; i < count / 50_000; i++) zlib.Write(block);
        }
        return Pdf(A(Catalog), A(Pages), A(Page()), Stream("/Filter /FlateDecode", compressed.ToArray()));
    }

    static byte[] NestedXmp(int nested, int textLength)
    {
        var xml = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
            + "<rdf:Description rdf:about=\"\" xmlns:pdfuaid=\"http://www.aiim.org/pdfua/ns/id/\">"
            + String.Concat(Enumerable.Repeat("<pdfuaid:part>", nested)) + new string('1', textLength)
            + String.Concat(Enumerable.Repeat("</pdfuaid:part>", nested)) + "</rdf:Description></rdf:RDF></x:xmpmeta>";
        return Pdf(A("<< /Type /Catalog /Pages 2 0 R /Lang (en) /Metadata 5 0 R >>"), A(Pages), A(Page()), Stream("", A("")),
            Stream("/Type /Metadata /Subtype /XML", A(xml)));
    }

    static byte[] LongFormName(int nameLength, int missingDraws)
    {
        var name = "/" + new string('A', nameLength);
        return Pdf(A(Catalog), A(Pages), A(Page("/Resources << /XObject << " + name + " 5 0 R >> >>")),
            Stream("", A("/Artifact BMC " + name + " Do EMC")),
            Stream("/Type /XObject /Subtype /Form /BBox [0 0 10 10] /Resources << >>",
                A(String.Concat(Enumerable.Repeat("/M Do\n", missingDraws)))));
    }

    static byte[] Type0Chain(int levels)
    {
        var objects = new List<byte[]> { A(Catalog), A(Pages), A(Page("/Resources << /Font << /F 5 0 R >> >>")),
            Stream("", A("/Artifact BMC BT /F 12 Tf (x) Tj ET EMC")) };
        for (int i = 0; i < levels; i++)
            objects.Add(A(i < levels - 1
                ? $"<< /Type /Font /Subtype /Type0 /BaseFont /X /Encoding /Identity-H /DescendantFonts [{objects.Count + 2} 0 R] >>"
                : "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /X >>"));
        return Pdf(objects.ToArray());
    }

    static byte[] SharedAlternativeText(int elements, int textLength)
    {
        var kids = String.Join(" ", Enumerable.Range(7, elements).Select(n => n + " 0 R"));
        var objects = new List<byte[]> { A("<< /Type /Catalog /Pages 2 0 R /Lang (en) /StructTreeRoot 5 0 R >>"), A(Pages), A(Page()),
            Stream("", A("")), A("<< /Type /StructTreeRoot /K [" + kids + "] >>"), A("(" + new string('a', textLength) + ")") };
        for (int i = 0; i < elements; i++) objects.Add(A("<< /Type /StructElem /S /Figure /P 5 0 R /Alt 6 0 R >>"));
        return Pdf(objects.ToArray());
    }

    static byte[] FormFanOut(int levels)
    {
        var objects = new List<byte[]> { A(Catalog), A(Pages), A(Page("/Resources << /XObject << /N 5 0 R >> >>")),
            Stream("", A("/Artifact BMC /N Do EMC")) };
        for (int i = 0; i < levels; i++)
        {
            bool last = i == levels - 1;
            var resources = last ? "<< >>" : $"<< /XObject << /N {objects.Count + 2} 0 R >> >>";
            objects.Add(Stream("/Type /XObject /Subtype /Form /BBox [0 0 10 10] /Resources " + resources, A(last ? "0 0 1 1 re f" : "/N Do /N Do")));
        }
        return Pdf(objects.ToArray());
    }

    static byte[] A(string value) => Encoding.ASCII.GetBytes(value);

    static byte[] Stream(string dictionary, byte[] body) =>
        A("<< " + dictionary + " /Length " + body.Length + " >>\nstream\n").Concat(body).Concat(A("\nendstream")).ToArray();

    // Object n is objects[n - 1]; object 1 is the catalog.
    static byte[] Pdf(params byte[][] objects)
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(A(text));
        Write("%PDF-1.7\n");
        var offsets = new long[objects.Length];
        for (int i = 0; i < objects.Length; i++)
        {
            offsets[i] = data.Position;
            Write((i + 1) + " 0 obj\n");
            data.Write(objects[i]);
            Write("\nendobj\n");
        }
        long xref = data.Position;
        Write("xref\n0 " + (objects.Length + 1) + "\n0000000000 65535 f \n");
        foreach (var offset in offsets) Write(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + " 00000 n \n");
        Write("trailer\n<< /Root 1 0 R /Size " + (objects.Length + 1) + " >>\nstartxref\n" + xref + "\n%%EOF\n");
        return data.ToArray();
    }
}
