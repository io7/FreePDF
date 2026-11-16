// Original FreePDF implementation. See LICENSE.
using PdfSharp.UniversalAccessibility;

namespace FreePDF.Accessibility;

internal static class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help"))
        {
            Console.WriteLine("freepdf-a11y INPUT.pdf [--format text|json|sarif|html] [--output PATH] [--policy review|automated] [--password-env NAME]");
            Console.WriteLine("Checks a PDF locally without changing it. The default is a plain-English summary.");
            Console.WriteLine("For a full report you can open in a browser: freepdf-a11y INPUT.pdf --format html --output report.html");
            Console.WriteLine("Exit codes: 0 automated policy satisfied (review still required); 1 detected failures; 2 review required; 3 incomplete scan; 64 usage/I/O error.");
            Console.WriteLine("Partial PDF/UA-1 preflight. Does not certify ADA, WCAG or PDF/UA compliance. Reports may contain document text.");
            return args.Length == 0 ? 64 : 0;
        }
        try
        {
            var input = Path.GetFullPath(args[0]);
            var format = "text"; var policy = "review"; string? output = null, password = null;
            for (int i = 1; i < args.Length; i += 2)
            {
                if (i + 1 == args.Length) throw new ArgumentException("Missing option value.");
                switch (args[i])
                {
                    case "--format": format = args[i + 1]; break;
                    case "--output": output = Path.GetFullPath(args[i + 1]); break;
                    case "--policy": policy = args[i + 1]; break;
                    case "--password-env": password = Environment.GetEnvironmentVariable(args[i + 1]) ?? throw new ArgumentException("Password environment variable is not set."); break;
                    default: throw new ArgumentException("Unknown option.");
                }
            }
            if (format is not ("text" or "json" or "sarif" or "html") || policy is not ("review" or "automated")) throw new ArgumentException("Unsupported format or policy.");
            if (output != null && String.Equals(input, output, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new ArgumentException("Report output must differ from the input PDF.");
            var report = PdfAccessibilityScanner.Scan(input, new PdfAccessibilityScanOptions { Password = password });
            var text = AccessibilityReportWriter.Write(report, Path.GetFileName(input), format);
            if (output == null) Console.WriteLine(text);
            else
            {
                // Never follow/overwrite a symlink or existing PDF through a report path.
                using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
                using var writer = new StreamWriter(destination);
                writer.Write(text);
                writer.Flush();
                Console.Error.WriteLine("Report written to " + AccessibilityReportWriter.SafeText(output));
            }
            return report.Status switch
            {
                PdfAccessibilityScanStatus.Incomplete => 3,
                PdfAccessibilityScanStatus.Failed => 1,
                _ => policy == "automated" ? 0 : 2
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine("Cannot scan or write report: " + AccessibilityReportWriter.SafeText(ex.Message));
            return 64;
        }
    }
}
