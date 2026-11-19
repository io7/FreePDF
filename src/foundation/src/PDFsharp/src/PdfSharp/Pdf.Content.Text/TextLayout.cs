// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// Joins text runs into page text. Lines are told apart by their baselines, words by the gaps between runs.
    /// </summary>
    static class TextLayout
    {
        // A gap wider than this fraction of the font size between two runs on one line is a word space.
        const double SpaceGap = 0.15;
        // Baselines closer than this fraction of the font size are one line (sub- and superscripts stay on it).
        const double LineTolerance = 0.5;

        internal static string Compose(IReadOnlyList<PdfTextRun> runs, PdfTextOrder order)
        {
            if (order == PdfTextOrder.Content)
                return Join(runs, newLines: true);
            var lines = new List<List<PdfTextRun>>();
            List<PdfTextRun>? line = null;
            double lineY = 0, lineSize = 0;
            foreach (var run in runs.OrderByDescending(r => r.Y).ThenBy(r => r.X))
            {
                if (line == null || Math.Abs(run.Y - lineY) > LineTolerance * Size(lineSize, run.FontSize))
                {
                    line = new List<PdfTextRun>();
                    lines.Add(line);
                    lineY = run.Y;
                    lineSize = run.FontSize;
                }
                line.Add(run);
            }
            return String.Join("\n", lines.Select(l => Join(l.OrderBy(r => r.X).ToList(), newLines: false)));
        }

        static double Size(double a, double b) => Math.Max(Math.Min(a, b), 1);

        static string Join(IReadOnlyList<PdfTextRun> runs, bool newLines)
        {
            var text = new StringBuilder();
            PdfTextRun? previous = null;
            foreach (var run in runs)
            {
                if (previous != null)
                {
                    double size = Size(previous.FontSize, run.FontSize);
                    double gap = run.X - previous.EndX;
                    if (newLines && Math.Abs(run.Y - previous.Y) > LineTolerance * size)
                        text.Append('\n');
                    else if ((gap > SpaceGap * size || gap < -size) && !EndsWithSpace(text) && !run.Text.StartsWith(" ", StringComparison.Ordinal))
                        text.Append(' ');
                }
                text.Append(run.Text);
                previous = run;
            }
            return text.ToString();
        }

        static bool EndsWithSpace(StringBuilder text) => text.Length > 0 && text[text.Length - 1] == ' ';
    }
}
