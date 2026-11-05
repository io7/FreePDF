// MigraDoc documents that threw or never finished, found in the upstream PDFsharp triage of 2026-11-05. The fixtures
// are our own. MIT, see LICENSE.
using Microsoft.Extensions.Logging;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes.Charts;
using MigraDoc.Rendering;
using PdfSharp.Logging;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using Xunit;

namespace FreePDF.RegressionTests;

public class MigraDocLayoutCrashTests
{
    static Document NewDocument()
    {
        TestFonts.Initialize();
        var document = new Document();
        document.Styles[StyleNames.Normal]!.Font.Name = "FreePDF Synthetic";
        return document;
    }

    static int Render(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        var pages = renderer.PdfDocument.PageCount;
        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output);
        renderer.PdfDocument.Dispose();
        return pages;
    }

    // Upstream #250: a row's KeepWith that reaches past the last row threw ArgumentOutOfRangeException.
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 1)]
    [InlineData(0, 100)]
    public void A_keep_with_past_the_last_row_stops_at_the_table_end(int row, int keepWith)
    {
        var document = NewDocument();
        var table = document.AddSection().AddTable();
        table.AddColumn(Unit.FromCentimeter(4));
        for (var index = 0; index < 3; index++)
            table.AddRow().Cells[0].AddParagraph($"Row {index}");
        table.Rows[0].HeadingFormat = row == 0;
        table.Rows[row].KeepWith = keepWith;

        Assert.Equal(1, Render(document));
    }

    // Upstream #83: an empty paragraph with a bottom border threw "'Rectangle' must not be null here" when its line
    // still fitted at the bottom of a page but its border did not. The spacer moves it down a quarter point at a time
    // across the last two lines of the page.
    [Fact]
    public void An_empty_paragraph_with_a_bottom_border_at_the_page_bottom_renders()
    {
        var failures = new List<string>();
        for (var spacer = 650.0; spacer < 700.0; spacer += 0.25)
        {
            var document = NewDocument();
            var section = document.AddSection();
            section.AddParagraph("Top");
            section.AddParagraph().Format.SpaceBefore = Unit.FromPoint(spacer);
            section.AddParagraph().Format.Borders.Bottom.Width = 1;
            try
            {
                Assert.InRange(Render(document), 1, 2);
            }
            catch (Exception ex)
            {
                failures.Add($"{spacer}: {ex.Message}");
            }
        }
        Assert.Empty(failures);
    }

    sealed class CollectingLoggerFactory : ILoggerFactory, ILogger
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Messages = new();
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => this;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Enqueue($"{logLevel}: {formatter(state, exception)}");
    }

    // Upstream #94: a row taller than a page is cut off at the page bottom. Splitting rows is a larger feature; until
    // then the loss is logged as a warning instead of passing silently.
    [Theory]
    [InlineData(120, true)]
    [InlineData(10, false)]
    public void A_row_taller_than_a_page_logs_a_warning(int lines, bool expectWarning)
    {
        var document = NewDocument();
        var table = document.AddSection().AddTable();
        table.AddColumn(Unit.FromCentimeter(8));
        table.AddRow().Cells[0].AddParagraph("Before");
        var paragraph = table.AddRow().Cells[0].AddParagraph();
        for (var index = 0; index < lines; index++)
        {
            paragraph.AddText($"line {index}");
            paragraph.AddLineBreak();
        }
        var logs = new CollectingLoggerFactory();
        LogHost.Factory = logs;
        try
        {
            Render(document);
        }
        finally
        {
            LogHost.ResetLogging();
        }

        var warnings = logs.Messages.Where(message => message.Contains("Table rows 1 to 1")).ToArray();
        if (expectWarning)
            Assert.Contains(warnings, message => message.StartsWith("Warning: ") && message.Contains("cut off"));
        else
            Assert.Empty(warnings);
    }

    // Upstream #339: a justified list paragraph with soft hyphens never finished laying out at many left indents.
    [Fact]
    public async Task Justified_list_paragraphs_with_soft_hyphens_finish()
    {
        const string shy = "­";
        var text = $"This text shows the issue with the Donau{shy}dampf{shy}schiff{shy}fahrts{shy}gesell{shy}schafts{shy}" +
                   $"kapitaens{shy}muetzen{shy}band of MigraDoc, which should be hyphen{shy}ated correctly. The text should be " +
                   $"justified and the hyphen{shy}ation should work as expected.";
        var render = Task.Run(() =>
        {
            for (var indent = 10; indent <= 60; indent++)
            {
                var document = NewDocument();
                var paragraph = document.AddSection().AddParagraph(text);
                paragraph.Format.ListInfo.ListType = ListType.BulletList1;
                paragraph.Format.LeftIndent = Unit.FromMillimeter(indent);
                paragraph.Format.FirstLineIndent = Unit.FromMillimeter(-5);
                paragraph.Format.Alignment = ParagraphAlignment.Justify;
                Assert.Equal(1, Render(document));
            }
        });
        Assert.Same(render, await Task.WhenAny(render, Task.Delay(TimeSpan.FromSeconds(30))));
        await render;
    }

    // Found in the triage: a chart threw "'Axis' must not be null here" unless the code had read chart.YAxis, and
    // without chart.XAxis its columns or line were drawn at NaN coordinates, which viewers reject.
    [Theory]
    [InlineData(ChartType.Column2D)]
    [InlineData(ChartType.ColumnStacked2D)]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.Area2D)]
    [InlineData(ChartType.Bar2D)]
    [InlineData(ChartType.BarStacked2D)]
    public void A_chart_renders_without_touching_its_axes(ChartType type)
    {
        var document = NewDocument();
        var chart = document.AddSection().AddChart(type);
        chart.Width = Unit.FromCentimeter(12);
        chart.Height = Unit.FromCentimeter(8);
        chart.SeriesCollection.AddSeries().Add(1, 4, 2, 5);
        chart.XValues.AddXSeries().Add("A", "B", "C", "D");

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        Assert.Equal(1, pdf.PageCount);
        var operators = ContentReader.ReadContent(pdf.Pages[0]).OfType<COperator>().ToArray();
        Assert.Contains(operators, op => op.Name is "re" or "l");
        Assert.DoesNotContain(operators, op => op.Name == "NaN");
        Assert.All(operators.SelectMany(op => op.Operands).OfType<CReal>(), real => Assert.False(double.IsNaN(real.Value) || double.IsInfinity(real.Value)));
    }
}
