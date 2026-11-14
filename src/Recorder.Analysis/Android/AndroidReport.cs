using System.Globalization;
using System.Text;
using Recorder.Analysis.Summary;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>What the Android report is written from, besides the audit.</summary>
/// <param name="ScreenAt">The phone's screen size at a time, in pixels, as the tree gives it; null when unknown.</param>
/// <param name="Load">Reads a picture file's pixels; null when it cannot be read.</param>
/// <param name="EvidenceKinds">What the recording holds, in words, such as "12 screenshots from the companion app".</param>
/// <param name="Incomplete">Why the recording file was read only in part, when it was.</param>
public sealed record AndroidReportInput(
    string SessionKey,
    long DurationNanoseconds,
    PhoneScreenTimeline Screens,
    PhoneDeviceTimeline Device,
    AndroidAudit Audit,
    PhoneSnapshotPictures Pictures,
    Func<long, (int Width, int Height)?> ScreenAt,
    Func<string, PhonePixels?> Load,
    IReadOnlyList<string> EvidenceKinds,
    string? Incomplete);

/// <summary>A model's summaries for the Android summary: the whole app's, each screen's, and the attempts that wrote them.</summary>
public sealed record AndroidModelText(
    AcceptedStep? Overall,
    IReadOnlyDictionary<PhoneScreen, AcceptedStep> Screens,
    IReadOnlyList<AttemptRecord> Attempts);

/// <summary>
/// Writes the Android report as Markdown, by screen, with the pictures it
/// shows (android-report.md). The Word document is made from this
/// Markdown, so the two say the same. With a model's text it is the Android
/// summary: the same report with each screen's and the app's summaries.
/// </summary>
public static class AndroidReport
{
    /// <summary>The labels that open a finding's paragraphs, which the Word document sets in bold.</summary>
    public static readonly IReadOnlyList<string> Labels =
    [
        "What the issue is:", "Why it matters:", "Who it affects:", "Suggested remediation for Android:",
        "Suggested remediation for Flutter:", "Suggested remediation for React Native:", "Picture:", "Sources:",
    ];

