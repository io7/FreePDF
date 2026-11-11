// PdfSignatureVerifier, later changes: an object that a file lists in a role that may change after the signature (an
// appearance, the document information, an annotation ...) and that a viewer also uses in a role that may not (page
// content, the page tree, a page box ...). Every pair of such roles is one cell: the file is crafted, signed, and an
// update changes the object; the signature must not stay Valid. A control per lenient role shows that its exemption
// is given when the object has that role alone, so that a refused cell means something. Original fixtures; MIT, see
// LICENSE.
#if NET8_0_OR_GREATER
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Signatures;
using Xunit;

namespace FreePDF.RegressionTests;

public class SignatureAliasingMatrixTests
{
    public SignatureAliasingMatrixTests() => TestFonts.Initialize();

    enum Kind { Dictionary, Stream, Array, Number }

    // A file under construction: objects 1 to 3 are the catalog, the root of the page tree and the first page, put
    // together from their entries when the file is built.
    sealed class Doc
    {
        public readonly List<string> Objects = ["", "", ""];
        public readonly Dictionary<string, string> Catalog = new();
        public readonly Dictionary<string, string> Pages = new() { ["/Kids"] = "[3 0 R]", ["/Count"] = "1" };
        public readonly Dictionary<string, string> Page = new() { ["/Parent"] = "2 0 R", ["/MediaBox"] = "[0 0 612 792]" };
        public readonly List<int> Annotations = [];
        public readonly List<int> Fields = [];
        public string? Trailer;

        public int Add(string body)
        {
            Objects.Add(body);
            return Objects.Count;
        }

        public void Set(int number, string body) => Objects[number - 1] = body;

        public byte[] Build()
        {
            var catalog = new StringBuilder("<< /Type /Catalog /Pages 2 0 R");
            foreach (var pair in Catalog)
                catalog.Append($" {pair.Key} {pair.Value}");
            if (Fields.Count > 0 && !Catalog.ContainsKey("/AcroForm"))
                catalog.Append($" /AcroForm << /Fields [{Refs(Fields)}] >>");
            Objects[0] = catalog + " >>";
            Objects[1] = "<< /Type /Pages" + string.Concat(Pages.Select(pair => $" {pair.Key} {pair.Value}")) + " >>";
            var page = new StringBuilder("<< /Type /Page");
            foreach (var pair in Page)
                page.Append($" {pair.Key} {pair.Value}");
            if (Annotations.Count > 0 && !Page.ContainsKey("/Annots"))
                page.Append($" /Annots [{Refs(Annotations)}]");
            Objects[2] = page + " >>";
            return TestPdf.Create(Objects.ToArray(), Trailer == null ? null : _ => Trailer);
        }

        public static string Refs(IEnumerable<int> numbers) => string.Join(" ", numbers.Select(n => $"{n} 0 R"));
    }

    // What an update has to go on: the signed file, the aliased object, the entries its lenient role gave it, and the
    // first free object number.
    sealed record UpdateContext(byte[] Signed, int Target, string Extra, int Next)
    {
        // The object as the signed file has it last (signing may have rewritten it).
        public string Body(int number)
            => Regex.Matches(Latin1(Signed), $@"(?s)(?<!\d){number} 0 obj\s*(.*?)\s*endobj").Cast<Match>().Last().Groups[1].Value;
    }

    // A use that may not change after the signature. Setup adds the aliased object, with the extra entries of its
    // lenient role when it is a dictionary or stream, and returns its number. Strictness: 3 = no certification permits
    // the change; 2 = only a certification that permits annotations does (P=3).
    sealed record Slot(string Name, Kind Kind, int Strictness, Func<Doc, string, int> Setup, Func<UpdateContext, (int, string)[]> Update,
        bool OwnSubtype = false);

    // A use that may change after the signature from level Level on. Extra: entries the aliased dictionary needs to
    // have that role. Attach makes the file refer to the object in that role.
    sealed record Role(string Name, int Level, Func<Kind, bool> Accepts, string Extra, Action<Doc, int> Attach, Kind ControlKind);

    static string Stream(string entries, string data) => $"<< {entries} /Length {data.Length} >>\nstream\n{data}\nendstream";

