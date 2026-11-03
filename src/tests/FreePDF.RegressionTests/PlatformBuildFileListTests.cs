// The GDI and WPF builds list the core sources one by one. A file added to PdfSharp but not to PdfSharp-gdi.csproj
// and PdfSharp-wpf.csproj broke the Windows CI jobs three times (PdfUnknownAnnotation, the Factur-X manager,
// FieldAppearance). This test finds such a file on any platform before CI does. MIT; see LICENSE.
using System.Text.RegularExpressions;
using Xunit;

namespace FreePDF.RegressionTests;

public class PlatformBuildFileListTests
{
    // Core files the platform builds leave out on purpose: preview controls of the other UI stack, the platform font
    // resolvers, and an enum file that no build compiles.
    static readonly string[] NotInGdi = {
        "Fonts/CoreBuildFontResolver.cs", "Fonts/SystemFontResolver.cs", "Fonts/WindowsPlatformFontResolver.cs",
        "Forms/ColorComboBox.cs", "Forms/DeviceInfos.cs", "Forms/PagePreview.cs", "Forms/PagePreviewCanvas.cs",
        "Forms/enums/RenderMode.cs", "Forms/enums/Zoom.cs",
        "Pdf.Annotations/enums/PdfAnnotationStates.cs",
        "Windows/PagePreview-wpf.xaml.cs", "Windows/PagePreviewDesignTimeData.cs", "Windows/VisualPresenter.cs",
        "Windows/enums/RenderMode.cs", "Windows/enums/Zoom.cs",
    };

    static readonly string[] NotInWpf = {
        "Fonts/CoreBuildFontResolver.cs", "Fonts/SystemFontResolver.cs", "Fonts/WindowsPlatformFontResolver.cs",
        "Forms/ColorComboBox.cs", "Forms/DeviceInfos.cs", "Forms/PagePreview.cs", "Forms/PagePreviewCanvas.cs",
        "Forms/enums/RenderMode.cs", "Forms/enums/Zoom.cs",
        "Pdf.Annotations/enums/PdfAnnotationStates.cs",
        "Windows/PagePreview-wpf.xaml.cs", "Windows/PagePreviewDesignTimeData.cs",
    };

    [Fact]
    public void Every_core_source_file_is_compiled_into_the_GDI_build() => AssertCompiled("PdfSharp-gdi", NotInGdi);

    [Fact]
    public void Every_core_source_file_is_compiled_into_the_WPF_build() => AssertCompiled("PdfSharp-wpf", NotInWpf);

    static void AssertCompiled(string platform, string[] leftOut)
    {
        var root = PdfSharpSourceRoot();
        var compiled = CompiledCoreFiles(Path.Combine(root, platform, platform + ".csproj"));

        var missing = CoreFiles(root).Where(file => !compiled.Contains(file) && !leftOut.Contains(file)).OrderBy(file => file).ToList();

        Assert.True(missing.Count == 0,
            $"Add these core files to {platform}.csproj (<Compile Include=\"..\\PdfSharp\\...\" Link=\"...\" />): " + String.Join(", ", missing));
    }

    static string PdfSharpSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "foundation", "src", "PDFsharp", "src");
            if (File.Exists(Path.Combine(candidate, "PdfSharp", "PdfSharp.csproj")))
                return candidate;
        }
        throw new DirectoryNotFoundException("The PDFsharp sources were not found above the test directory.");
    }

    static IEnumerable<string> CoreFiles(string root)
    {
        var core = Path.Combine(root, "PdfSharp");
        return Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories)
            .Select(file => file.Substring(core.Length + 1).Replace('\\', '/'))
            .Where(file => !file.StartsWith("bin/", StringComparison.Ordinal) && !file.StartsWith("obj/", StringComparison.Ordinal));
    }

    static HashSet<string> CompiledCoreFiles(string projectFile)
    {
        var project = File.ReadAllText(projectFile);
        var files = Regex.Matches(project, @"Include=""\.\.\\PdfSharp\\([^""]+\.cs)""")
            .Cast<Match>()
            .Select(match => match.Groups[1].Value.Replace('\\', '/'));
        return new HashSet<string>(files, StringComparer.Ordinal);
    }
}
