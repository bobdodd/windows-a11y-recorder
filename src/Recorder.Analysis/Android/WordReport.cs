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
/// A report's Markdown as a Word document for the client's team
/// (android-report.md, "The Word document"): each block the reading view
/// reads, in the same order. The first level 1 heading is the title, in
/// Word's Title style, followed by a table of contents; every other
/// heading is one of Word's numbered heading styles, a level higher, so
/// that the Markdown's level 2 is Heading 1, numbered 1, 1.1 and so on by
/// the styles themselves. Each page has the title in its header and its
/// number in its footer. Tables have their header row repeated on each page
/// and a caption; pictures have their descriptions as alternative text;
/// links say where they go; the title and language are in the document's
/// properties; on Letter paper with one inch margins. Word updates the
/// contents' page numbers when it opens the document.
/// </summary>
public static partial class WordReport
{
    private const long EmuPerInch = 914400;

    /// <summary>The heading levels the table of contents lists: Word's Heading 1 and 2.</summary>
    public const int ContentsLevels = 2;

    // A Markdown link, [text](address), or a web address on its own.
    [GeneratedRegex(@"\[([^\]]+)\]\((https?://[^)\s]+)\)|https?://[^\s)\]]+")]
    private static partial Regex LinkPattern();

    /// <summary>The Word document's bytes.</summary>
    /// <param name="folder">The report's folder, which the pictures' paths are relative to.</param>
    /// <param name="boldLabels">Labels that open a paragraph and are set in bold.</param>
    public static byte[] Write(string markdown, string folder, IReadOnlyList<string> boldLabels)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(boldLabels);
        var report = ReportDocument.Parse(markdown);
        var title = report.Title ?? "Android accessibility report";
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            document.PackageProperties.Title = title;
            document.PackageProperties.Language = "en-CA";
            document.PackageProperties.Creator = "Windows A11y Recorder";
            var main = document.AddMainDocumentPart();
            AddStyles(main);
            AddNumbering(main);
            AddSettings(main);
            var body = new Body();
            main.Document = new Document(body);
            var writer = new BodyWriter(main, body, folder, boldLabels, Contents(report.Blocks));
            foreach (var block in report.Blocks)
            {
                writer.Add(block);
            }

