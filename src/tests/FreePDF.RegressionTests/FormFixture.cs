// An original AcroForm fixture for form-filling tests: text field, check box with a custom on-state, radio group with
// two widgets, combo box with export values, list box, and a hierarchical text field whose kid inherits /FT.
// MIT; see LICENSE.
namespace FreePDF.RegressionTests;

static class FormFixture
{
    static string Appearance(string content) => $"<< /Type /XObject /Subtype /Form /BBox [0 0 14 14] /Length {content.Length} >>\nstream\n{content}\nendstream";

    public static byte[] Create()
    {
        const string checkMark = "q 0 g 2 2 10 10 re f Q";
        const string empty = "q Q";
        return TestPdf.Create(new[] {
            /* 1 */ "<< /Type /Catalog /Pages 2 0 R /AcroForm 4 0 R >>",
            /* 2 */ "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            /* 3 */ "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [5 0 R 6 0 R 8 0 R 9 0 R 10 0 R 11 0 R 13 0 R] >>",
            /* 4 */ "<< /Fields [5 0 R 6 0 R 7 0 R 10 0 R 11 0 R 12 0 R] /DA (/Helv 0 Tf 0 g) /DR << /Font << /Helv 14 0 R /ZaDb 15 0 R >> >> >>",
            /* 5 */ "<< /FT /Tx /T (name) /V (Old name) /DA (/Helv 12 Tf 0 0 1 rg) /Type /Annot /Subtype /Widget /Rect [20 350 220 370] /P 3 0 R /F 4 >>",
            /* 6 */ "<< /FT /Btn /T (agree) /V /Off /AS /Off /Type /Annot /Subtype /Widget /Rect [20 320 34 334] /P 3 0 R /F 4 /AP << /N << /Ja 16 0 R /Off 17 0 R >> >> >>",
            /* 7 */ "<< /FT /Btn /Ff 49152 /T (size) /V /Off /Kids [8 0 R 9 0 R] >>",
            /* 8 */ "<< /Parent 7 0 R /Type /Annot /Subtype /Widget /Rect [20 290 34 304] /P 3 0 R /F 4 /AS /Off /AP << /N << /Small 16 0 R /Off 17 0 R >> >> >>",
            /* 9 */ "<< /Parent 7 0 R /Type /Annot /Subtype /Widget /Rect [40 290 54 304] /P 3 0 R /F 4 /AS /Off /AP << /N << /Large 16 0 R /Off 17 0 R >> >> >>",
            /* 10 */ "<< /FT /Ch /Ff 131072 /T (country) /Opt [[(NO) (Norway)] [(SE) (Sweden)] [(DK) (Denmark)]] /V (NO) /DA (/Helv 10 Tf 0 g) /Type /Annot /Subtype /Widget /Rect [20 250 160 270] /P 3 0 R /F 4 >>",
            /* 11 */ "<< /FT /Ch /T (colour) /Opt [(Red) (Green) (Blue)] /DA (/Helv 10 Tf 0 g) /Type /Annot /Subtype /Widget /Rect [20 180 160 240] /P 3 0 R /F 4 >>",
            /* 12 */ "<< /FT /Tx /T (address) /Kids [13 0 R] /DA (/Helv 10 Tf 0 g) >>",
            /* 13 */ "<< /T (city) /Parent 12 0 R /Type /Annot /Subtype /Widget /Rect [20 150 220 170] /P 3 0 R /F 4 >>",
            /* 14 */ "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            /* 15 */ "<< /Type /Font /Subtype /Type1 /BaseFont /ZapfDingbats >>",
            /* 16 */ Appearance(checkMark),
            /* 17 */ Appearance(empty),
        });
    }
}
