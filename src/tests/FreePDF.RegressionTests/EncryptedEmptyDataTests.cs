// Empty strings and streams in an encrypted file. AES turns empty data into an initialization vector and a padding
// block, 32 bytes (ISO 32000-1, 7.6.2); FreePDF wrote it empty and unencrypted, which pyHanko cannot read
// ("Invalid IV size (0)"). RC4 leaves empty data empty. Original fixtures; MIT, see LICENSE.
using System.Globalization;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class EncryptedEmptyDataTests
{
    public static TheoryData<string, int> Kinds() => new()
    {
        { "RC4-128", 0 },
        { "AES-128", 32 },
        { "AES-256", 32 },
    };

    static void Encrypt(PdfDocument document, string kind)
    {
        switch (kind)
        {
            case "RC4-128": document.SecurityHandler.SetEncryptionToV2With128Bits(); break;
            case "AES-128": document.SecurityHandler.SetEncryptionToV4UsingAES(); break;
            case "AES-256": document.SecurityHandler.SetEncryptionToV5(); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        document.SecuritySettings.UserPassword = "user";
        document.SecuritySettings.OwnerPassword = "owner";
    }

    // An empty stream and an empty literal and hexadecimal string, referenced from the catalog.
    static void AddEmptyData(PdfDocument document)
    {
        var empty = new PdfDictionary(document);
        empty.CreateStream([]);
        document.Internals.AddObject(empty);
        document.Catalog.Elements.SetObject("/EmptyStream", empty);
        document.Catalog.Elements["/EmptyLiteral"] = new PdfString("");
        document.Catalog.Elements["/EmptyHex"] = new PdfString("", true);
    }

    static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    static byte[] NewDocument(string? kind, bool emptyData)
    {
        using var document = new PdfDocument();
        document.AddPage();
        if (emptyData)
            AddEmptyData(document);
        if (kind != null)
            Encrypt(document, kind);
        return Save(document);
    }

    // The raw lengths of the empty stream and strings as written in the file.
    static (int Stream, int Literal, int Hex) RawLengths(string text)
    {
        var number = Regex.Matches(text, @"/EmptyStream\s*(\d+) 0 R").Cast<Match>().Last().Groups[1].Value;
        var objectAt = Regex.Matches(text, @"(?<!\d)" + number + @" 0 obj\b").Cast<Match>().Last().Index;
        var streamAt = text.IndexOf("stream", objectAt, StringComparison.Ordinal);
        var length = int.Parse(Regex.Match(text.Substring(objectAt, streamAt - objectAt), @"/Length\s+(\d+)").Groups[1].Value,
            CultureInfo.InvariantCulture);
        var dataAt = streamAt + "stream".Length + (text[streamAt + "stream".Length] == '\r' ? 2 : 1);
        Assert.Equal("endstream", text.Substring(dataAt + length).TrimStart('\r', '\n').Substring(0, "endstream".Length));

        var hex = Regex.Matches(text, @"/EmptyHex\s*<([0-9A-Fa-f]*)>").Cast<Match>().Last().Groups[1].Value;
        var literalAt = Regex.Matches(text, @"/EmptyLiteral\s*\(").Cast<Match>().Last();
        return (length, LiteralLength(text, literalAt.Index + literalAt.Length), hex.Length / 2);
    }

    // The number of bytes in a literal string that starts after its opening parenthesis.
    static int LiteralLength(string text, int index)
    {
        var count = 0;
        for (var depth = 0; ; index++)
        {
            var character = text[index];
            if (character == '\\')
            {
                index++;
                // An octal escape has up to three digits.
                for (var digits = 1; digits < 3 && text[index] is >= '0' and <= '7' && text[index + 1] is >= '0' and <= '7'; digits++)
                    index++;
            }
            else if (character == '(')
            {
                depth++;
            }
            else if (character == ')' && depth-- == 0)
            {
                return count;
            }
            count++;
        }
    }

    static void AssertReadsBackEmpty(byte[] file)
    {
        using var document = PdfReader.Open(new MemoryStream(file), "user", PdfDocumentOpenMode.Import);
        Assert.Empty(document.Catalog.Elements.GetDictionary("/EmptyStream")!.Stream!.Value);
        Assert.Equal("", document.Catalog.Elements.GetString("/EmptyLiteral"));
        Assert.Equal("", document.Catalog.Elements.GetString("/EmptyHex"));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Empty_data_in_a_new_encrypted_document_is_encrypted(string kind, int encryptedLength)
    {
        var file = NewDocument(kind, true);

        Assert.Equal((encryptedLength, encryptedLength, encryptedLength), RawLengths(TestPdf.Latin1.GetString(file)));
        AssertReadsBackEmpty(file);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Empty_data_in_an_encrypted_append_update_is_encrypted(string kind, int encryptedLength)
    {
        var original = NewDocument(kind, false);

        byte[] result;
        using (var document = PdfReader.Open(new MemoryStream(original), "owner", PdfDocumentOpenMode.Append))
        {
            AddEmptyData(document);
            result = Save(document);
        }

        Assert.Equal(original, result.Take(original.Length).ToArray());
        var update = TestPdf.Latin1.GetString(result, original.Length, result.Length - original.Length);
        Assert.Equal((encryptedLength, encryptedLength, encryptedLength), RawLengths(update));
        AssertReadsBackEmpty(result);
    }

    [Fact]
    public void Empty_data_in_an_unencrypted_document_stays_empty()
    {
        var text = TestPdf.Latin1.GetString(NewDocument(null, true));

        Assert.Equal((0, 0, 0), RawLengths(text));
        Assert.Matches(@"/EmptyLiteral\s*\(\)", text);
        Assert.Matches(@"/EmptyHex\s*<>", text);
    }

    // A file another writer saved with empty AES data still opens, with the data read as empty.
    [Fact]
    public void Empty_AES_data_from_another_writer_reads_as_empty()
    {
        var file = NewDocument("AES-256", true);
        var text = TestPdf.Latin1.GetString(file);
        var hex = Regex.Match(text, @"/EmptyHex\s*<[0-9A-Fa-f]*>");
        text = text.Substring(0, hex.Index) + "/EmptyHex<" + new string(' ', hex.Length - "/EmptyHex<>".Length) + ">" +
            text.Substring(hex.Index + hex.Length);

        using var document = PdfReader.Open(new MemoryStream(TestPdf.Latin1.GetBytes(text)), "user", PdfDocumentOpenMode.Import);
        Assert.Equal("", document.Catalog.Elements.GetString("/EmptyHex"));
    }
}
