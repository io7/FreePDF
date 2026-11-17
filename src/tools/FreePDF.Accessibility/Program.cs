// Original FreePDF implementation. See LICENSE.
using PdfSharp.UniversalAccessibility;
using System.Globalization;

namespace FreePDF.Accessibility;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help"))
        {
            Console.WriteLine("freepdf-a11y INPUT.pdf [--format text|json|sarif|html] [--output PATH] [--policy review|automated] [--password-env NAME]");
            Console.WriteLine("Checks a PDF locally without changing it. The default is a plain-English summary.");
            Console.WriteLine("For a full report you can open in a browser: freepdf-a11y INPUT.pdf --format html --output report.html");
            Console.WriteLine("Optional independent validation: --verapdf PATH [--verapdf-profile ua1] [--verapdf-timeout SECONDS]");
            Console.WriteLine("List all Matterhorn requirements without scanning: freepdf-a11y --requirements [--format text|json] [--output PATH]");
            Console.WriteLine("Exit codes: 0 automated policy satisfied (review still required); 1 detected failures, also in an incomplete scan; 2 review required; 3 incomplete scan without detected failures; 64 usage/I/O error.");
            Console.WriteLine("Partial PDF/UA-1 preflight. Does not certify ADA, WCAG or PDF/UA compliance. Reports may contain document text.");
            return args.Length == 0 ? 64 : 0;
        }
        try
        {
            bool inventory = args[0] == "--requirements";
            var input = inventory ? null : Path.GetFullPath(args[0]);
            var format = "text"; var policy = "review"; string? output = null, password = null;
            string? validator = null; string validatorProfile = "ua1"; int validatorTimeout = 60;
            var supplied = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i += 2)
            {
                if (i + 1 == args.Length) throw new ArgumentException("Missing option value.");
                if (!supplied.Add(args[i])) throw new ArgumentException("Duplicate option.");
                switch (args[i])
                {
                    case "--format": format = args[i + 1]; break;
                    case "--output": output = Path.GetFullPath(args[i + 1]); break;
                    case "--policy": policy = args[i + 1]; break;
                    case "--password-env": password = Environment.GetEnvironmentVariable(args[i + 1]) ?? throw new ArgumentException("Password environment variable is not set."); break;
                    case "--verapdf": validator = Path.GetFullPath(args[i + 1]); break;
                    case "--verapdf-profile": validatorProfile = args[i + 1]; break;
                    case "--verapdf-timeout":
                        if (!int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out validatorTimeout) || validatorTimeout is < 1 or > 600)
                            throw new ArgumentException("Validator timeout must be between 1 and 600 seconds.");
                        break;
                    default: throw new ArgumentException("Unknown option.");
                }
            }
            if (format is not ("text" or "json" or "sarif" or "html") || policy is not ("review" or "automated")) throw new ArgumentException("Unsupported format or policy.");
            if (inventory)
            {
                if (supplied.Any(o => o is not ("--format" or "--output"))) throw new ArgumentException("Inventory mode accepts only --format and --output.");
                WriteOutput(RequirementInventory.Write(format), output);
                return 0;
            }
            if (validator == null && (supplied.Contains("--verapdf-profile") || supplied.Contains("--verapdf-timeout")))
                throw new ArgumentException("Validator options require --verapdf PATH.");
            if (output != null && String.Equals(input, output, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new ArgumentException("Report output must differ from the input PDF.");
            var options = new PdfAccessibilityScanOptions { Password = password };
            PdfAccessibilityReport report;
            IndependentValidation? independent = null;
            if (validator == null) report = PdfAccessibilityScanner.Scan(input!, options);
            else
            {
                try
                {
                    using var snapshot = new ScanSnapshot(input!, options.MaxFileBytes);
                    report = PdfAccessibilityScanner.Scan(snapshot.Path, options);
                    independent = await VeraPdfAdapter.ValidateAsync(validator, snapshot.Path, report.DocumentSha256,
                        validatorProfile, TimeSpan.FromSeconds(validatorTimeout));
                }
                catch (InvalidDataException)
                {
                    report = PdfAccessibilityScanner.Scan(input!, options);
                    independent = new IndependentValidation { RequestedProfile = validatorProfile, DocumentSha256 = report.DocumentSha256,
                        IncompleteReason = "Input exceeds the independent validation snapshot limit." };
                }
            }
            var text = independent == null ? AccessibilityReportWriter.Write(report, Path.GetFileName(input!), format)
                : IndependentReportWriter.Write(report, independent, Path.GetFileName(input!), format);
            WriteOutput(text, output);
            // A detected failure is definite even when other content could not be checked.
            if (IndependentValidation.HasFailures(report, independent)) return 1;
            if (IndependentValidation.Combine(report, independent) == PdfAccessibilityScanStatus.Incomplete) return 3;
            return policy == "automated" ? 0 : 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine("Cannot scan or write report: " + AccessibilityReportWriter.SafeText(ex.Message));
            return 64;
        }
    }

    static void WriteOutput(string text, string? output)
    {
        if (output == null) Console.WriteLine(text);
        else
        {
            // CreateNew refuses existing files, symlinks and hard links.
            using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(destination);
            writer.Write(text);
            writer.Flush();
            Console.Error.WriteLine("Report written to " + AccessibilityReportWriter.SafeText(output));
        }
    }
}
