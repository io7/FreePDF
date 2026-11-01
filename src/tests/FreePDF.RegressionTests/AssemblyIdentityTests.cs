// Original tests: FreePDF assemblies must identify as io7's FreePDF, never as empira's PDFsharp.
// FreePDF 7.0.0 shipped with empira's company/product names and strong-name key, which made its
// assemblies indistinguishable from empira's PDFsharp 7.0.0 for loaders and licence scanners.
using System.Reflection;
using MigraDoc.Rendering;
using PdfSharp.Pdf;
using Xunit;

namespace FreePDF.RegressionTests;

public class AssemblyIdentityTests
{
    const string Io7PublicKeyToken = "77af05c50284aa70";
    const string EmpiraPublicKeyToken = "f94615aa0424f9eb";

    // The shipped assemblies reachable from the public entry points, plus their PdfSharp/MigraDoc dependencies.
    static List<Assembly> FreePdfAssemblies()
    {
        var found = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>([typeof(PdfDocument).Assembly, typeof(PdfDocumentRenderer).Assembly, Assembly.Load("PdfSharp.Cryptography")]);
        while (pending.Count > 0)
        {
            var assembly = pending.Dequeue();
            var name = assembly.GetName().Name!;
            if (found.ContainsKey(name))
                continue;
            found.Add(name, assembly);
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name!.StartsWith("PdfSharp", StringComparison.Ordinal) || reference.Name.StartsWith("MigraDoc", StringComparison.Ordinal))
                    pending.Enqueue(Assembly.Load(reference));
            }
        }
        return found.Values.ToList();
    }

    [Fact]
    public void Core_and_layout_assemblies_are_covered()
    {
        var names = FreePdfAssemblies().Select(a => a.GetName().Name).ToList();
        Assert.Contains("PdfSharp", names);
        Assert.Contains("PdfSharp.Shared", names);
        Assert.Contains("PdfSharp.Cryptography", names);
        Assert.Contains("MigraDoc.DocumentObjectModel", names);
        Assert.Contains("MigraDoc.Rendering", names);
    }

    [Fact]
    public void Assemblies_carry_io7_identity_and_keep_empira_attribution()
    {
        foreach (var assembly in FreePdfAssemblies())
        {
            var name = assembly.GetName().Name;
            var token = BitConverter.ToString(assembly.GetName().GetPublicKeyToken() ?? []).Replace("-", "").ToLowerInvariant();
            Assert.NotEqual(EmpiraPublicKeyToken, token);
            Assert.True(token == Io7PublicKeyToken, $"{name} has public key token '{token}', expected {Io7PublicKeyToken}.");
            Assert.Equal("io7", assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company);
            Assert.Equal("FreePDF", assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
            Assert.Contains("empira Software GmbH", assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright);
        }
    }
}
