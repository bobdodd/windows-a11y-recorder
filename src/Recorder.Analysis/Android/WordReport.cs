using System.Buffers.Binary;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Recorder.Analysis.Android;

/// <summary>
/// A report's Markdown as a Word document (android-report.md, "The Word
/// document"): each block the reading view reads, in the same order, with
/// Word's heading styles, tables whose header row repeats on each page,
/// pictures with their descriptions as alternative text, web addresses as
/// links, the title and language in the document's properties, on Letter
/// paper with one inch margins.
/// </summary>
public static partial class WordReport
{
    private const long EmuPerInch = 914400;

    [GeneratedRegex(@"https?://[^\s)\]]+")]
    private static partial Regex UrlPattern();

    /// <summary>The Word document's bytes.</summary>
    /// <param name="folder">The report's folder, which the pictures' paths are relative to.</param>
    /// <param name="boldLabels">Labels that open a paragraph and are set in bold.</param>
    public static byte[] Write(string markdown, string folder, IReadOnlyList<string> boldLabels)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(boldLabels);
        var report = ReportDocument.Parse(markdown);
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            document.PackageProperties.Title = report.Title ?? "Android accessibility report";
            document.PackageProperties.Language = "en-CA";
            document.PackageProperties.Creator = "Windows A11y Recorder";
            var main = document.AddMainDocumentPart();
            AddStyles(main);
            AddNumbering(main);
            var body = new Body();
            main.Document = new Document(body);
            var writer = new BodyWriter(main, body, folder, boldLabels);
            foreach (var block in report.Blocks)
            {
                writer.Add(block);
            }