            var header = main.AddNewPart<HeaderPart>();
            header.Header = new Header(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Header" }), new Run(new Text(title))));
            header.Header.Save();
            var footer = main.AddNewPart<FooterPart>();
            footer.Footer = new Footer(new Paragraph(
                new ParagraphProperties(new ParagraphStyleId { Val = "Footer" }),
                new Run(new Text("Page ") { Space = SpaceProcessingModeValues.Preserve }),
                new SimpleField(new Run(new Text("1"))) { Instruction = " PAGE " },
                new Run(new Text(" of ") { Space = SpaceProcessingModeValues.Preserve }),
                new SimpleField(new Run(new Text("1"))) { Instruction = " NUMPAGES " }));
            footer.Footer.Save();
            body.Append(new SectionProperties(
                new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(header) },
                new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(footer) },
                new PageSize { Width = 12240U, Height = 15840U },
                new PageMargin { Top = 1440, Bottom = 1440, Left = 1440U, Right = 1440U, Header = 720U, Footer = 720U, Gutter = 0U }));
            main.Document.Save();
        }

        return stream.ToArray();
    }

    /// <summary>A heading's number as its style numbers it, such as "2.3", by its place among the headings before it.</summary>
    private static List<(ReportBlock Heading, string Number)> Contents(IReadOnlyList<ReportBlock> blocks)
    {
        var counts = new int[6];
        var numbered = new List<(ReportBlock, string)>();
        var titled = false;
        foreach (var block in blocks.Where(block => block.Kind == ReportBlockKind.Heading))
        {
            if (block.Level == 1 && !titled)
            {
                titled = true;
                continue;
            }

            var level = Math.Clamp(block.Level - 1, 1, 6);
            counts[level - 1]++;
            Array.Clear(counts, level, counts.Length - level);
            numbered.Add((block, string.Join('.', counts.Take(level).Select(count => count.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
        }

        return numbered;
    }

    private sealed class BodyWriter(MainDocumentPart main, Body body, string folder, IReadOnlyList<string> boldLabels, List<(ReportBlock Heading, string Number)> contents)
    {
        private uint _pictures;
        private int _headings;
        private bool _titled;
        private bool _afterTable;
        private string _lastHeading = "";

        private static string Bookmark(int index) => "_Toc" + index.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

        public void Add(ReportBlock block)
        {
            // Two tables in a row would join into one; a paragraph keeps
            // them apart. Otherwise no empty paragraph is written, as a
            // screen reader reads each one as blank.
            if (block.Kind == ReportBlockKind.Table && _afterTable)
            {
                body.Append(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "0" })));
            }

            var spaceAfterTable = _afterTable && block.Kind != ReportBlockKind.Table;
            var first = body.ChildElements.Count;
            _afterTable = block.Kind == ReportBlockKind.Table;
            Write(block);

            // The paragraph after a table is set a little below it.
            if (spaceAfterTable && body.ChildElements.Count > first && body.ChildElements[first] is Paragraph next)
            {
                var properties = next.ParagraphProperties ??= new ParagraphProperties();
                var spacing = properties.SpacingBetweenLines ??= new SpacingBetweenLines();
                spacing.Before ??= "160";
            }
        }

        private void Write(ReportBlock block)
        {
            switch (block.Kind)
            {
                case ReportBlockKind.Heading when block.Level == 1 && !_titled:
                    _titled = true;
                    body.Append(Paragraph("Title", Runs(block.Text)));
                    break;
                case ReportBlockKind.Heading:
                    Heading(block);
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

        // A heading in its numbered style, a level higher than the
        // Markdown's, with a bookmark for the contents to link to. The
        // contents come before the first heading, which starts a new page.
        private void Heading(ReportBlock block)
        {
            var index = _headings++;
            if (index == 0)
            {
                Contents();
            }

            var level = Math.Clamp(block.Level - 1, 1, 6);
            var properties = new ParagraphProperties(new ParagraphStyleId { Val = "Heading" + level.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            if (index == 0)
            {
                properties.Append(new PageBreakBefore());
            }

            var id = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            body.Append(new Paragraph(new OpenXmlElement[] { properties, new BookmarkStart { Name = Bookmark(index), Id = id } }
                .Concat(Runs(block.Text))
                .Append(new BookmarkEnd { Id = id })));
            _lastHeading = block.Text;
        }

        // The table of contents: Word's TOC field over Heading 1 and 2,
        // with its entries written as links to the headings' bookmarks so
        // that it reads in full before Word updates it with page numbers.
        private void Contents()
        {
            body.Append(Paragraph("TOCHeading", [new Run(Text("Contents"))]));
            var entries = contents.Select((entry, index) => (entry.Heading, entry.Number, Index: index))
                .Where(entry => entry.Heading.Level - 1 <= ContentsLevels)
                .ToList();
            var instruction = $" TOC \\o \"1-{ContentsLevels.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" \\h \\z \\u ";
            OpenXmlElement[] begin =
            [
                new Run(new FieldChar { FieldCharType = FieldCharValues.Begin, Dirty = true }),
                new Run(new FieldCode(instruction) { Space = SpaceProcessingModeValues.Preserve }),
                new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            ];
            OpenXmlElement end = new Run(new FieldChar { FieldCharType = FieldCharValues.End });
            if (entries.Count == 0)
            {
                body.Append(new Paragraph(begin.Append(new Run(Text("No headings."))).Append(end)));
                return;
            }

            for (var at = 0; at < entries.Count; at++)
            {
                var (heading, number, index) = entries[at];
                var level = Math.Clamp(heading.Level - 1, 1, ContentsLevels);
                var paragraph = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "TOC" + level.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
                if (at == 0)
                {
                    paragraph.Append(begin.Select(element => element.CloneNode(true)));
                }

                paragraph.Append(new Hyperlink(new Run(new RunProperties(new RunStyle { Val = "Hyperlink" }), Text(number + " " + heading.Text))) { Anchor = Bookmark(index), History = true });
                if (at == entries.Count - 1)
                {
                    paragraph.Append(end);
                }

                body.Append(paragraph);
            }
        }

        private IEnumerable<OpenXmlElement> Labelled(string text)
        {
            var label = boldLabels.FirstOrDefault(candidate => text.StartsWith(candidate, StringComparison.Ordinal));
            if (label is null)
            {
                return Runs(text);
            }

            return new OpenXmlElement[] { new Run(new RunProperties(new Bold()), Text(label)) }.Concat(Runs(text[label.Length..]));
        }

        // Text as runs, with each Markdown link a link with its text, and
        // each web address on its own a link to itself.
        private List<OpenXmlElement> Runs(string text, bool bold = false)
        {
            var runs = new List<OpenXmlElement>();
            var at = 0;
            foreach (Match match in LinkPattern().Matches(text))
            {
                if (match.Index > at)
                {
                    runs.Add(Run(text[at..match.Index], bold));
                }

                var named = match.Groups[1].Success;
                var url = named ? match.Groups[2].Value : match.Value.TrimEnd('.', ',', ';', ':');
                var relationship = main.AddHyperlinkRelationship(new Uri(url, UriKind.Absolute), true);
                runs.Add(new Hyperlink(new Run(new RunProperties(new RunStyle { Val = "Hyperlink" }), Text(named ? match.Groups[1].Value : url))) { Id = relationship.Id });
                at = match.Index + (named ? match.Length : url.Length);
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
                new TableLook { Val = "04A0", FirstRow = true, LastRow = false, FirstColumn = false, LastColumn = false, NoHorizontalBand = true, NoVerticalBand = true },
                // The heading the table is under, as its title for a
                // screen reader's list of tables.
                new TableCaption { Val = _lastHeading.Length == 0 ? "Table" : _lastHeading }));
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
        styles.Append(new Style(
            new StyleName { Val = "Title" },
            new BasedOn { Val = "Normal" },
            new NextParagraphStyle { Val = "Normal" },
            new PrimaryStyle(),
            new StyleParagraphProperties(new SpacingBetweenLines { After = "240" }),
            new StyleRunProperties(new Bold(), new Color { Val = "1F3864" }, new FontSize { Val = "48" }, new FontSizeComplexScript { Val = "48" }))
        { Type = StyleValues.Paragraph, StyleId = "Title" });
        var sizes = new[] { "32", "28", "26", "24", "22", "22" };
        for (var level = 1; level <= 6; level++)
        {
            // Each heading style is numbered by the heading list, at its
            // level: 1, 1.1, 1.1.1 and so on.
            styles.Append(new Style(
                new StyleName { Val = "heading " + level.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                new BasedOn { Val = "Normal" },
                new NextParagraphStyle { Val = "Normal" },
                new PrimaryStyle(),
                new StyleParagraphProperties(
                    new KeepNext(),
                    new KeepLines(),
                    new NumberingProperties(new NumberingLevelReference { Val = level - 1 }, new NumberingId { Val = HeadingNumbering }),
                    new SpacingBetweenLines { Before = level == 1 ? "360" : "240", After = "80" },
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
            new StyleName { Val = "TOC Heading" },
            new BasedOn { Val = "Normal" },
            new NextParagraphStyle { Val = "Normal" },
            new StyleParagraphProperties(new KeepNext(), new SpacingBetweenLines { Before = "240", After = "120" }),
            new StyleRunProperties(new Bold(), new Color { Val = "1F3864" }, new FontSize { Val = "32" }, new FontSizeComplexScript { Val = "32" }))
        { Type = StyleValues.Paragraph, StyleId = "TOCHeading" });
        for (var level = 1; level <= ContentsLevels; level++)
        {
            styles.Append(new Style(
                new StyleName { Val = "toc " + level.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                new BasedOn { Val = "Normal" },
                new NextParagraphStyle { Val = "Normal" },
                new StyleParagraphProperties(
                    new Tabs(new TabStop { Val = TabStopValues.Right, Leader = TabStopLeaderCharValues.Dot, Position = 9350 }),
                    new SpacingBetweenLines { After = "60" },
                    new Indentation { Left = ((level - 1) * 440).ToString(System.Globalization.CultureInfo.InvariantCulture) }))
            { Type = StyleValues.Paragraph, StyleId = "TOC" + level.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        foreach (var (name, id) in new[] { ("header", "Header"), ("footer", "Footer") })
        {
            styles.Append(new Style(
                new StyleName { Val = name },
                new BasedOn { Val = "Normal" },
                new StyleParagraphProperties(new SpacingBetweenLines { After = "0" }, new Justification { Val = id == "Footer" ? JustificationValues.Center : JustificationValues.Right }),
                new StyleRunProperties(new Color { Val = "404040" }, new FontSize { Val = "20" }, new FontSizeComplexScript { Val = "20" }))
            { Type = StyleValues.Paragraph, StyleId = id });
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

    private const int HeadingNumbering = 2;

    // Word opens the document in its current mode, not compatibility mode,
    // where its accessibility checker is limited, and updates the
    // contents' page numbers when it opens it.
    private static void AddSettings(MainDocumentPart main)
    {
        var part = main.AddNewPart<DocumentSettingsPart>();
        part.Settings = new Settings(
            new UpdateFieldsOnOpen { Val = true },
            new Compatibility(new CompatibilitySetting
            {
                Name = CompatSettingNameValues.CompatibilityMode,
                Uri = "http://schemas.microsoft.com/office/word",
                Val = "15",
            }));
        part.Settings.Save();
    }

    private static void AddNumbering(MainDocumentPart main)
    {
        var part = main.AddNewPart<NumberingDefinitionsPart>();
        var headings = new AbstractNum(new MultiLevelType { Val = MultiLevelValues.Multilevel }) { AbstractNumberId = HeadingNumbering };
        for (var level = 0; level < 6; level++)
        {
            headings.Append(new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Decimal },
                new ParagraphStyleIdInLevel { Val = "Heading" + (level + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) },
                new LevelSuffix { Val = LevelSuffixValues.Space },
                new LevelText { Val = string.Join('.', Enumerable.Range(1, level + 1).Select(n => "%" + n.ToString(System.Globalization.CultureInfo.InvariantCulture))) },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(new Indentation { Left = "0", FirstLine = "0" }))
            { LevelIndex = level });
        }

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
            headings,
            new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 },
            new NumberingInstance(new AbstractNumId { Val = HeadingNumbering }) { NumberID = HeadingNumbering });
        part.Numbering.Save();
    }
}
