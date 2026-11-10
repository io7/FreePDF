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
    public void Every_core_source_file_is_compiled_into_the_GDI_build() => AssertCompiled(PdfSharpSourceRoot(), "PdfSharp", "PdfSharp-gdi", NotInGdi);

    [Fact]
    public void Every_core_source_file_is_compiled_into_the_WPF_build() => AssertCompiled(PdfSharpSourceRoot(), "PdfSharp", "PdfSharp-wpf", NotInWpf);

    // The bar code and MigraDoc rendering projects list their sources the same way (7.9.0 added files to both).
    [Theory]
    [InlineData("PdfSharp.BarCodes", "PdfSharp.BarCodes-gdi")]
    [InlineData("PdfSharp.BarCodes", "PdfSharp.BarCodes-wpf")]
    [InlineData("MigraDoc.Rendering", "MigraDoc.Rendering-gdi")]
    [InlineData("MigraDoc.Rendering", "MigraDoc.Rendering-wpf")]
    public void Every_source_file_is_compiled_into_the_platform_builds(string core, string platform)
    {
        var root = core.StartsWith("MigraDoc", StringComparison.Ordinal)
            ? Path.GetFullPath(Path.Combine(PdfSharpSourceRoot(), "..", "..", "MigraDoc", "src"))
            : PdfSharpSourceRoot();
        AssertCompiled(root, core, platform, []);
    }

    static void AssertCompiled(string root, string core, string platform, string[] leftOut)
    {
        var compiled = CompiledCoreFiles(Path.Combine(root, platform, platform + ".csproj"), core);

        var missing = CoreFiles(root, core).Where(file => !compiled.Contains(file) && !leftOut.Contains(file)).OrderBy(file => file).ToList();

        Assert.True(missing.Count == 0,
            $"Add these {core} files to {platform}.csproj (<Compile Include=\"..\\{core}\\...\" Link=\"...\" />): " + String.Join(", ", missing));
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

    static IEnumerable<string> CoreFiles(string root, string coreProject)
    {
        var core = Path.Combine(root, coreProject);
        return Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories)
            .Select(file => file.Substring(core.Length + 1).Replace('\\', '/'))
            .Where(file => !file.StartsWith("bin/", StringComparison.Ordinal) && !file.StartsWith("obj/", StringComparison.Ordinal));
    }

    static HashSet<string> CompiledCoreFiles(string projectFile, string coreProject)
    {
        var project = File.ReadAllText(projectFile);
        var files = Regex.Matches(project, @"Include=""\.\.\\" + Regex.Escape(coreProject) + @"\\([^""]+\.cs)""")
            .Cast<Match>()
            .Select(match => match.Groups[1].Value.Replace('\\', '/'));
        return new HashSet<string>(files, StringComparer.Ordinal);
    }
}