            body.Append(new SectionProperties(
                new PageSize { Width = 12240U, Height = 15840U },
                new PageMargin { Top = 1440, Bottom = 1440, Left = 1440U, Right = 1440U, Header = 720U, Footer = 720U, Gutter = 0U }));
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private sealed class BodyWriter(MainDocumentPart main, Body body, string folder, IReadOnlyList<string> boldLabels)
    {
        private uint _pictures;

        public void Add(ReportBlock block)
        {
            switch (block.Kind)
            {
                case ReportBlockKind.Heading:
                    body.Append(Paragraph("Heading" + Math.Clamp(block.Level, 1, 6).ToString(System.Globalization.CultureInfo.InvariantCulture), Runs(block.Text)));
                    break;
                case ReportBlockKind.ListItem:
                    body.Append(new Paragraph(new OpenXmlElement[]
                    {
                        new ParagraphProperties(new ParagraphStyleId { Val = "ListParagraph" }, new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 })),
                    }.Concat(Runs(block.Text))));
                    break;
                case ReportBlockKind.NumberedItem:
                    body.Append(Paragraph("Normal", Runs($"{block.Level}. {block.Text}")));
                    break;
                case ReportBlockKind.Quote:
                    body.Append(Paragraph("Quote", Runs(block.Text)));
                    break;
                case ReportBlockKind.Table:
                    body.Append(Table(block.Rows));
                    body.Append(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "0" })));
                    break;
                case ReportBlockKind.Image:
                    body.Append(Picture(block.Text, block.Source ?? ""));
                    break;
                default:
                    body.Append(Paragraph("Normal", Labelled(block.Text.TrimStart('\u200B'))));
                    break;
            }
        }

        private static Paragraph Paragraph(string style, IEnumerable<OpenXmlElement> runs) =>
            new(new OpenXmlElement[] { new ParagraphProperties(new ParagraphStyleId { Val = style }) }.Concat(runs));

        private IEnumerable<OpenXmlElement> Labelled(string text)
        {
            var label = boldLabels.FirstOrDefault(candidate => text.StartsWith(candidate, StringComparison.Ordinal));
            if (label is null)
            {
                return Runs(text);
            }

            return new OpenXmlElement[] { new Run(new RunProperties(new Bold()), Text(label)) }.Concat(Runs(text[label.Length..]));
        }

        // Text as runs, with each web address a link to itself.
        private List<OpenXmlElement> Runs(string text, bool bold = false)
        {
            var runs = new List<OpenXmlElement>();
            var at = 0;
            foreach (Match match in UrlPattern().Matches(text))
            {
                if (match.Index > at)
                {
                    runs.Add(Run(text[at..match.Index], bold));
                }

                var url = match.Value.TrimEnd('.', ',', ';', ':');
                var relationship = main.AddHyperlinkRelationship(new Uri(url, UriKind.Absolute), true);
                runs.Add(new Hyperlink(new Run(new RunProperties(new RunStyle { Val = "Hyperlink" }), Text(url))) { Id = relationship.Id });
                at = match.Index + url.Length;
            }

            if (at < text.Length || runs.Count == 0)
            {
                runs.Add(Run(text[at..], bold));
            }

            return runs;
        }

        private static Run Run(string text, bool bold) =>
            bold ? new Run(new RunProperties(new Bold()), Text(text)) : new Run(Text(text));

        private static Text Text(string text) => new(text) { Space = SpaceProcessingModeValues.Preserve };

        private Table Table(IReadOnlyList<IReadOnlyList<string>> rows)
        {
            var columns = rows.Count == 0 ? 1 : rows.Max(row => row.Count);
            var table = new Table(new TableProperties(
                new TableStyle { Val = "ReportTable" },
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableLook { Val = "04A0", FirstRow = true, LastRow = false, FirstColumn = false, LastColumn = false, NoHorizontalBand = true, NoVerticalBand = true }));
            // Each column's width in fiftieths of a percent, shared by the
            // length of its longest text, counted from 6 to 60 characters,
            // so that short columns such as a reference or a time are narrow
            // and long text wraps in the widest.
            var weights = Enumerable.Range(0, columns)
                .Select(column => Math.Clamp(rows.Max(row => column < row.Count ? row[column].Length : 0), 6, 60))
                .ToList();
            var widths = weights.Select(weight => 5000 * weight / weights.Sum()).ToList();
            widths[^1] += 5000 - widths.Sum();
            var grid = new TableGrid();
            for (var column = 0; column < columns; column++)
            {
                grid.Append(new GridColumn { Width = (9360 * widths[column] / 5000).ToString(System.Globalization.CultureInfo.InvariantCulture) });
            }

            table.Append(grid);
            for (var index = 0; index < rows.Count; index++)
            {
                var header = index == 0;
                var row = new TableRow();
                var properties = new TableRowProperties(new CantSplit());
                if (header)
                {
                    properties.Append(new TableHeader());
                }

                row.Append(properties);
                for (var column = 0; column < columns; column++)
                {
                    var text = column < rows[index].Count ? rows[index][column] : "";
                    row.Append(new TableCell(
                        new TableCellProperties(new TableCellWidth { Width = widths[column].ToString(System.Globalization.CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Pct }),
                        new Paragraph(new OpenXmlElement[] { new ParagraphProperties(new SpacingBetweenLines { After = "0" }) }.Concat(Runs(text, header)))));
                }

                table.Append(row);
            }

            return table;
        }

        private Paragraph Picture(string description, string source)
        {
            var path = Path.GetFullPath(Path.Combine(folder, source.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(path))
            {
                return Paragraph("Normal", Runs($"The picture {source} is missing. It showed: {description}"));
            }

            var bytes = File.ReadAllBytes(path);
            var (width, height) = PngSize(bytes);
            var part = main.AddImagePart(ImagePartType.Png);
            using (var data = new MemoryStream(bytes))
            {
                part.FeedData(data);
            }

            // A picture taller than wide, a phone's screen, is shown at most
            // 3 inches wide; a zoomed crop at most 6; neither more than 5.5
            // inches high.
            var maxWidth = height > width ? 3.0 : 6.0;
            var scale = Math.Min(maxWidth / Math.Max(1, width), 5.5 / Math.Max(1, height));
            var cx = (long)(width * scale * EmuPerInch);
            var cy = (long)(height * scale * EmuPerInch);
            var id = ++_pictures;
            var name = Path.GetFileName(path);
            var inline = new DW.Inline(
                new DW.Extent { Cx = cx, Cy = cy },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = id, Name = name, Description = description },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(new A.GraphicData(
                    new PIC.Picture(
                        new PIC.NonVisualPictureProperties(
                            new PIC.NonVisualDrawingProperties { Id = id, Name = name, Description = description },
                            new PIC.NonVisualPictureDrawingProperties()),
                        new PIC.BlipFill(new A.Blip { Embed = main.GetIdOfPart(part) }, new A.Stretch(new A.FillRectangle())),
                        new PIC.ShapeProperties(
                            new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = cx, Cy = cy }),
                            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U,
            };
            return new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Normal" }, new KeepNext()), new Run(new Drawing(inline)));
        }

        private static (int Width, int Height) PngSize(byte[] png) =>
            png.Length >= 24 ? (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20))) : (1, 1);
    }

    private static void AddStyles(MainDocumentPart main)
    {
        var part = main.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", ComplexScript = "Calibri", EastAsia = "Calibri" },
                    new FontSize { Val = "22" },
                    new FontSizeComplexScript { Val = "22" },
                    new Languages { Val = "en-CA" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(new SpacingBetweenLines { After = "160", Line = "264", LineRule = LineSpacingRuleValues.Auto }))),
            new Style(new StyleName { Val = "Normal" }, new PrimaryStyle()) { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true });
        var sizes = new[] { "36", "30", "26", "24", "22", "22" };
        for (var level = 1; level <= 6; level++)
        {
            styles.Append(new Style(
                new StyleName { Val = "heading " + level.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                new BasedOn { Val = "Normal" },
                new NextParagraphStyle { Val = "Normal" },
                new PrimaryStyle(),
                new StyleParagraphProperties(
                    new KeepNext(),
                    new KeepLines(),
                    new SpacingBetweenLines { Before = level == 1 ? "240" : "200", After = "80" },
                    new OutlineLevel { Val = level - 1 }),
                new StyleRunProperties(
                    new Bold(),
                    new Color { Val = "1F3864" },
                    new FontSize { Val = sizes[level - 1] },
                    new FontSizeComplexScript { Val = sizes[level - 1] }))
            {
                Type = StyleValues.Paragraph,
                StyleId = "Heading" + level.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }

        styles.Append(new Style(
            new StyleName { Val = "List Paragraph" },
            new BasedOn { Val = "Normal" },
            new StyleParagraphProperties(new SpacingBetweenLines { After = "60" }, new Indentation { Left = "720" }))
        { Type = StyleValues.Paragraph, StyleId = "ListParagraph" });
        styles.Append(new Style(
            new StyleName { Val = "Quote" },
            new BasedOn { Val = "Normal" },
            new StyleParagraphProperties(new Indentation { Left = "720", Right = "720" }))
        { Type = StyleValues.Paragraph, StyleId = "Quote" });
        styles.Append(new Style(
            new StyleName { Val = "Hyperlink" },
            new StyleRunProperties(new Color { Val = "0563C1" }, new Underline { Val = UnderlineValues.Single }))
        { Type = StyleValues.Character, StyleId = "Hyperlink" });
        styles.Append(new Style(
            new StyleName { Val = "Normal Table" },
            new UIPriority { Val = 99 },
            new StyleTableProperties(new TableCellMarginDefault(
                new TopMargin { Width = "0", Type = TableWidthUnitValues.Dxa },
                new TableCellLeftMargin { Width = 108, Type = TableWidthValues.Dxa },
                new BottomMargin { Width = "0", Type = TableWidthUnitValues.Dxa },
                new TableCellRightMargin { Width = 108, Type = TableWidthValues.Dxa })))
        { Type = StyleValues.Table, StyleId = "TableNormal", Default = true });
        styles.Append(new Style(
            new StyleName { Val = "Report Table" },
            new BasedOn { Val = "TableNormal" },
            new StyleTableProperties(
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4U, Color = "808080" },
                    new LeftBorder { Val = BorderValues.Single, Size = 4U, Color = "808080" },
                    new BottomBorder { Val = BorderValues.Single, Size = 4U, Color = "808080" },
                    new RightBorder { Val = BorderValues.Single, Size = 4U, Color = "808080" },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4U, Color = "808080" },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4U, Color = "808080" })),
            new TableStyleProperties(
                new TableStyleConditionalFormattingTableCellProperties(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "D9E2F3" }))
            { Type = TableStyleOverrideValues.FirstRow })
        { Type = StyleValues.Table, StyleId = "ReportTable" });
        part.Styles = styles;
        part.Styles.Save();
    }

    private static void AddNumbering(MainDocumentPart main)
    {
        var part = main.AddNewPart<NumberingDefinitionsPart>();
        part.Numbering = new Numbering(
            new AbstractNum(
                new MultiLevelType { Val = MultiLevelValues.SingleLevel },
                new Level(
                    new StartNumberingValue { Val = 1 },
                    new NumberingFormat { Val = NumberFormatValues.Bullet },
                    new LevelText { Val = "\u2022" },
                    new LevelJustification { Val = LevelJustificationValues.Left },
                    new PreviousParagraphProperties(new Indentation { Left = "720", Hanging = "360" }))
                { LevelIndex = 0 })
            { AbstractNumberId = 1 },
            new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 });
        part.Numbering.Save();
    }
}
