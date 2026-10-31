// Original content-preservation fixtures shared by tests and both lab engines. MIT.
using System.Text;

namespace FreePDF.Lab.Fixtures;

static class PreservationFixtures
{
    internal static byte[] SharedContent(bool array)
    {
        const string content = "0 1 0 rg 10 10 20 20 re f";
        var contents = array ? "[5 0 R]" : "5 0 R";
        return PdfFixtureData.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents {contents} >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents {contents} >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream"
        });
    }
    internal static byte[] Incremental(int markerIndex = 1)
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new Dictionary<int,int>();
        void Object(int number, string body) { offsets[number]=text.Length; text.Append($"{number} 0 obj\n{body}\nendobj\n"); }
        string Stream(string value,string keys="") => $"<< /Length {value.Length} {keys} >>\nstream\n{value}\nendstream";
        string Packed(string first,string second)
        {
            var header=$"8 0 9 {first.Length+1} "; var data=header+first+" "+second;
            return Stream(data,$"/Type /ObjStm /N 2 /First {header.Length}");
        }
        string Entry(int type,int second,int third) => type.ToString("X2")+second.ToString("X8")+third.ToString("X4");
        Object(1,"<< /Type /Catalog /Pages 2 0 R /Marker 9 0 R >>");
        Object(2,"<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object(3,"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents 8 0 R >>");
        Object(4,Packed("[5 0 R]","(old marker)"));
        Object(5,Stream("0 1 0 rg 20 20 100 80 re f"));
        var firstXref=text.Length;
        var rows=Entry(0,0,65535)+string.Concat(Enumerable.Range(1,5).Select(n=>Entry(1,offsets[n],0)))+Entry(0,0,65535)+Entry(1,firstXref,0)+Entry(2,4,0)+Entry(2,4,1)+">";
        Object(7,Stream(rows,"/Type /XRef /Root 1 0 R /Size 10 /W [1 4 2] /Filter /ASCIIHexDecode"));
        text.Append($"startxref\n{firstXref}\n%%EOF\n");
        // The newer stream contains an obsolete copy of object 8. Only object 9
        // is selected from it by the new revision. Stream ordering cannot decide this.
        Object(6,Packed("[]","(current marker)"));
        var lastXref=text.Length;
        Object(10,Stream(Entry(1,offsets[6],0)+Entry(2,6,markerIndex)+Entry(1,lastXref,0)+">",$"/Type /XRef /Root 1 0 R /Size 11 /W [1 4 2] /Index [6 1 9 2] /Prev {firstXref} /Filter /ASCIIHexDecode"));
        text.Append($"startxref\n{lastXref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }
    internal static byte[] Semantic()
    {
        const string content="BT /F1 12 Tf 20 100 Td (Invoice 123) Tj ET";
        const string attachment="<invoice id=\"123\"/>";
        return PdfFixtureData.Create(new[]{
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [9 0 R] /DR << /Font << /F1 5 0 R >> >> /DA (/F1 12 Tf 0 g) >> /Names << /Dests << /Names [(invoice) [3 0 R /Fit]] >> /EmbeddedFiles << /Names [(invoice.xml) 10 0 R] >> >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> /Annots [7 0 R 8 0 R 9 0 R] >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Title (Original title) >>",
            "<< /Type /Annot /Subtype /Text /Rect [10 10 30 30] /Contents (review note) /P 3 0 R >>",
            "<< /Type /Annot /Subtype /Link /Rect [20 90 150 110] /A << /S /URI /URI (https://example.org/invoice/123) >> /P 3 0 R >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (Customer) /V (Ada Example) /Rect [20 40 150 60] /P 3 0 R >>",
            "<< /Type /Filespec /F (invoice.xml) /UF (invoice.xml) /EF << /F 11 0 R >> >>",
            $"<< /Type /EmbeddedFile /Length {attachment.Length} >>\nstream\n{attachment}\nendstream"
        },_=>"/Info 6 0 R");
    }
}