    const string Black = "0 0 612 792 re f";
    const string EmptyForm = "<< /Type /XObject /Subtype /Form /BBox [0 0 100 50] /Length 0 >>\nstream\n\nendstream";

    static readonly Slot[] Slots =
    [
        new("page /Contents", Kind.Stream, 3,
            (d, extra) =>
            {
                int target = d.Add(Stream(extra, "0 0 1 rg " + Black));
                d.Page["/Contents"] = $"{target} 0 R";
                return target;
            },
            c => [(c.Target, Stream(c.Extra, "1 0 0 rg " + Black))]),
        new("XObject a page draws", Kind.Stream, 3,
            (d, extra) =>
            {
                int target = d.Add(Stream(extra + " /BBox [0 0 612 792]", "0 0 1 rg " + Black));
                d.Page["/Contents"] = $"{d.Add(Stream("", "/A Do"))} 0 R";
                d.Page["/Resources"] = $"<< /XObject << /A {target} 0 R >> >>";
                return target;
            },
            c => [(c.Target, Stream(c.Extra + " /BBox [0 0 612 792]", "1 0 0 rg " + Black))]),
        new("page /Resources", Kind.Dictionary, 3,
            (d, extra) =>
            {
                int form = d.Add(EmptyForm);
                int target = d.Add($"<< {extra} /XObject << /A {form} 0 R >> >>");
                d.Page["/Contents"] = $"{d.Add(Stream("", "/A Do"))} 0 R";
                d.Page["/Resources"] = $"{target} 0 R";
                return target;
            },
            c =>
            [
                (c.Target, $"<< {c.Extra} /XObject << /A {c.Next} 0 R >> >>"),
                (c.Next, Stream("/Type /XObject /Subtype /Form /BBox [0 0 612 792]", Black)),
            ]),
        new("font of a page", Kind.Dictionary, 3,
            (d, extra) =>
            {
                int target = d.Add($"<< {extra} /BaseFont /Helvetica >>");
                d.Page["/Contents"] = $"{d.Add(Stream("", "BT /F1 12 Tf (Signed) Tj ET"))} 0 R";
                d.Page["/Resources"] = $"<< /Font << /F1 {target} 0 R >> >>";
                return target;
            },
            c => [(c.Target, $"<< {c.Extra} /BaseFont /Courier >>")]),
        new("page /MediaBox", Kind.Array, 3,
            (d, _) =>
            {
                int target = d.Add("[0 0 612 792]");
                d.Page["/MediaBox"] = $"{target} 0 R";
                return target;
            },
            c => [(c.Target, c.Body(c.Target).Replace("612", "10"))]),
        new("page /Rotate", Kind.Number, 3,
            (d, _) =>
            {
                int target = d.Add("0");
                d.Page["/Rotate"] = $"{target} 0 R";
                return target;
            },
            c => [(c.Target, "180")]),
        new("page tree /Kids", Kind.Array, 3,
            (d, _) =>
            {
                int content = d.Add(Stream("", "1 0 0 rg " + Black));
                int second = d.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {content} 0 R >>");
                int target = d.Add($"[3 0 R {second} 0 R]");
                d.Pages["/Kids"] = $"{target} 0 R";
                d.Pages["/Count"] = "2";
                return target;
            },
            c =>
            {
                var match = Regex.Match(c.Body(c.Target), @"3 0 R\s+(\d+) 0 R");
                return [(c.Target, c.Body(c.Target).Replace(match.Value, $"{match.Groups[1].Value} 0 R 3 0 R"))];
            }),
        new("page tree node", Kind.Dictionary, 3,
            (d, extra) =>
            {
                int content = d.Add(Stream("", "1 0 0 rg " + Black));
                int second = d.Add("");
                int target = d.Add($"<< {extra} /Type /Pages /Parent 2 0 R /Kids [3 0 R {second} 0 R] /Count 2 >>");
                d.Set(second, $"<< /Type /Page /Parent {target} 0 R /MediaBox [0 0 612 792] /Contents {content} 0 R >>");
                d.Page["/Parent"] = $"{target} 0 R";
                d.Pages["/Kids"] = $"[{target} 0 R]";
                d.Pages["/Count"] = "2";
                return target;
            },
            c =>
            {
                var match = Regex.Match(c.Body(c.Target), @"3 0 R\s+(\d+) 0 R");
                return [(c.Target, c.Body(c.Target).Replace(match.Value, $"{match.Groups[1].Value} 0 R 3 0 R"))];
            }),
        new("second page", Kind.Dictionary, 3,
            (d, extra) =>
            {
                int content = d.Add(Stream("", "0 0 1 rg " + Black));
                int target = d.Add($"<< {extra} /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {content} 0 R >>");
                d.Pages["/Kids"] = $"[3 0 R {target} 0 R]";
                d.Pages["/Count"] = "2";
                return target;
            },
            c =>
            [
                (c.Target, $"<< {c.Extra} /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {c.Next} 0 R >>"),
                (c.Next, Stream("", "1 0 0 rg " + Black)),
            ]),
        new("optional content configuration", Kind.Dictionary, 3,
            (d, extra) =>
            {
                int group = d.Add("<< /Type /OCG /Name (Layer) >>");
                int target = d.Add($"<< {extra} /OFF [{group} 0 R] >>");
                d.Catalog["/OCProperties"] = $"<< /OCGs [{group} 0 R] /D {target} 0 R >>";
                d.Page["/Contents"] = $"{d.Add(Stream("", "/OC /L BDC " + Black + " EMC"))} 0 R";
                d.Page["/Resources"] = $"<< /Properties << /L {group} 0 R >> >>";
                return target;
            },
            c => [(c.Target, $"<< {c.Extra} /ON [{Regex.Match(c.Body(c.Target), @"/OFF\s*\[\s*(\d+)").Groups[1].Value} 0 R] >>")]),
        new("structure tree root", Kind.Dictionary, 3,
            (d, extra) =>
            {
                int target = d.Add($"<< {extra} /Type /StructTreeRoot /K [] >>");
                d.Catalog["/StructTreeRoot"] = $"{target} 0 R";
                d.Catalog["/MarkInfo"] = "<< /Marked true >>";
                return target;
            },
            c =>
            [
                (c.Target, $"<< {c.Extra} /Type /StructTreeRoot /K [{c.Next} 0 R] >>"),
                (c.Next, $"<< /Type /StructElem /S /P /P {c.Target} 0 R /Pg 3 0 R /ActualText (Not what was signed) >>"),
            ]),
        new("bookmark", Kind.Dictionary, 3,
            (d, extra) =>
            {
                int root = d.Add("");
                int target = d.Add($"<< {extra} /Title (Signed) /Parent {root} 0 R /Dest [3 0 R /Fit] >>");
                d.Set(root, $"<< /Type /Outlines /First {target} 0 R /Last {target} 0 R /Count 1 >>");
                d.Catalog["/Outlines"] = $"{root} 0 R";
                return target;
            },
            c => [(c.Target, c.Body(c.Target).Replace("(Signed)", "(Other)"))]),
        new("viewer preferences", Kind.Dictionary, 3,
            (d, extra) =>
            {
                int target = d.Add($"<< {extra} /HideToolbar false >>");
                d.Catalog["/ViewerPreferences"] = $"{target} 0 R";
                return target;
            },
            c => [(c.Target, $"<< {c.Extra} /HideToolbar true >>")]),
        new("named destinations", Kind.Dictionary, 3,
            (d, extra) =>
            {
                int target = d.Add($"<< {extra} /Start [3 0 R /Fit] >>");
                d.Catalog["/Dests"] = $"{target} 0 R";
                return target;
            },
            c => [(c.Target, $"<< {c.Extra} /Start [3 0 R /FitH 100] >>")]),
        new("form /Fields", Kind.Array, 3,
            (d, _) =>
            {
                int field = d.Add("<< /Type /Annot /Subtype /Widget /FT /Tx /T (kept) /V (Signed) /Rect [100 100 200 120] /P 3 0 R >>");
                d.Annotations.Add(field);
                int target = d.Add($"[{field} 0 R]");
                d.Catalog["/AcroForm"] = $"<< /Fields {target} 0 R >>";
                return target;
            },
            c => [(c.Target, Regex.Replace(c.Body(c.Target), @"^\[\s*\d+ 0 R", "["))]),

        // What only a certification that permits annotations lets change.
        new("stamp", Kind.Dictionary, 2,
            (d, extra) =>
            {
                int form = d.Add(Stream("/Type /XObject /Subtype /Form /BBox [0 0 612 792]", Black));
                int target = d.Add($"<< {extra} /Type /Annot /Subtype /Stamp /Rect [0 0 0 0] /P 3 0 R /AP << /N {form} 0 R >> >>");
                d.Annotations.Add(target);
                return target;
            },
            c => [(c.Target, c.Body(c.Target).Replace("/Rect [0 0 0 0]", "/Rect [0 0 612 792]"))], OwnSubtype: true),
        new("stamp appearance", Kind.Stream, 2,
            (d, extra) =>
            {
                int target = d.Add(Stream(extra + " /BBox [0 0 612 792]", "1 1 1 rg " + Black));
                d.Annotations.Add(d.Add($"<< /Type /Annot /Subtype /Stamp /Rect [0 0 612 792] /P 3 0 R /AP << /N {target} 0 R >> >>"));
                return target;
            },
            c => [(c.Target, Stream(c.Extra + " /BBox [0 0 612 792]", "0 0 0 rg " + Black))]),
        new("stamp /F", Kind.Number, 2,
            (d, _) =>
            {
                int form = d.Add(Stream("/Type /XObject /Subtype /Form /BBox [0 0 612 792]", Black));
                int target = d.Add("2");
                d.Annotations.Add(d.Add($"<< /Type /Annot /Subtype /Stamp /F {target} 0 R /Rect [0 0 612 792] /P 3 0 R /AP << /N {form} 0 R >> >>"));
                return target;
            },
            c => [(c.Target, "0")]),
        new("stamp /Rect", Kind.Array, 2,
            (d, _) =>
            {
                int form = d.Add(Stream("/Type /XObject /Subtype /Form /BBox [0 0 612 792]", Black));
                int target = d.Add("[0 0 0 0]");
                d.Annotations.Add(d.Add($"<< /Type /Annot /Subtype /Stamp /Rect {target} 0 R /P 3 0 R /AP << /N {form} 0 R >> >>"));
                return target;
            },
            c => [(c.Target, "[0 0 612 792]")]),
        new("page /Annots", Kind.Array, 2,
            (d, _) =>
            {
                int stamp = d.Add("<< /Type /Annot /Subtype /Stamp /Rect [0 0 10 10] /P 3 0 R >>");
                int target = d.Add($"[{stamp} 0 R]");
                d.Page["/Annots"] = $"{target} 0 R";
                return target;
            },
            c =>
            [
                (c.Target, Regex.Replace(c.Body(c.Target), @"\]\s*$", $" {c.Next} 0 R]")),
                (c.Next, $"<< /Type /Annot /Subtype /Stamp /Rect [0 0 612 792] /P 3 0 R /AP << /N {c.Next + 1} 0 R >> >>"),
                (c.Next + 1, Stream("/Type /XObject /Subtype /Form /BBox [0 0 612 792]", Black)),
            ]),
        new("empty signature widget appearance", Kind.Stream, 2,
            (d, extra) =>
            {
                int target = d.Add(Stream(extra + " /BBox [0 0 612 792]", ""));
                int widget = d.Add($"<< /Type /Annot /Subtype /Widget /FT /Sig /T (empty) /Rect [0 0 612 792] /P 3 0 R /AP << /N {target} 0 R >> >>");
                d.Annotations.Add(widget);
                d.Fields.Add(widget);
                return target;
            },
            c => [(c.Target, Stream(c.Extra + " /BBox [0 0 612 792]", Black))]),
    ];

    static bool Any(Kind _) => true;
    static bool DictionaryLike(Kind kind) => kind is Kind.Dictionary or Kind.Stream;
    // The reader refuses a file whose widget has an array as an appearance, so such a file gets no verdict at all.
    static bool NotArray(Kind kind) => kind != Kind.Array;

    // Cells this builder cannot make: signing adds its widget to the page's /Annots, which here is the /Kids array, and
    // the reader refuses a widget in the page tree. SignatureVerifierTests has that pair with an invisible signature
    // (A_kids_array_that_is_also_an_annots_array_keeps_the_page_order).
    static readonly HashSet<(string Slot, string Role)> NotBuildable = [("page tree /Kids", "the /Annots array")];

    // Cells where the signature stays Valid in 7.10.1: a documented limit for files crafted before signing
    // (docs/support-policy.md). A cell that is refused later must leave this list, and no other cell may join it.
    // - An array that is a page's /MediaBox and also its /Annots: a change to the box is not seen under any level.
    // - A bookmark that a text widget also lists as an appearance: a change to it is refused under P=1, and passes as a
    //   field's appearance from P=2 on, although the catalog reaches the bookmark through /Outlines.
    static readonly HashSet<(string Slot, string Role, string Level)> KnownLeaks =
    [
        ("page /MediaBox", "the /Annots array", "P=1"),
        ("page /MediaBox", "the /Annots array", "P=2"),
        ("page /MediaBox", "the /Annots array", "P=3"),
        ("page /MediaBox", "the /Annots array", "approval"),
        ("bookmark", "widget /AP", "P=2"),
        ("bookmark", "widget /AP", "P=3"),
        ("bookmark", "widget /AP", "approval"),
    ];

    static readonly Role[] Roles =
    [
        new("widget /AP", 2, NotArray, "",
            (d, target) =>
            {
                int widget = d.Add($"<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /Rect [300 300 400 350] /P 3 0 R /AP << /N {d.Add(EmptyForm)} 0 R /R {target} 0 R >> >>");
                d.Annotations.Add(widget);
                d.Fields.Add(widget);
            }, Kind.Stream),
        // Not roles: what a widget's appearance stream uses as resources, and a widget's /MK, get no exemption (a change
        // to such an object is refused also when it has that use alone).
        new("stamp /AP", 3, Any, "",
            (d, target) => d.Annotations.Add(d.Add(
                $"<< /Type /Annot /Subtype /Stamp /Rect [300 300 400 350] /P 3 0 R /AP << /N {d.Add(EmptyForm)} 0 R /R {target} 0 R >> >>")),
            Kind.Stream),
        // The reader refuses a file whose /Info or /Metadata is an array or a number.
        new("trailer /Info", 2, DictionaryLike, "", (d, target) => d.Trailer = $"/Info {target} 0 R", Kind.Dictionary),
        new("catalog /Metadata", 2, DictionaryLike, "", (d, target) => d.Catalog["/Metadata"] = $"{target} 0 R", Kind.Stream),
        new("DSS /Certs", 1, Any, "", (d, target) => d.Catalog["/DSS"] = $"<< /Certs [{target} 0 R] >>", Kind.Stream),
        new("DSS /VRI", 1, Any, "", (d, target) => d.Catalog["/DSS"] = $"<< /VRI {target} 0 R >>", Kind.Dictionary),
        new("annotation in /Annots", 3, DictionaryLike, "/Subtype /Stamp /Rect [0 0 0 0]", (d, target) => d.Annotations.Add(target), Kind.Dictionary),
        new("the /Annots array", 3, kind => kind == Kind.Array, "", (d, target) => d.Page["/Annots"] = $"{target} 0 R", Kind.Array),
        new("form field", 2, DictionaryLike, "/FT /Tx /T (alias)", (d, target) => d.Fields.Add(target), Kind.Dictionary),
        new("form widget", 2, DictionaryLike, "/Subtype /Widget /FT /Tx /T (alias) /Rect [0 0 0 0]",
            (d, target) =>
            {
                d.Annotations.Add(target);
                d.Fields.Add(target);
            }, Kind.Dictionary),
    ];

    // The object in its lenient role alone, and a change that role permits.
    static Slot Control(Kind kind) => kind switch
    {
        Kind.Stream => new("(control)", kind, 0, (d, extra) => d.Add(Stream(extra + " /BBox [0 0 100 50]", "")),
            c => [(c.Target, Stream(c.Extra + " /BBox [0 0 100 50]", "0 0 10 10 re f"))]),
        Kind.Dictionary => new("(control)", kind, 0, (d, extra) => d.Add($"<< {extra} /V (before) >>"),
            c => [(c.Target, c.Body(c.Target).Replace("(before)", "(after)"))]),
        Kind.Array => new("(control)", kind, 0, (d, _) => d.Add("[]"),
            c =>
            [
                (c.Target, Regex.Replace(c.Body(c.Target), @"\]\s*$", $" {c.Next} 0 R]")),
                (c.Next, "<< /Type /Annot /Subtype /Stamp /Rect [0 0 10 10] /P 3 0 R >>"),
            ]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    enum Outcome
    {
        // The update was refused or reported: the signature is not Valid and names a change.
        Refused,
        // Not Valid, but without naming a change: the cell says nothing about the rule.
        NotValidOtherwise,
        // The signature stayed Valid.
        Valid,
        // The crafted file is not Valid before the update, or cannot be signed: no cell.
        NotBuilt,
    }

    sealed record Cell(string Slot, string Role, string Level, Outcome Outcome, string Detail);

    static readonly Lazy<X509Certificate2> Certificate = new(() =>
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FreePDF aliasing matrix", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    });

    static async Task<Cell> Run(Slot slot, Role role, PdfCertificationLevel? level)
    {
        string levelName = level == null ? "approval" : $"P={(int)level}";
        var options = new PdfSignatureVerificationOptions { TrustedRoots = [Certificate.Value] };
        byte[] signed;
        int target;
        try
        {
            var doc = new Doc();
            target = slot.Setup(doc, role.Extra);
            role.Attach(doc, target);
            signed = Sign(doc.Build(), level);
            var before = await PdfSignatureVerifier.VerifyAsync(signed, options);
            if (before.Count != 1 || before[0].Status != PdfSignatureStatus.Valid)
            {
                return new(slot.Name, role.Name, levelName, Outcome.NotBuilt,
                    before.Count == 1 ? $"{before[0].Status}: {string.Join(" | ", before[0].Reasons)}" : $"{before.Count} signatures");
            }
        }
        catch (Exception ex)
        {
            return new(slot.Name, role.Name, levelName, Outcome.NotBuilt, $"{ex.GetType().Name}: {ex.Message}");
        }

        var updated = AppendUpdate(signed, slot.Update(new(signed, target, role.Extra, Size(signed))));
        var after = await PdfSignatureVerifier.VerifyAsync(updated, options);
        if (after.Count != 1)
            return new(slot.Name, role.Name, levelName, Outcome.NotValidOtherwise, $"{after.Count} signatures");
        var result = after[0];
        if (result.Status == PdfSignatureStatus.Valid)
            return new(slot.Name, role.Name, levelName, Outcome.Valid, "");
        var change = result.ForbiddenChange ?? result.UnexpectedChange;
        return change != null
            ? new(slot.Name, role.Name, levelName, Outcome.Refused, change)
            : new(slot.Name, role.Name, levelName, Outcome.NotValidOtherwise, $"{result.Status}: {string.Join(" | ", result.Reasons)}");
    }

    static async Task<List<Cell>> RunAll()
    {
        var cells = new List<Cell>();
        foreach (var role in Roles)
        {
            // The role alone, at the lowest level that permits its change and at the highest.
            cells.Add(await Run(Control(role.ControlKind), role, (PdfCertificationLevel)role.Level));
            cells.Add(await Run(Control(role.ControlKind), role, PdfCertificationLevel.FormFillingAndAnnotations));
        }
        foreach (var slot in Slots)
        {
            foreach (var role in Roles)
            {
                if (!role.Accepts(slot.Kind) || slot.OwnSubtype && role.Extra.Contains("/Subtype") || NotBuildable.Contains((slot.Name, role.Name)))
                    continue;
                // Under every level that does not permit the slot's change, and after an approval signature.
                if (slot.Strictness == 2 && role.Level > 2)
                    continue;
                cells.Add(await Run(slot, role, PdfCertificationLevel.NoChanges));
                cells.Add(await Run(slot, role, PdfCertificationLevel.FormFilling));
                if (slot.Strictness == 3)
                    cells.Add(await Run(slot, role, PdfCertificationLevel.FormFillingAndAnnotations));
                cells.Add(await Run(slot, role, null));
            }
        }
        return cells;
    }

    static string Report(IEnumerable<Cell> cells)
        => string.Join("\n", cells.Select(c => $"{c.Outcome,-17} | {c.Slot,-33} | {c.Role,-22} | {c.Level,-8} | {c.Detail}"));

    [Fact]
    public async Task An_object_with_a_lenient_and_a_strict_role_keeps_the_strict_rule()
    {
        var cells = await RunAll();
        if (Environment.GetEnvironmentVariable("FREEPDF_ALIASING_REPORT") is { Length: > 0 } path)
            File.WriteAllText(path, Report(cells) + "\n");

        var controls = cells.Where(c => c.Slot == "(control)").ToList();
        var matrix = cells.Where(c => c.Slot != "(control)").ToList();
        var deadRoles = controls.Where(c => c.Outcome != Outcome.Valid).ToList();
        var leaks = matrix.Where(c => c.Outcome == Outcome.Valid).ToList();
        var newLeaks = leaks.Where(c => !KnownLeaks.Contains((c.Slot, c.Role, c.Level))).ToList();
        var closed = KnownLeaks.Where(known => !leaks.Any(c => (c.Slot, c.Role, c.Level) == known)).ToList();
        var unclear = matrix.Where(c => c.Outcome is Outcome.NotBuilt or Outcome.NotValidOtherwise).ToList();

        Assert.True(deadRoles.Count == 0, "A lenient role's own change is not Valid, so its cells show nothing:\n" + Report(deadRoles));
        Assert.True(unclear.Count == 0, "Cells that show nothing: the file could not be built, or the update failed for another reason:\n" + Report(unclear));
        Assert.True(newLeaks.Count == 0, "The signature stays Valid after a change to an object with a stricter role:\n" + Report(newLeaks));
        Assert.True(closed.Count == 0, "These cells are refused now; take them off KnownLeaks: " + string.Join(", ", closed));
    }

    static byte[] Sign(byte[] pdf, PdfCertificationLevel? certification)
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Append);
        DigitalSignatureHandler.ForDocument(document, new PdfSharpDefaultSigner(Certificate.Value, PdfMessageDigestType.SHA256),
            new DigitalSignatureOptions { Rectangle = new XRect(36, 36, 160, 40), Certification = certification });
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    static int Size(byte[] pdf) => int.Parse(Regex.Matches(Latin1(pdf), @"/Size\s+(\d+)").Cast<Match>().Last().Groups[1].Value);

    // An incremental update written by hand: the objects, a cross-reference section for them, and a trailer that keeps
    // the file's /Root and /Info.
    static byte[] AppendUpdate(byte[] pdf, (int Number, string Body)[] objects)
    {
        var text = Latin1(pdf);
        var startXref = Regex.Matches(text, @"startxref\s+(\d+)").Cast<Match>().Last().Groups[1].Value;
        var root = Regex.Matches(text, @"/Root\s+(\d+\s+\d+\s+R)").Cast<Match>().Last().Groups[1].Value;
        var info = Regex.Matches(text, @"/Info\s+(\d+\s+\d+\s+R)").Cast<Match>().LastOrDefault()?.Groups[1].Value;
        int size = Math.Max(Size(pdf), objects.Max(o => o.Number) + 1);
        var update = new StringBuilder("\n");
        var offsets = new List<(int Number, int Offset)>();
        foreach (var (number, body) in objects)
        {
            offsets.Add((number, pdf.Length + update.Length));
            update.Append($"{number} 0 obj\n{body}\nendobj\n");
        }
        int xrefOffset = pdf.Length + update.Length;
        update.Append("xref\n");
        foreach (var (number, offset) in offsets.OrderBy(o => o.Number))
            update.Append($"{number} 1\n{offset:D10} 00000 n\r\n");
        update.Append($"trailer\n<< /Size {size} /Root {root}{(info != null ? " /Info " + info : "")} /Prev {startXref} >>\n");
        update.Append($"startxref\n{xrefOffset}\n%%EOF\n");
        return pdf.Concat(Encoding.ASCII.GetBytes(update.ToString())).ToArray();
    }

    static string Latin1(byte[] bytes) => new(bytes.Select(b => (char)b).ToArray());
}
#endif