    /// <summary>
    /// Writes the report and its pictures. The pictures are written to
    /// <paramref name="picturesFolder"/>, named in the Markdown by
    /// <paramref name="picturesRelative"/>, relative to the report's folder.
    /// </summary>
    public static string Write(
        AndroidReportInput input,
        AndroidGuidance guidance,
        AndroidEvidence evidence,
        AndroidModelText? model,
        string picturesFolder,
        string picturesRelative,
        DateTime writtenUtc)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(guidance);
        ArgumentNullException.ThrowIfNull(evidence);
        var writer = new Writer(input, guidance, evidence, model, picturesFolder, picturesRelative);
        return writer.Write(writtenUtc);
    }

    /// <summary>A model statement's text with its references at its end, each once.</summary>
    internal static string Statement(Claim claim)
    {
        var (text, _) = LayeredReport.WithoutCitedReferences(SummaryReport.OneLine(claim.Text), claim.Evidence);
        var references = claim.Evidence.Distinct(StringComparer.Ordinal).ToList();
        return references.Count == 0 ? text : text + " " + string.Join(' ', references.Select(reference => $"[{reference}]"));
    }

    private sealed class Writer(
        AndroidReportInput input,
        AndroidGuidance guidance,
        AndroidEvidence evidence,
        AndroidModelText? model,
        string picturesFolder,
        string picturesRelative)
    {
        private readonly StringBuilder _text = new();
        private readonly HashSet<string> _cited = new(StringComparer.Ordinal);
        private readonly Dictionary<PhoneFinding, string> _placed = [];
        private readonly Dictionary<PhoneFinding, (PhonePixels Pixels, PictureBox Box)> _zoomable = [];

        public string Write(DateTime writtenUtc)
        {
            var summary = model is not null;
            Heading(1, (summary ? "Android accessibility summary: " : "Android accessibility report: ") + input.SessionKey);
            Paragraph(summary
                ? "The findings in this summary are the recorder's own checks of the recording, with no model. The summaries of the app and of each screen are a model's inferences from those findings, and say so where they appear."
                : "Every finding in this report is the recorder's own check of the recording; no model wrote any of it.");
            About(writtenUtc);
            HowToRead();
            if (model is not null)
            {
                Overview(model);
            }

            AtAGlance();
            AcrossTheApp();
            foreach (var screen in input.Screens.Screens)
            {
                Screen(screen);
            }

            Limits();
            if (model is not null)
            {
                Heading(2, "How the summaries were written");
                var lines = new StringBuilder();
                SummaryReport.AppendModelPerStep(lines, model.Attempts);
                EndList();
                _text.Append(lines.Length == 0 ? "No model step ran.\n" : lines.ToString()).Append('\n');
            }

            Evidence();
            return _text.ToString();
        }

        private void About(DateTime writtenUtc)
        {
            Heading(2, "About this recording");
            var rows = new List<(string, string)>
            {
                ("Recording", input.SessionKey),
                ("Length", WindowsPreferenceTimeline.ClockOf(input.DurationNanoseconds)),
            };
            foreach (var app in input.Screens.Screens.Select(screen => screen.Package).Distinct(StringComparer.Ordinal))
            {
                rows.Add(($"App {app}", $"{input.Device.DescribeVersion(app)}. {input.Device.DescribeFramework(app)}"));
            }

            rows.Add(("Screens and visits", Count(input.Screens.Screens.Count, "screen", "screens") + ", " + Count(input.Screens.Visits.Count, "visit", "visits")));
            rows.Add(("Audit level", $"WCAG {input.Audit.Level}; the phone had to be held turned for {input.Audit.HoldSeconds.ToString(CultureInfo.InvariantCulture)} seconds for the orientation check"));
            rows.Add(("Evidence in the recording", input.EvidenceKinds.Count == 0 ? "No phone evidence besides the accessibility tree" : string.Join("; ", input.EvidenceKinds)));
            rows.Add(("Text recognition", input.Audit.OcrEngines.Count > 0
                ? string.Join("; ", input.Audit.OcrEngines)
                : input.Audit.Measured?.OcrUnavailable is { } unavailable ? "Not used: " + unavailable + "; the app's text was measured on its items' bounds"
                : input.Audit.MeasureProblem is { } problem ? "The colours could not be measured: " + problem
                : "Not used"));
            rows.Add(("Guidance", $"Version {guidance.Version.ToString(CultureInfo.InvariantCulture)}, SHA-256 {guidance.Sha256}"));
            rows.Add(("Written", writtenUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)));
            if (input.Incomplete is { } incomplete)
            {
                rows.Add(("Recording file", "Read in part: " + incomplete));
            }

            Table(["Item", "Value"], rows.Select(row => (IReadOnlyList<string>)[row.Item1, row.Item2]));
        }

        private void HowToRead()
        {
            Heading(2, "How to read this report");
            Paragraph("The report is arranged by screen, in the order the screens were first visited, after the findings of the app as a whole. Each visit to a screen has the screen's picture, with each finding's item outlined and numbered, and each finding has its own section.");
            Item("An error is a check that failed on the recorded evidence.");
            Item("A warning is a likely issue that a person needs to confirm, for example with TalkBack or a keyboard; its impact is scored as if confirmed.");
            Item("Information is a note that may point to an issue; its impact is also scored as if confirmed.");
            Paragraph("The impact score is the recorder's default judgement of each kind of issue once confirmed, from its guidance file; it is not measured from this app:");
            foreach (var step in guidance.Impact)
            {
                Item($"{step.Score.ToString(CultureInfo.InvariantCulture)}, {step.Word}: {step.Meaning}");
            }

            Paragraph("In the pictures, each item is outlined in cyan between two black bands, with the finding's number in black on cyan beside it. A small item also has a zoomed picture, taken from the picture at full size. A finding about the whole screen has no outline.");
            Paragraph("The remediation is given for Android's own Views and Jetpack Compose, and also for Flutter or React Native when the recorder found that framework's library in the app. The recorder reads which libraries are bundled in the app's files, not which framework drew each screen, and not finding one does not mean the app is native Android.");
            Paragraph("References such as [E3] are to the evidence list at the end, which gives each one's time in the recording.");
        }

        private void Overview(AndroidModelText text)
        {
            Heading(2, "Overview, inferred by a model");
            if (text.Overall is null)
            {
                Paragraph("No overview was written, as the recording has no findings.");
                return;
            }

            Claims(text.Overall, "app", "The app, inferred");
            Claims(text.Overall, "barriers", "The main barriers, inferred");
            Claims(text.Overall, "priorities", "What to fix first, inferred");
        }

        private void Claims(AcceptedStep step, string section, string heading)
        {
            Heading(3, heading);
            var claims = step.Claims.Where(claim => claim.Section == section).ToList();
            if (claims.Count == 0)
            {
                Paragraph("The model wrote nothing here.");
                return;
            }

            foreach (var paragraph in claims.GroupBy(claim => claim.Paragraph).OrderBy(group => group.Key))
            {
                Paragraph(string.Join(' ', paragraph.OrderBy(claim => claim.Ordinal).Select(Statement)));
            }
        }

        private void AtAGlance()
        {
            Heading(2, "At a glance");
            var all = evidence.All;
            Paragraph(Count(all.Count, "finding", "findings") + ": " +
                Count(all.Count(f => f.Finding.Level == PhoneCheckLevel.Error), "error", "errors") + ", " +
                Count(all.Count(f => f.Finding.Level == PhoneCheckLevel.Warning), "warning", "warnings") + " and " +
                Count(all.Count(f => f.Finding.Level == PhoneCheckLevel.Info), "note of information", "notes of information") + ".");
            Heading(3, "By WCAG success criterion");
            var byCriterion = all
                .SelectMany(finding => (finding.Guidance?.Criteria ?? []).DefaultIfEmpty("").Select(criterion => (Key: criterion, Finding: finding)))
                .GroupBy(pair => pair.Key, pair => pair.Finding)
                .OrderBy(group => group.Key.Length == 0 ? 1 : 0)
                .ThenBy(group => CriterionOrder(group.Key))
                .ToList();
            if (byCriterion.Count == 0)
            {
                Paragraph("No findings.");
            }
            else
            {
                Table(["Success criterion", "Findings", "Errors", "Warnings", "Highest impact"], byCriterion.Select(group => (IReadOnlyList<string>)
                [
                    group.Key.Length == 0 ? "None: best practice" : guidance.CriterionText(group.Key),
                    .. Counts(group.ToList()),
                ]));
            }

            Heading(3, "By screen");
            var rows = new List<IReadOnlyList<string>>();
            if (evidence.App.Count > 0)
            {
                rows.Add(["Across the app", "", .. Counts(evidence.App)]);
            }

            foreach (var screen in input.Screens.Screens)
            {
                rows.Add([ScreenHeading(screen), screen.Visits.Count.ToString(CultureInfo.InvariantCulture), .. Counts([.. evidence.For(screen)])]);
            }

            Table(["Screen", "Visits", "Findings", "Errors", "Warnings", "Highest impact"], rows);
        }

        private IReadOnlyList<string> Counts(IReadOnlyList<NumberedFinding> findings)
        {
            var highest = findings.Count == 0 ? 0 : findings.Max(finding => finding.Impact);
            return
            [
                findings.Count.ToString(CultureInfo.InvariantCulture),
                findings.Count(finding => finding.Finding.Level == PhoneCheckLevel.Error).ToString(CultureInfo.InvariantCulture),
                findings.Count(finding => finding.Finding.Level == PhoneCheckLevel.Warning).ToString(CultureInfo.InvariantCulture),
                highest == 0 ? "None" : $"{highest.ToString(CultureInfo.InvariantCulture)}, {guidance.ImpactOf(highest).Word}",
            ];
        }

        private static string CriterionOrder(string number) =>
            string.Join('.', number.Split('.').Select(part => part.PadLeft(3, '0')));

        private void AcrossTheApp()
        {
            if (evidence.App.Count == 0)
            {
                return;
            }

            Heading(2, "Across the app");
            Paragraph("These findings are about the app as a whole, not one screen.");
            foreach (var finding in evidence.App)
            {
                Finding(finding, null);
            }
        }

        private static string ScreenHeading(PhoneScreen screen) =>
            $"Screen {screen.Number.ToString(CultureInfo.InvariantCulture)}: {screen.Name}";

        private void Screen(PhoneScreen screen)
        {
            Heading(2, ScreenHeading(screen));
            var findings = evidence.For(screen).ToList();
            Paragraph(string.Create(CultureInfo.InvariantCulture,
                $"The app {screen.Package}; the screen is {screen.NamedBy}. Visited {Times(screen.Visits.Count)}, with {Count(findings.Count, "finding", "findings")}."));
            if (model is not null)
            {
                Heading(3, "Summary of the screen, inferred by a model");
                if (model.Screens.TryGetValue(screen, out var step))
                {
                    foreach (var section in new[] { "screen", "issues" })
                    {
                        foreach (var paragraph in step.Claims.Where(claim => claim.Section == section).GroupBy(claim => claim.Paragraph).OrderBy(group => group.Key))
                        {
                            Paragraph(string.Join(' ', paragraph.OrderBy(claim => claim.Ordinal).Select(Statement)));
                        }
                    }
                }
                else
                {
                    Paragraph("No summary was asked for, as the screen has no findings.");
                }
            }

            foreach (var visit in screen.Visits)
            {
                Visit(visit);
            }
        }

        private static string Count(int count, string one, string many) =>
            count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : many);

        private static string Sentence(string text) =>
            text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

        private static string Times(int count) => count switch
        {
            1 => "once",
            2 => "twice",
            _ => count.ToString(CultureInfo.InvariantCulture) + " times",
        };

        private void Visit(PhoneVisit visit)
        {
            Heading(3, $"Visit {visit.Number.ToString(CultureInfo.InvariantCulture)}, {visit.When}");
            Paragraph($"{Sentence(visit.CauseText)}.{Refs(evidence.VisitReference(visit))}");
            var findings = evidence.For(visit);
            if (findings.Count == 0)
            {
                if (visit.IsReturn)
                {
                    Paragraph("No findings on this visit. The screen's picture is with its first visit.");
                    return;
                }

                if (FirstPicture(visit) is { } picture)
                {
                    Overview(visit, picture, []);
                }
                else
                {
                    Paragraph("No picture of this visit was recorded.");
                }

                Paragraph(visit.SettledSnapshots.Count == 0
                    ? "Not checked: the screen did not settle while the tree was recorded."
                    : "No findings on this visit.");
                return;
            }

            var groups = findings
                .Select(finding => (Finding: finding, Picture: PictureOf(finding.Finding)))
                .GroupBy(pair => pair.Picture?.AbsolutePath ?? "", StringComparer.Ordinal)
                .ToList();
            foreach (var group in groups)
            {
                var picture = group.First().Picture;
                if (picture is null)
                {
                    foreach (var (finding, _) in group)
                    {
                        _placed[finding.Finding] = "No picture of this time was recorded.";
                    }

                    continue;
                }

                Overview(visit, picture, [.. group.Select(pair => pair.Finding)]);
            }

            foreach (var finding in findings)
            {
                Finding(finding, visit);
            }
        }

        private PhoneContrastPicture? FirstPicture(PhoneVisit visit)
        {
            foreach (var settled in visit.SettledSnapshots)
            {
                if (input.Pictures.Of(visit, settled) is { } picture)
                {
                    return picture;
                }
            }

            return input.Pictures.Between(visit.Start, visit.End);
        }

        private PhoneContrastPicture? PictureOf(PhoneFinding finding)
        {
            var first = finding.Times[0];
            return input.Pictures.Of(finding.Visit, first.Snapshot) ?? input.Pictures.Between(first.Time, finding.Visit.End);
        }

        // A picture of a visit at half size, with the findings shown on it
        // outlined and numbered.
        private void Overview(PhoneVisit visit, PhoneContrastPicture picture, IReadOnlyList<NumberedFinding> findings)
        {
            var pixels = input.Load(picture.AbsolutePath);
            if (pixels is null)
            {
                Paragraph($"The picture at {WindowsPreferenceTimeline.ClockOf(picture.Time)} could not be read.");
                foreach (var finding in findings)
                {
                    _placed[finding.Finding] = "The picture of this time could not be read.";
                }

                return;
            }

            var divisor = Math.Max(1, pixels.Width / 540);
            var canvas = ReportPictures.Scaled(pixels, divisor);
            var unit = ReportPictures.Unit(canvas.Width, canvas.Height);
            var outlined = new List<int>();
            var screen = (long)pixels.Width * pixels.Height;
            foreach (var finding in findings)
            {
                var first = finding.Finding.Times[0];
                if (first.Bounds.IsEmpty)
                {
                    _placed[finding.Finding] = "This finding is about the whole screen, so no item is outlined.";
                    continue;
                }

                if (Scaled(first.Bounds, picture.Time, pixels.Width, pixels.Height) is not { } box)
                {
                    _placed[finding.Finding] = "Not outlined: the phone was turned between the snapshot and the picture.";
                    continue;
                }

                if (box.Area >= ReportPictures.WholeScreen * screen)
                {
                    _placed[finding.Finding] = "The item fills the screen, so it is not outlined.";
                    continue;
                }

                var small = new PictureBox(box.Left / divisor, box.Top / divisor, box.Right / divisor, box.Bottom / divisor);
                var outline = ReportPictures.Outline(canvas, small, unit);
                ReportPictures.Badge(canvas, finding.Number, outline, unit);
                outlined.Add(finding.Number);
                if (box.Area < ReportPictures.ZoomLimit * screen)
                {
                    _zoomable[finding.Finding] = (pixels, box);
                    _placed[finding.Finding] = $"Outlined as number {finding.Number.ToString(CultureInfo.InvariantCulture)} in the picture of visit {visit.Number.ToString(CultureInfo.InvariantCulture)}, and zoomed here.";
                }
                else
                {
                    _placed[finding.Finding] = $"Outlined as number {finding.Number.ToString(CultureInfo.InvariantCulture)} in the picture of visit {visit.Number.ToString(CultureInfo.InvariantCulture)}.";
                }
            }

            var name = string.Create(CultureInfo.InvariantCulture, $"visit-{visit.Number:D4}-{picture.Time:D}.png");
            Save(name, canvas);
            var source = picture.Kind == PhonePictureKind.Screenshot ? "screenshot" : "video frame";
            var alt = $"The screen {visit.Screen.Name} at {WindowsPreferenceTimeline.ClockOf(picture.Time)}, from a {source}";
            alt += outlined.Count switch
            {
                0 => ".",
                1 => $", with finding {outlined[0].ToString(CultureInfo.InvariantCulture)} outlined in cyan and numbered.",
                _ => $", with findings {List(outlined.Select(number => number.ToString(CultureInfo.InvariantCulture)).ToList())} outlined in cyan and numbered.",
            };
            Image(alt, name);
        }

        // Bounds in the phone's screen pixels scaled to a picture's; null
        // when the picture and the screen are turned differently, as
        // ScaledToFrame in the player.
        private PictureBox? Scaled(PhoneBounds bounds, long time, int width, int height)
        {
            double scaleX = 1, scaleY = 1;
            if (input.ScreenAt(time) is { } screen && screen.Width > 0 && screen.Height > 0)
            {
                if (width > height != screen.Width > screen.Height)
                {
                    return null;
                }

                scaleX = (double)width / screen.Width;
                scaleY = (double)height / screen.Height;
            }

            var box = new PictureBox(
                (int)Math.Floor(bounds.Left * scaleX), (int)Math.Floor(bounds.Top * scaleY),
                (int)Math.Ceiling(bounds.Right * scaleX), (int)Math.Ceiling(bounds.Bottom * scaleY)).Clamp(width, height);
            return box.IsEmpty ? null : box;
        }

        private void Finding(NumberedFinding numbered, PhoneVisit? visit)
        {
            var finding = numbered.Finding;
            var check = numbered.Guidance;
            Heading(visit is null ? 3 : 4, numbered.Heading);
            var first = finding.Times[0];
            var impact = check is null ? "Not scored: the guidance does not cover this check"
                : $"{check.Impact.ToString(CultureInfo.InvariantCulture)} of 4, {guidance.ImpactOf(check.Impact).Word}" +
                  (finding.Level == PhoneCheckLevel.Error ? "" : ", if confirmed");
            var level = finding.Level switch
            {
                PhoneCheckLevel.Error => "Error",
                PhoneCheckLevel.Warning => "Warning, for a person to confirm",
                PhoneCheckLevel.Info => "Information, for a person to judge",
                _ => "Not run",
            };
            var measured = first.Measured +
                (finding.Times.Skip(1).Any(time => time.Measured != first.Measured) ? " (other values at other times)" : "") +
                (first.State is null ? "" : $"; {first.State}");
            // Two snapshots can be shown by the same picture, when the video
            // did not change between them; each time is listed once.
            var distinct = finding.Times.Select(time => time.Time).Distinct().ToList();
            var shown = distinct.Take(10).Select(WindowsPreferenceTimeline.ClockOf).ToList();
            var seen = Sentence(Times(finding.Times.Count)) +
                (distinct.Count < finding.Times.Count ? ", in " + Count(distinct.Count, "picture", "pictures") : "") +
                ", at " + string.Join(", ", shown) +
                (distinct.Count > shown.Count ? $" and {(distinct.Count - shown.Count).ToString(CultureInfo.InvariantCulture)} more" : "") +
                Refs(numbered.Reference);
            var rows = new List<IReadOnlyList<string>>
            {
                new[] { "Level", level },
                new[] { "Impact", impact },
                new[] { "WCAG success criteria", Criteria(check) },
                new[] { "Item", finding.Label },
                new[] { "Measured", measured },
                new[] { "Seen", seen },
                new[] { "Check", PhoneCheckNames.Title(finding.Check) + (check is null ? "" : ". " + check.Basis) },
            };
            Table(["Field", "Value"], rows);
            if (visit is not null)
            {
                Picture(numbered, visit);
            }

            if (check is null)
            {
                Paragraph("The guidance file has no text for this check.");
                return;
            }

            Paragraph("What the issue is: " + check.Issue);
            Paragraph("Why it matters: " + check.Why);
            Paragraph("Who it affects: " + string.Join("; ", check.Affects) + ".");
            Paragraph("Suggested remediation for Android: " + check.Android);
            var frameworks = Frameworks(finding.Screen.Package);
            if (frameworks.Contains("Flutter"))
            {
                Paragraph("Suggested remediation for Flutter: " + check.Flutter);
            }

            if (frameworks.Contains("React Native"))
            {
                Paragraph("Suggested remediation for React Native: " + check.ReactNative);
            }

            Paragraph("Sources:");
            foreach (var key in check.Sources)
            {
                // A framework's sources go with its remediation, only when the
                // framework was found.
                var framework = key.StartsWith("flutter-", StringComparison.Ordinal) ? "Flutter"
                    : key.StartsWith("rn-", StringComparison.Ordinal) ? "React Native"
                    : null;
                if ((framework is null || frameworks.Contains(framework)) && guidance.Sources.TryGetValue(key, out var source))
                {
                    Item($"{source.Title}: {source.Url}");
                }
            }
        }

        private string Criteria(CheckGuidance? check)
        {
            if (check is null)
            {
                return "Unknown";
            }

            if (check.Criteria.Count > 0)
            {
                return string.Join("; ", check.Criteria.Select(guidance.CriterionText));
            }

            return check.Related.Count == 0 ? "None: best practice"
                : "None: best practice, related to " + string.Join("; ", check.Related.Select(guidance.CriterionText));
        }

        private HashSet<string> Frameworks(string package)
        {
            var framework = input.Device.VersionOf(package)?.Framework;
            return framework is { Status: "detected" } ? new HashSet<string>(framework.Names, StringComparer.Ordinal) : [];
        }

        private void Picture(NumberedFinding numbered, PhoneVisit visit)
        {
            var finding = numbered.Finding;
            if (_zoomable.TryGetValue(finding, out var zoom))
            {
                var (crop, scale) = ReportPictures.ZoomOf(zoom.Box, zoom.Pixels.Width, zoom.Pixels.Height);
                if (!crop.IsEmpty)
                {
                    var canvas = ReportPictures.Zoomed(zoom.Pixels, crop, scale);
                    var item = new PictureBox(
                        (zoom.Box.Left - crop.Left) * scale, (zoom.Box.Top - crop.Top) * scale,
                        (zoom.Box.Right - crop.Left) * scale, (zoom.Box.Bottom - crop.Top) * scale);
                    var unit = Math.Max(2, ReportPictures.Unit(canvas.Width, canvas.Height));
                    var outline = ReportPictures.Outline(canvas, item, unit);
                    ReportPictures.Badge(canvas, numbered.Number, outline, unit);
                    var name = string.Create(CultureInfo.InvariantCulture, $"finding-{numbered.Number:D4}.png");
                    Save(name, canvas);
                    var times = scale == 1 ? "at full size" : $"zoomed {scale.ToString(CultureInfo.InvariantCulture)} times";
                    Paragraph("Picture: " + _placed[finding]);
                    Image($"Finding {numbered.Number.ToString(CultureInfo.InvariantCulture)}, {finding.Label}, on the screen {visit.Screen.Name}, {times}, outlined in cyan.", name);
                    return;
                }
            }

            Paragraph("Picture: " + (_placed.GetValueOrDefault(finding) ?? "No picture of this time was recorded."));
        }

        private void Limits()
        {
            Heading(2, "Limits");
            Item("The checks cover what the recording shows: the screens visited, in the states they were seen in. A screen or state not recorded is not checked.");
            Item("No finding on a screen does not show that it meets WCAG; many success criteria need a person's judgement or a test the recording cannot make.");
            Item("Warnings and information need a person to confirm them; their impact is scored as if confirmed.");
            Item("The impact scores and the remediation are the recorder's guidance for each kind of issue, not advice written for this app.");
            Item("Colours measured on a video frame are approximate, as the video is compressed; those measured on the companion app's screenshots are exact.");
            Item("The report covers the whole recording, whatever part of it was chosen when it was queued.");
            if (input.Audit.MeasureProblem is { } problem)
            {
                Item("The colours could not be measured, so the contrast, image of text and label in name checks did not run: " + problem);
            }

            foreach (var problem2 in input.Audit.Tree.Problems)
            {
                Item("A check could not run: " + problem2);
            }
        }

        private void Evidence()
        {
            Heading(2, "Evidence");
            Paragraph("Each visit and each finding the report cites, with its time in the recording.");
            Table(["Ref", "Time", "Evidence"], evidence.Entries
                .Where(entry => _cited.Contains(entry.Reference))
                .Select(entry => (IReadOnlyList<string>)[entry.Reference, WindowsPreferenceTimeline.ClockOf(entry.Time), entry.Text]));
        }

        private string Refs(params string[] references)
        {
            foreach (var reference in references)
            {
                _cited.Add(reference);
            }

            return references.Length == 0 ? "" : " " + string.Join(' ', references.Select(reference => $"[{reference}]"));
        }

        private void Save(string name, ReportCanvas canvas)
        {
            Directory.CreateDirectory(picturesFolder);
            File.WriteAllBytes(Path.Combine(picturesFolder, name), ReportPictures.Png(ReportPictures.Edged(canvas)));
        }

        private void Heading(int level, string text)
        {
            EndList();
            _text.Append('#', level).Append(' ').Append(SummaryReport.OneLine(text)).Append("\n\n");
        }

        private void Paragraph(string text)
        {
            EndList();
            foreach (var reference in ReportDocument.ReferencesIn(text))
            {
                _cited.Add(reference);
            }

            _text.Append(Escaped(SummaryReport.OneLine(text))).Append("\n\n");
        }

        private void Item(string text)
        {
            foreach (var reference in ReportDocument.ReferencesIn(text))
            {
                _cited.Add(reference);
            }

            _text.Append("- ").Append(SummaryReport.OneLine(text)).Append('\n');
        }

        private void Image(string alt, string name)
        {
            EndList();
            var path = picturesRelative.TrimEnd('/') + "/" + name;
            _text.Append("![").Append(SummaryReport.OneLine(alt).Replace("]", ")", StringComparison.Ordinal)).Append("](")
                .Append(Uri.EscapeDataString(path).Replace("%2F", "/", StringComparison.Ordinal)).Append(")\n\n");
        }

        private void Table(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
        {
            EndList();
            _text.Append("| ").Append(string.Join(" | ", header.Select(SummaryReport.Cell))).Append(" |\n");
            _text.Append('|').Append(string.Concat(header.Select(_ => " --- |"))).Append('\n');
            foreach (var row in rows)
            {
                foreach (var reference in row.SelectMany(ReportDocument.ReferencesIn))
                {
                    _cited.Add(reference);
                }

                _text.Append("| ").Append(string.Join(" | ", row.Select(SummaryReport.Cell))).Append(" |\n");
            }

            _text.Append('\n');
        }

        // A list ends with a blank line before the next block.
        private void EndList()
        {
            if (_text.Length > 1 && _text[^1] == '\n' && _text[^2] != '\n')
            {
                _text.Append('\n');
            }
        }

        // A paragraph that would read as another block is kept a paragraph.
        private static string Escaped(string text) =>
            text.Length > 0 && (text[0] is '#' or '|' or '>' || text.StartsWith("- ", StringComparison.Ordinal) ||
                text.StartsWith("* ", StringComparison.Ordinal) || text.StartsWith("![", StringComparison.Ordinal)) ? "\u200B" + text : text;

        private static string List(IReadOnlyList<string> items) =>
            items.Count <= 1 ? string.Concat(items) : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
    }
}
