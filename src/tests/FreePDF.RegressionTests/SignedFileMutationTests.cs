// PdfSignatureVerifier, later changes: no value of a signed file can change unseen. Each fixture is certified with "no
// changes" (P=1). Then, one value at a time, an update rewrites one object of the file with that value changed: a
// name, a number, a string, a boolean, or a byte of stream data. The signature must not stay Valid. A value that
// reading a file sets, drops or replaces would hide its change, and shows here. Original fixtures; MIT, see LICENSE.
#if NET8_0_OR_GREATER
using System.Globalization;
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

public class SignedFileMutationTests
{
    public SignedFileMutationTests() => TestFonts.Initialize();

    sealed record Mutation(int Object, string What, string Body);

    sealed record Row(string Fixture, int Object, string What, bool Valid, string Detail);

    // A literal string, a hexadecimal string, a name, a number, or a boolean. Hexadecimal strings stay as they are.
    static readonly Regex Token = new(
        @"(?<string>\((?:\\.|[^\\()])*\))|(?<hex><(?!<)[0-9A-Fa-f\s]*>)|(?<name>/[^\s/<>\[\]()%]*)"
        + @"|(?<number>(?<![\w.])[+-]?(?:\d+\.?\d*|\.\d+)(?![\w.]))|(?<word>\b(?:true|false)\b)", RegexOptions.Singleline);

    // The object's body with one value changed, once for every value: of a stream, the values of its dictionary and
    // the first byte of its data.
    static IEnumerable<Mutation> Mutations(int number, string body)
    {
        string dictionary = body, rest = "";
        var stream = Regex.Match(body, @"(?s)^(?<dictionary>.*?>>)(?<rest>\s*stream\r?\n.*endstream)\s*$");
        if (stream.Success)
        {
            dictionary = stream.Groups["dictionary"].Value;
            rest = stream.Groups["rest"].Value;
        }
        // The name before a value says where it is: mostly the key it belongs to.
        string after = "";
        foreach (Match token in Token.Matches(dictionary))
        {
            string changed;
            if (token.Groups["string"].Success)
                changed = token.Value.Substring(0, token.Value.Length - 1) + "X)";
            else if (token.Groups["name"].Success)
                changed = token.Value + "X";
            else if (token.Groups["number"].Success)
                changed = (decimal.Parse(token.Value, NumberStyles.Float, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
            else if (token.Groups["word"].Success)
                changed = token.Value == "true" ? "false" : "true";
            else
                continue;
            yield return new(number, $"{(token.Groups["name"].Success ? "" : after + " ")}{Shown(token.Value)} -> {Shown(changed)}",
                dictionary.Substring(0, token.Index) + changed + dictionary.Substring(token.Index + token.Length) + rest);
            if (token.Groups["name"].Success)
                after = token.Value;
        }
        if (rest.Length > 0)
        {
            int start = Regex.Match(rest, @"^\s*stream\r?\n").Length;
            if (!Regex.IsMatch(rest.Substring(start), @"^\r?\n?endstream"))
            {
                char other = rest[start] == 'A' ? 'B' : 'A';
                yield return new(number, "first byte of the stream data", dictionary + rest.Substring(0, start) + other + rest.Substring(start + 1));
            }
        }

        static string Shown(string value) => value.Length <= 40 ? value : value.Substring(0, 37) + "...";
    }

    static readonly Lazy<X509Certificate2> Certificate = new(() =>
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FreePDF mutation test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    });

    static async Task<List<Row>> Run(string fixture)
    {
        var options = new PdfSignatureVerificationOptions { TrustedRoots = [Certificate.Value] };
        var signed = Certify(ReadFixtures.Get(fixture));
        var before = await PdfSignatureVerifier.VerifyAsync(signed, options);
        Assert.True(before.Count == 1 && before[0].Status == PdfSignatureStatus.Valid,
            $"The certified fixture '{fixture}' is not Valid before any change: {string.Join(" | ", before.SelectMany(r => r.Reasons))}");

        var rows = new List<Row>();
        var text = Latin1(signed);
        for (int number = 1; number < Size(text); number++)
        {
            // The object as the file has it last: signing rewrites some.
            var last = Regex.Matches(text, $@"(?s)(?<!\d){number} 0 obj\s*(.*?)\s*endobj").Cast<Match>().LastOrDefault();
            if (last == null)
                continue;
            foreach (var mutation in Mutations(number, last.Groups[1].Value))
            {
                var updated = AppendUpdate(signed, text, (number, mutation.Body));
                try
                {
                    var after = await PdfSignatureVerifier.VerifyAsync(updated, options);
                    bool valid = after.Any(result => result.Status == PdfSignatureStatus.Valid);
                    var first = after.FirstOrDefault();
                    rows.Add(new(fixture, number, mutation.What, valid,
                        first == null ? "no signature found" : first.ForbiddenChange ?? first.UnexpectedChange ?? $"{first.Status}: {string.Join(" | ", first.Reasons)}"));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // A file the verifier refuses gets no verdict, so it is not Valid.
                    rows.Add(new(fixture, number, mutation.What, false, $"{ex.GetType().Name}: {ex.Message}"));
                }
            }
        }
        return rows;
    }

    static string Report(IEnumerable<Row> rows)
        => string.Join("\n", rows.Select(r => $"{(r.Valid ? "Valid" : "Seen"),-5} | {r.Fixture,-20} | object {r.Object,-3} | {r.What} | {r.Detail}"));

    static string Key(Row row) => $"{row.Fixture} | {row.Object} | {row.What}";

    // Changes that are not seen in 7.10.1, a documented limit (docs/support-policy.md): values that FreePDF sets when
    // it opens a file, so that both revisions have the same one. A change that is seen later must leave this list, and
    // no other change may join it.
    static readonly HashSet<string> KnownUnseen = new(new (string Fixture, int Object, string[] What)[]
    {
        // The /Parent of a page. (Its place in the page tree, through /Kids, is compared.)
        ("simple page", 3, ["/Parent 2 -> 3", "/Parent 0 -> 1"]),
        ("shared content", 3, ["/Parent 2 -> 3", "/Parent 0 -> 1"]),
        ("shared content", 4, ["/Parent 2 -> 3", "/Parent 0 -> 1"]),
        ("shared content array", 3, ["/Parent 2 -> 3", "/Parent 0 -> 1"]),
        ("shared content array", 4, ["/Parent 2 -> 3", "/Parent 0 -> 1"]),
        ("semantic", 3, ["/Parent 2 -> 3", "/Parent 0 -> 1"]),
        ("kinds", 3, ["/Parent 2 -> 3", "/Parent 0 -> 1"]),
        ("kinds", 4, ["/Parent 2 -> 3", "/Parent 0 -> 1"]),
        // The pointers of bookmarks that lead back or to the last one: /Last, /Parent and /Prev. (/First, /Next and
        // /Count are compared.) And a colour component above 1, which is read as 1.
        ("kinds", 10, ["/Last 12 -> 13", "/Last 0 -> 1"]),
        ("kinds", 11, ["/Parent 10 -> 11", "/Parent 0 -> 1", "/Last 13 -> 14", "/Last 0 -> 1", "/C 1 -> 2"]),
        ("kinds", 12, ["/Parent 10 -> 11", "/Parent 0 -> 1", "/Prev 11 -> 12", "/Prev 0 -> 1"]),
        ("kinds", 13, ["/Parent 11 -> 12", "/Parent 0 -> 1"]),
        // The /SigFlags of a form that is an object of its own (the one that signing makes for a file without a form).
        ("simple page", 10, ["/SigFlags 3 -> 4"]),
        ("shared content", 12, ["/SigFlags 3 -> 4"]),
        ("shared content array", 12, ["/SigFlags 3 -> 4"]),
    }.SelectMany(known => known.What.Select(what => $"{known.Fixture} | {known.Object} | {what}")));

    [Fact]
    public async Task A_changed_value_of_a_certified_file_is_seen()
    {
        var rows = new List<Row>();
        foreach (var fixture in ReadFixtures.Plain)
            rows.AddRange(await Run(fixture));
        if (Environment.GetEnvironmentVariable("FREEPDF_MUTATION_REPORT") is { Length: > 0 } path)
            File.WriteAllText(path, Report(rows) + "\n");

        var unseen = rows.Where(r => r.Valid).ToList();
        var newlyUnseen = unseen.Where(r => !KnownUnseen.Contains(Key(r))).ToList();
        var seenNow = KnownUnseen.Where(known => !unseen.Any(r => Key(r) == known)).ToList();
        Assert.True(rows.Count > 500, $"Only {rows.Count} values were changed: the fixtures or the way their values are found changed.");
        Assert.True(newlyUnseen.Count == 0,
            $"The signature stays Valid after a value of the file changed ({newlyUnseen.Count} of {rows.Count}):\n" + Report(newlyUnseen));
        Assert.True(seenNow.Count == 0, "These changes are seen now; take them off KnownUnseen:\n" + string.Join("\n", seenNow));
    }

    static byte[] Certify(byte[] pdf)
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Append);
        DigitalSignatureHandler.ForDocument(document, new PdfSharpDefaultSigner(Certificate.Value, PdfMessageDigestType.SHA256),
            new DigitalSignatureOptions { Rectangle = new XRect(36, 36, 160, 40), Certification = PdfCertificationLevel.NoChanges });
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    static int Size(string text) => int.Parse(Regex.Matches(text, @"/Size\s+(\d+)").Cast<Match>().Last().Groups[1].Value, CultureInfo.InvariantCulture);

    // An incremental update written by hand with one object: the object, a cross-reference section for it, and a
    // trailer that keeps the file's /Root and /Info. Bytes pass through unchanged (one char for each byte).
    static byte[] AppendUpdate(byte[] pdf, string text, params (int Number, string Body)[] objects)
    {
        var startXref = Regex.Matches(text, @"startxref\s+(\d+)").Cast<Match>().Last().Groups[1].Value;
        var root = Regex.Matches(text, @"/Root\s+(\d+\s+\d+\s+R)").Cast<Match>().Last().Groups[1].Value;
        var info = Regex.Matches(text, @"/Info\s+(\d+\s+\d+\s+R)").Cast<Match>().LastOrDefault()?.Groups[1].Value;
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
        int size = Math.Max(Size(text), objects.Max(o => o.Number) + 1);
        update.Append($"trailer\n<< /Size {size} /Root {root}{(info != null ? " /Info " + info : "")} /Prev {startXref} >>\n");
        update.Append($"startxref\n{xrefOffset}\n%%EOF\n");
        return pdf.Concat(update.ToString().Select(c => (byte)c)).ToArray();
    }

    static string Latin1(byte[] bytes) => new(bytes.Select(b => (char)b).ToArray());
}
#endif
