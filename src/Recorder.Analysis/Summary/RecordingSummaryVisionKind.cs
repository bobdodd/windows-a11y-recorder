using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Recorder.Database.RecordingFiles;

namespace Recorder.Analysis.Summary;

/// <summary>
/// One moment as the vision stage recorded it: the moment, its picture's
/// reference, its reasons' references, the accepted step that describes
/// its captured frame alone, and its recreated page with its tiles'
/// references and the groups of tiles not sent. The frame's and the tiles'
/// descriptions are the moment's record (recording-summary.md, "The
/// layered report").
/// </summary>
public sealed record MomentDescription(
    Moment Moment,
    string FrameReference,
    IReadOnlyList<string> ReasonReferences,
    AcceptedStep FrameStep,
    RecreatedPage? Page = null,
    IReadOnlyList<string>? TileReferences = null,
    IReadOnlyList<GroupNotSent>? NotSent = null)
{
    /// <summary>Whether the moment's page was recreated and pictured in tiles.</summary>
    public bool Recreated => Page is { Tiles.Count: > 0 };
}

/// <summary>
/// A group of a moment's tiles not sent to the model, as its tiles are the
/// same pictures at the same places as those of a group already described
/// at an earlier moment of the recording, whose answer it uses unchanged:
/// the group's number and tiles, and that group's visit, moment, frame
/// time, number, and step (recording-summary.md, "Unique images across the
/// recording", decisions 4, 5, and 9).
/// </summary>
public sealed record GroupNotSent(int Group, int FirstTile, int LastTile, int FromVisit, int FromMoment, long FromTime, int FromGroup, string FromStep);

/// <summary>
/// A group of tiles described: the moment whose request was sent, the
/// group's number, step, tiles, and their references, and the answer.
/// </summary>
internal sealed record DescribedGroup(int Visit, int Moment, long Time, int Group, string StepKey, IReadOnlyList<PageTile> Tiles, IReadOnlyList<string> TileReferences, AcceptedStep Step);

/// <summary>
/// A group of a moment's tiles with its description, the earlier group it
/// was the same as, where it was not sent, and its key across the
/// recording.
/// </summary>
internal sealed record PageGroupDescription(IReadOnlyList<PageTile> Tiles, AcceptedStep Step, string Key, DescribedGroup? Earlier = null);

/// <summary>
/// A visit's part of the vision stage: its moments as recorded, how many
/// were left out, its knowledge in layers after its last moment, and its
/// part of the story, chapter 4.
/// </summary>
public sealed record VisitVision(IReadOnlyList<MomentDescription> Moments, int MomentsLeftOut, Knowledge Knowledge, AcceptedStep Story);

/// <summary>
/// A page of chapter 3: its number, its address, its title as last
/// recorded, the visits to it, and its description.
/// </summary>
public sealed record PageChapter(int Number, string Url, string? Title, IReadOnlyList<int> Visits, AcceptedStep Step);

/// <summary>
/// A picture the report may show: the reference of the captured frame or
/// tile, its file in the recording's folder with forward slashes, and what
/// it is.
/// </summary>
public sealed record ReportPicture(string Reference, string File, string What);

/// <summary>What the report adds for the vision stage.</summary>
public sealed record VisionReportParts(
    IReadOnlyDictionary<int, VisitVision> Visits,
    IReadOnlyList<PageChapter> Pages,
    AcceptedStep Opening,
    IReadOnlyDictionary<string, ReportPicture> Pictures,
    MomentLimits Limits,
    Guid SummaryTaskId,
    int ImageTokensPerRequest = RecordingSummaryVisionKind.DefaultImageTokensPerRequest,
    string ImagesFolder = "analysis/images",
    string MomentIndexFile = "analysis/TASK/moments.json",
    IReadOnlyList<string>? LayersNotSent = null)
{
    /// <summary>Chapter 1, the executive summary; null in a report written before it.</summary>
    public AcceptedStep? Executive { get; init; }

    /// <summary>The settings at the start and their changes, for the appendices and the story.</summary>
    public RecordedSettings Settings { get; init; } = RecordedSettings.None;

    /// <summary>The reference each setting's record is cited by, by its key.</summary>
    public IReadOnlyDictionary<string, string> SettingReferences { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// The recording summary's second stage, the layered report: each visit's
/// moments, chosen by the recorder's code, each recorded as its captured
/// frame described alone and its page, recreated at the frame and pictured
/// whole in tiles, described in groups; then at each moment, layer 2 adds
/// to the visit's knowledge what the frame shows, and layer 3 what the web
/// page adds that the frame does not, the knowledge starting as layer 1,
/// the visit's description from the recorded data. The report's chapter 1 is the
/// executive summary, chapter 2 the participant, chapter 3 describes each page from every visit to it, chapter 4 tells the
/// story of the user experience, one part for each visit and an opening,
/// written as summary-with-vision.md. It builds on a finished summary of
/// the same recording, the task it follows. See
/// docs/architecture/recording-summary.md, "The layered report".
/// </summary>
public sealed partial class RecordingSummaryVisionKind : IAnalysisTaskKind
{
    public const string Name = "recording-summary-vision";

    public const string FileName = "summary-with-vision.md";

    public const string MomentTemplate = "recording-summary-vision-moment";


    // Version 4 of the page template, from the fourth run again on the
    // target machine, 2026-10-14: a description of moment 1's first tiles
    // was stopped at its second "Below the description is a yellow button
    // labeled Read more.", true of each of several cards. Each statement now
    // says which item it is about (agreed with the owner; recording-summary.md,
    // "Step 4b"). The other templates are unchanged.
    public const int PageTemplateVersion = 4;

    /// <summary>Which item a statement is about, said in the page template from version 4.</summary>
    public const string ItemNamed =
        "When several items are alike, such as cards that each end with the same button, say in each statement which item it is about, " +
        "by its heading, its label, or its place, so that no two statements are the same word for word. ";

    /// <summary>What kind a statement of what is unknown is, said in the combined template from version 3.</summary>
    public const string UnknownIsUnknown =
        "A statement that says something is unknown, or that cites no reference, is of kind unknown; every claim cites at least one reference. ";

    /// <summary>How a reference is cited, said in the templates of version 2.</summary>
    public const string CiteWithoutBrackets = "Cite each reference as it is written without its brackets, such as \"E3\". ";

    /// <summary>
    /// That references go in a statement's evidence and not its text, said
    /// in the templates that write knowledge or the report: the target
    /// report of 2026-10-18 wrote "[E2]" in nearly every sentence of the
    /// story, which the recorder then cited again.
    /// </summary>
    public const string ReferencesOutOfText =
        "Put references in a statement's evidence only, never in its text: the recorder cites them after it. ";

    /// <summary>
    /// How what is seen is set beside what the page is built as (owner,
    /// 2026-10-18: "often web pages render links to look like buttons.
    /// That's important information. In cases where the visual styling
    /// conflicts with the underlying semantics, we describe as visual and
    /// note it is built on other semantics").
    /// </summary>
    public const string VisualAndSemantics =
        "Describe what a picture shows as it looks. Where the recorder's facts give the element another role, say both in one statement, the look first, " +
        "such as \"a red Donate Now button, built as a link\" or \"styled as buttons, built as headings with no control recorded\"; " +
        "where its visible label differs from its name in the facts, give both, such as \"labelled + show preferences, named Show Display Preferences\". " +
        "Do not give an element one role in one statement and another in the next. ";

    /// <summary>
    /// The rules of the report's prose that the target report of
    /// 2026-10-18 broke: order, causes, counts, the recording's ending, and
    /// where facts come from (report-fixes-2026-10-18.md, part 2).
    /// </summary>
    public const string EvidenceRules =
        "Keep the recorded order: put each fact at its time, and never say that something earlier disrupted, followed, or answered something later. " +
        "A change before the participant's first input belongs to the page loading, not to what the participant did. " +
        "Do not infer an intent, a reason, or an attempt to recover from inputs alone. Count as the facts count: one press of a key is one, not several. " +
        "The recording ends when the auditor stops it: the recorder or another program coming to the front at its end ends the session; it is not something that got in the way. " +
        "Give each fact its own source: a layout change comes from the recorded layout, not from a picture; a picture shows only what is in it. ";

    /// <summary>
    /// What a setting at its default shows (target report, 2026-10-18:
    /// defaults read as a preference for structure, and a theme chosen but
    /// off read as in use).
    /// </summary>
    public const string SettingsRules =
        "A setting at its default shows no choice, preference, or need: do not say it does. " +
        "A setting not at its default was set by whoever set up the computer: do not take it as evidence of what the participant is able to do or needs. " +
        "A theme or other value said to be chosen but not in use is not in use. Do not call a value large, small, high, or low unless the facts compare it. ";

    /// <summary>
    /// What the recreation lacks, which is a limit of the evidence, not of
    /// the participant's experience (target report, 2026-10-18).
    /// </summary>
    public const string RecreationIsEvidence =
        "What the rebuilt page lacks, such as an image that was not recorded, is a limit of the evidence, not part of the participant's experience: leave it out of what the participant met. ";

    /// <summary>
    /// What an engineer would want noticed, where the facts show it
    /// (target report, 2026-10-18).
    /// </summary>
    public const string Noticing =
        "Notice what an engineer would want to know where the facts show it, such as a heading with no name, a skip link passed over, an input with no recorded response, " +
        "an image with no name, or a visible label that differs from the name. ";

    // Version 2 asks for an overview of the screen, after a moment showing
    // the recorder's own player was answered with every control and time
    // on it, 128 statements, until the output limit (recording-summary.md,
    // "Stage 2 implementation status").
    // Version 3 puts the statement's fields in the order text, kind,
    // evidence, as analysis-runner.md, decision 7, gives.
    public const int MomentTemplateVersion = 3;

    public static readonly IReadOnlyList<string> MomentSections = ["screen", "change"];

    public const string PageTemplate = "recording-summary-vision-page";

    public static readonly IReadOnlyList<string> PageSections = ["page"];

    /// <summary>
    /// The image tokens of the tiles in one request, a setting: on the
    /// target machine the context is 16,384 tokens and the answer up to
    /// 8,192, and 4,096 is the most llama.cpp gives one Qwen-VL picture
    /// (see <see cref="TileGroups.ImageTokens"/>). A tile of 922 by 925
    /// pixels is 841 tokens, so four go in one request.
    /// </summary>
    public const int DefaultImageTokensPerRequest = 4096;

    /// <summary>
    /// The layer 2 step at each moment: what the captured frame adds to the
    /// visit's knowledge of the visual user experience.
    /// </summary>
    public const string Layer2Template = "recording-summary-vision-layer-2";

    /// <summary>
    /// The layer 3 step at each moment whose page was recreated: what the
    /// web page adds that the frame does not show.
    /// </summary>
    public const string Layer3Template = "recording-summary-vision-layer-3";

    /// <summary>Chapter 2's step for each page, from every visit to it.</summary>
    public const string PageChapterTemplate = "recording-summary-vision-page-chapter";

    /// <summary>Chapter 3's step for each visit, its part of the story.</summary>
    public const string StoryTemplate = "recording-summary-vision-story";

    /// <summary>Chapter 3's opening, from the visits' parts.</summary>
    public const string OpeningTemplate = "recording-summary-vision-story-opening";

    /// <summary>Chapter 1, the executive summary, written last.</summary>
    public const string ExecutiveTemplate = "recording-summary-vision-executive-summary";

    // Version 2: references out of the text, and what is seen set beside
    // what the page is built as (report-fixes-2026-10-18.md).
    public const int LayerTemplateVersion = 2;

    // Version 2 of the chapters' templates: the executive summary becomes
    // chapter 1, so the pages are chapter 2 and the story chapter 3, named
    // by what they are; the story's parts and opening are given the changes
    // to the settings and the assistive technology (recording-summary.md,
    // "The executive summary and the settings in use"). Version 3 of the
    // page chapter, story, and opening, and version 2 of the executive
    // summary: each says what a paragraph is, and the story, its opening,
    // and the executive summary have list forms; the participant becomes
    // chapter 2, the pages 3 and the story 4 (recording-summary.md, "The
    // report's structure and its paragraphs"). Version 4 of the page
    // chapter and version 3 of the executive summary: each in subsections
    // the recorder gives and heads, each its own section of the answer,
    // and the executive summary interpreting and explaining, after the
    // owner's example (recording-summary.md, "The executive summary and
    // the pages rewritten"). Version 5 of the page chapter: given each
    // visit's changes as the recorder found them (recording-summary.md,
    // "The page's changes during a visit"). Version 4 of the story and its opening: each
    // visit's part in three subsections the recorder gives and heads, the
    // opening an introduction the recorder heads, and no headings of the
    // model's own (recording-summary.md, "The story in subsections").
    // Version 5 of the story and its opening: the story told that the
    // visit's facts have the page's changes in time order.
    // Version 6 of the page chapter and the story, and version 4 of the
    // executive summary: the rules the target report of 2026-10-18 broke,
    // the story given the visit in time order to take its steps from, and
    // what is seen set beside what the page is built as
    // (report-fixes-2026-10-18.md).
    public const int PageChapterTemplateVersion = 6;

    public const int StoryTemplateVersion = 6;

    public const int ExecutiveTemplateVersion = 4;

    /// <summary>The executive summary's subsections, each a section of its answer, with the heading the recorder gives it.</summary>
    public static readonly IReadOnlyList<(string Section, string Heading)> ExecutiveParts =
    [
        ("overview", "Overview"),
        ("participant", "The participant and their set-up"),
        ("journey", "The journey"),
        ("evidence", "What the evidence shows and does not show")
    ];

    public static readonly IReadOnlyList<string> ExecutiveSections = [.. ExecutiveParts.Select(part => part.Section)];

    /// <summary>A page's subsections, each a section of its answer, with the heading the recorder gives it.</summary>
    public static readonly IReadOnlyList<(string Section, string Heading)> PageChapterParts =
    [
        ("purpose", "What the page is for"),
        ("layout", "Layout from top to bottom"),
        ("content", "Content"),
        ("controls", "Controls, forms, dialogs, menus, and messages"),
        ("changes", "How the page changed")
    ];

    public static readonly IReadOnlyList<string> PageChapterSections = [.. PageChapterParts.Select(part => part.Section)];

    /// <summary>A visit's part of the story: its subsections, each a section of its answer, with the heading the recorder gives it.</summary>
    public static readonly IReadOnlyList<(string Section, string Heading)> StoryParts =
    [
        ("arriving", "Arriving"),
        ("actions", "What the participant did"),
        ("experience", "What worked and what got in the way")
    ];

    public static readonly IReadOnlyList<string> StorySections = [.. StoryParts.Select(part => part.Section)];

    public static readonly IReadOnlyList<string> OpeningSections = ["opening"];

    public const string Sentence = "sentence";

    public const string Picture = "picture";

    public const string Bullet = "bullet";

    public const string Numbered = "numbered";

    /// <summary>
    /// What a paragraph is, said in each template that writes the report's
    /// prose (owner, 2026-10-16: "One topic each"; recording-summary.md,
    /// "The report's structure and its paragraphs").
    /// </summary>
    public const string ParagraphRule =
        "A paragraph is about one topic: begin a new paragraph for each new topic, so that no paragraph covers several. ";

    /// <summary>The list forms, said in the story's and the executive summary's templates.</summary>
    public const string ListForms =
        "\"bullet\", an item of a list whose order does not matter, such as the settings in use; " +
        "\"numbered\", a step of a sequence whose order matters, such as the participant's actions in order, which the recorder numbers. " +
        "Consecutive statements of one of these forms are one list; use a list where the reader is helped by one, and sentences otherwise. ";

    /// <summary>
    /// The forms a statement of chapter 4 takes: a sentence of prose; an
    /// item of a list; or a picture, its text the caption, citing the frame
    /// or tile it shows, which the recorder puts in. The recorder heads
    /// every part, so the model gives no headings (owner, 2026-10-16;
    /// recording-summary.md, "The story in subsections").
    /// </summary>
    public static readonly IReadOnlyList<string> StoryForms = [Sentence, Bullet, Numbered, Picture];

    /// <summary>The forms a statement of the executive summary takes: a sentence of prose, or an item of a list.</summary>
    public static readonly IReadOnlyList<string> ExecutiveForms = [Sentence, Bullet, Numbered];

    /// <summary>The rules on what a frame shows of the participant, and on NVDA, said in each layer's and chapter's template.</summary>
    public const string ParticipantRules =
        "A captured frame shows what was on the screen, not what the participant saw: say that the participant saw something only if the participant's capabilities, " +
        "as given, say that they work by sight; otherwise do not say what the participant saw. " +
        "What NVDA had to announce is the recorder's inference from the page, not a recording of speech: never say that NVDA announced, said, read, or spoke anything, that its speech stopped, or that the participant heard it. " +
        "Say what NVDA had to announce, inferred, or what NVDA would do, inferred, such as \"NVDA had to announce the main landmark (inferred)\" or \"which NVDA treats as a command to stop speech (inferred)\". " +
        "Assistive technology observed running shows what was running, not what the participant is able to do. ";

    /// <summary>How a layer's answer is given, said in both layers' templates.</summary>
    public const string LayerAnswer =
        "Answer in JSON with three sections: \"elaborations\", statements that add detail to a numbered statement of the knowledge, each citing that statement's number, such as \"K3\", first; " +
        "\"corrections\", statements where this moment shows that a numbered statement is wrong or disagrees with it, each citing that statement's number first and saying how they disagree, citing both, without settling it; " +
        "and \"new\", statements about what the knowledge does not yet cover. " +
        "Do not repeat what the knowledge already says, and do not restate the recorder's facts: add only what this moment adds. Where it adds nothing, give the three sections empty. " +
        "This is about the user experience, not a list of the page's elements: do not list each control, link, or line of text. " +
        "Each section is a list of paragraphs, and each paragraph a list of statements. Each section holds only its own paragraphs. " +
        "A statement is {\"text\": ..., \"kind\": \"claim\", \"evidence\": [...]} citing every reference it rests on, " +
        "or {\"text\": ..., \"kind\": \"unknown\", \"evidence\": []}. " + UnknownIsUnknown + "Cite only references that appear in the input. " + CiteWithoutBrackets + ReferencesOutOfText +
        VisualAndSemantics + "Write plain sentences without Markdown.";

    public const string Layer2Instructions =
        "You add to what is known of one page visit from an accessibility test recording, for accessibility and usability engineers, what a captured frame of the screen shows of the visual user experience. " +
        "You are given the visit's knowledge so far, each statement numbered, such as [K3], and labelled with its layer: layer 1 from the recorded data, layer 2 from captured frames of the screen, " +
        "and layer 3 from the web page as the recorder rebuilt it; why the recorder chose this moment, and the recorder's facts since the moment before, each beginning with an evidence reference such as [E3]; " +
        "and a model's description of this moment's captured frame, a picture of the whole screen, with the picture's reference. The knowledge and the description are inferences, not facts. " +
        "Add what the frame adds to the knowledge of the visual user experience: what was on the screen and how it looked, what stood out, what was open, such as a dialog, a menu, or a popup, " +
        "which part of the page was in view, what was hard to see or read, any other window in view, and what changed since the moment before. " +
        "Every statement from the frame's description cites the frame's reference. " +
        ParticipantRules + LayerAnswer;

    public const string Layer3Instructions =
        "You add to what is known of one page visit from an accessibility test recording, for accessibility and usability engineers, what the web page adds that the captured frame of the screen does not show. " +
        "You are given the visit's knowledge so far, each statement numbered, such as [K3], and labelled with its layer: layer 1 from the recorded data, layer 2 from captured frames of the screen, " +
        "and layer 3 from the web page as the recorder rebuilt it; why the recorder chose this moment, each reason beginning with an evidence reference such as [E3]; " +
        "the page as the recorder rebuilt it at this moment and pictured it whole in tiles, which part of it was on the screen, and what the recreation lacked; " +
        "and a model's descriptions of the groups of tiles not already in the visit's knowledge, with each tile's reference. " +
        "A group of tiles may be described as a model described the same pictures at an earlier moment, where the recorder found them the same, pixel for pixel, at the same places: " +
        "that description cites the earlier moment's tiles, which are the same pictures as this moment's. " +
        "The knowledge and the descriptions are inferences, not facts. " +
        "A tile is a recreation, not the page itself: where the recording lacks something, such as an image that was not loaded, the tile lacks it too, so say that it is missing in the recreation, not that the page lacked it. " +
        "Add what the web page adds that the frame does not show: content that was not on the screen, and the interactions, dialogs, menus, widgets, forms, messages, notices, alerts, toasts, and errors " +
        "that appeared during the visit, however small, each with where it is on the page and whether it was in the part on the screen. " +
        "Every statement from a description of tiles cites the tiles it describes. " +
        ParticipantRules + LayerAnswer;

    public const string PageChapterInstructions =
        "You write the description of one web page for the chapter on the pages of a professional report for accessibility and usability engineers on an accessibility test recording. " +
        "The chapter describes each page from everything known of it from every visit to it, independent of the participant's journey, which the chapter on the user experience tells. " +
        "You are given the page's address and title, the visits to it with what the recorder derived of them, the page's changes during each visit as the recorder found them, " +
        "each beginning with an evidence reference such as [E3]: its live regions, its popups opening and closing, NVDA's windows, and its scrolls; and each visit's knowledge in layers: " +
        "layer 1 from the recorded data, layer 2 from captured frames of the screen, and layer 3 from the web page as the recorder rebuilt it, each statement with the evidence references it rests on, such as [E3]. " +
        "The knowledge is inference, not fact. " +
        "Describe the page as an engineer needs to know it, as an overview and not a list of every element, in prose whose sentences connect, each paragraph one topic. " +
        "Take your terms from the page's own: its headings, landmarks, and the names of its controls. " +
        "Describe the page, not the participant. Where the knowledge disagrees, say so, citing both, and do not settle it. " +
        "Describe only what the knowledge supports, name sites and pages only as they are named, and say that something is unknown where it does not settle it. " +
        "Answer in JSON with five sections, each a subsection of the page's description that the recorder heads, so that each holds only its own topic: " +
        "\"purpose\", what the page is for and who it appears to be for; " +
        "\"layout\", the page's own regions from top to bottom, with their landmarks, and its imagery, colour, and emphasis: the page alone, not the screen, the browser's window, or other programs' windows; " +
        "\"content\", what its regions hold: its headings, sections, text, and images, as an overview; " +
        "\"controls\", the controls, links, forms, dialogs, menus, widgets, and messages it has or showed, with where they are and how they behave; " +
        "and \"changes\", what appeared, went, moved, or changed on the page during the visits, in order, each with when, from the recorder's changes and the knowledge. " +
        "Do not repeat in one section what another holds. " +
        VisualAndSemantics + Noticing + EvidenceRules +
        "What the rebuilt page lacks, such as an image that was not recorded, is not part of the page: do not describe it as the page's content. " +
        "Each section is a list of paragraphs, and each paragraph a list of statements. Each section holds only its own paragraphs. " + ParagraphRule +
        "A statement is {\"text\": ..., \"kind\": \"claim\", \"evidence\": [...]} citing every reference it rests on, " +
        "or {\"text\": ..., \"kind\": \"unknown\", \"evidence\": []}. " + UnknownIsUnknown + "Cite only references that appear in the input. " + CiteWithoutBrackets + ReferencesOutOfText + "Write plain sentences without Markdown.";

    /// <summary>How chapter 4's answers are given, said in its templates.</summary>
    public const string StoryAnswer =
        "Write clean, readable prose for a professional report. The recorder gives every heading; give none. " +
        "Show a picture where it helps the reader see a layout, a pattern, or content you describe: a captured frame for what was on the screen, or a tile for part of the page as the recorder rebuilt it. " +
        "Take your terms and your structure from the pages' own: their headings, landmarks, and the names of their controls. " +
        ParagraphRule +
        "Each section is a list of paragraphs, each paragraph a list of statements, and each statement has a form: " +
        "\"sentence\", a sentence of prose; " + ListForms +
        "or \"picture\", a picture the recorder puts in, its text the picture's caption, citing the one captured frame or tile it shows and what the caption rests on. " +
        "A statement is {\"form\": ..., \"text\": ..., \"kind\": \"claim\", \"evidence\": [...]} citing every reference it rests on, " +
        "or {\"form\": ..., \"text\": ..., \"kind\": \"unknown\", \"evidence\": []}. " + UnknownIsUnknown + "Cite only references that appear in the input. " + CiteWithoutBrackets + ReferencesOutOfText +
        "Write plain sentences without Markdown.";

    /// <summary>
    /// The profile's sampling options the steps that write the report leave
    /// out: the presence penalty, kept for the steps that describe a moment,
    /// where answers looped. Sent with the story of visit 1 on the target
    /// machine on 2026-10-17, it gave every section empty; the same request
    /// without it gave 27 statements (owner, the same day).
    /// </summary>
    public static readonly IReadOnlySet<string> WritingLeavesOut = new HashSet<string>(StringComparer.Ordinal) { "presence_penalty" };

    public const string StoryInstructions =
        "You write one part of the chapter on the user experience of a professional report for accessibility and usability engineers on an accessibility test recording: the story of the user experience. " +
        "This part is one page visit. You are given the participant's capabilities; the recorder's facts about the visit, each beginning with an evidence reference such as [E3], " +
        "with the page's changes during it in time order: its live regions, its popups opening and closing, NVDA's windows, and its scrolls; " +
        "the visit in time order, each input of the participant with what the recorder found after it and before the next; " +
        "the visit's knowledge in layers: layer 1 from the recorded data, layer 2 from captured frames of the screen, and layer 3 from the web page as the recorder rebuilt it, each statement with the references it rests on; " +
        "the moments of the visit, each with its captured frame's reference and its tiles' references; which section of the chapter on the pages describes the visit's page; " +
        "the changes during the visit to the accessibility settings, the assistive technology, and the Magnifier's view, found by the recorder, each with its references; " +
        "and the story so far, the parts written for the visits before. The knowledge is inference, not fact. " +
        "Tell what happened during the visit and what the experience was, in three subsections that the recorder heads, so that each holds only its own topic. " +
        "Tell each change to the settings or the assistive technology where it happened in the story, and what followed it as far as the evidence shows. " +
        "Where the Magnifier panned, say which part of the screen it showed, from the parts given: that is what was on the screen, not necessarily what the participant looked at. " +
        "Follow on from the story so far without repeating it, and do not describe the page again: refer to its section of the chapter on the pages. " +
        "Describe only what these support, name sites, programs, windows, and pages only as they are named, and say that something is unknown where they do not settle it. " +
        ParticipantRules + EvidenceRules + RecreationIsEvidence + VisualAndSemantics + Noticing +
        "Answer in JSON with three sections: \"arriving\", how the participant came to the page and what was on the screen when they arrived; " +
        "\"actions\", what the participant did, in order, as numbered steps taken from the visit in time order: each step an input and what the recorder found after it, citing both, " +
        "and where nothing was found after it, saying so; inputs one after another with the same kind of response may be one step; " +
        "and \"experience\", what worked and what got in the way for this participant, interpreted: what helped or hindered them and why it matters, with the facts as support, " +
        "without restating the steps, the scrolls, or what was on the screen. " +
        "Do not repeat in one section what another holds. " + StoryAnswer;

    public const string OpeningInstructions =
        "You write the opening of the chapter on the user experience of a professional report for accessibility and usability engineers on an accessibility test recording: the story of the user experience. " +
        "You are given the participant's capabilities; the page visits, as the recorder derived them; the pages, as the chapter on the pages names them; " +
        "the changes to the accessibility settings, the assistive technology, and the Magnifier's view before the first visit, found by the recorder, each with its references; " +
        "and the chapter's parts, one for each visit, which follow the opening. " +
        "Each part's statements cite the evidence references they rest on, such as [E3]; the parts are inferences, not facts. " +
        "Introduce the story in one or two paragraphs: the site tested and who it appears to be for, the changes before the first visit, and the course of the session in brief, " +
        "so that the parts that follow read as one story. The participant, their capabilities, and their assistive technology are described in the executive summary and the chapter on the participant: " +
        "refer to them in a phrase, and do not describe them again. Do not repeat the parts. The settings at the start are given in the report's appendices; do not list them. " +
        "Describe only what these support, name sites, programs, windows, and pages only as they are named, and say that something is unknown where they do not settle it. " +
        ParticipantRules + EvidenceRules + RecreationIsEvidence +
        "Answer in JSON with one section, \"opening\", the introduction, which the recorder heads. " + StoryAnswer;

    public const string ExecutiveInstructions =
        "You write the executive summary, the first chapter, of a professional report for accessibility and usability engineers on an accessibility test recording. " +
        "You are given the participant's capabilities; the assistive technology at the start and the accessibility settings at the start, found by the recorder, each with whether it is at its default; " +
        "the changes to them during the recording; the page visits and the pages; and the chapter on the user experience, which tells the journey. " +
        "Each is given with the evidence references it rests on, such as [E3]; the chapter's statements are inferences, not facts. " +
        "Write as the executive summary of a professional report is written: interpret and explain. State a judgement first and then explain it, with its reasons from the evidence and the caveats that temper it. " +
        "Say what the facts mean and why they matter, for this participant and for the engineers who will act on the report, not the facts alone; the chapters and appendices give the facts. " +
        "Judge how the journey went for this participant, where the experience worked and where it got in the way, and why, as the evidence and the chapter on the user experience show; " +
        "do not grade the site, rank findings, or map them to WCAG, which the report does not yet do. " +
        "A setting whose default is unknown may still be significant, such as a text size above 100 percent or a contrast theme in use: name it with its value, and do not say whether it is the default. " +
        SettingsRules +
        "Describe only what these support, name sites, programs, windows, and pages only as they are named, and say that something is unknown where they do not settle it. " +
        ParticipantRules + EvidenceRules +
        "What the rebuilt page lacks is a limit of the evidence: give it, if at all, in what the evidence does not show, not in the journey. " +
        "Answer in JSON with four sections, each a subsection of the summary that the recorder heads, so that each holds only its own topic: " +
        "\"overview\", the overall picture in one paragraph: who the participant is, what they did, and your judgement of how it went, with its main reason; " +
        "\"participant\", the participant and their set-up: the assistive technology they used and the significant accessibility settings in use and their changes, and what these mean for how the participant works and for what the report can say; " +
        "\"journey\", the journey: what the participant did and how it went, where it worked and where it got in the way, and why it matters; " +
        "and \"evidence\", what the evidence shows and does not show: how much the recording covers, what was inferred rather than recorded, and what is unknown, and how that limits the judgement. " +
        "Do not repeat in one section what another holds. " +
        "Each section is a list of paragraphs, each a list of statements. " + ParagraphRule +
        "Write prose by default. Each statement has a form: \"sentence\", a sentence of prose; \"bullet\", a point of a list whose order does not matter, its text the point and why it matters, in two or three sentences, never a bare fact; " +
        "or \"numbered\", a step of a sequence whose order matters, which the recorder numbers. Use a list only where its points are distinct and each needs explaining; otherwise write sentences. " +
        "A statement is {\"form\": ..., \"text\": ..., \"kind\": \"claim\", \"evidence\": [...]} citing every reference it rests on, " +
        "or {\"form\": ..., \"text\": ..., \"kind\": \"unknown\", \"evidence\": []}. " + UnknownIsUnknown + "Cite only references that appear in the input. " + CiteWithoutBrackets + ReferencesOutOfText +
        "Write clean, readable prose for a professional report, without Markdown.";
    private readonly Func<string, string, CancellationToken, Task<(Recorder.Session.SessionPlaybackArchive Archive, RecordingFileDocuments Documents)>> _open;

    public RecordingSummaryVisionKind()
        : this(OpenAsync)
    {
    }

    /// <param name="open">Opens a recording file, given the session folder and the file's path.</param>
    public RecordingSummaryVisionKind(Func<string, string, CancellationToken, Task<(Recorder.Session.SessionPlaybackArchive Archive, RecordingFileDocuments Documents)>> open)
    {
        _open = open;
    }

    public string Kind => Name;

    // Version 2 is step 4b: the recreated page's tiles, and the combined
    // description of each moment. Version 3 is the layered report: the
    // layers' additions at each moment, and the report's two chapters
    // (recording-summary.md, "The layered report").
    public int Version => 3;

    /// <summary>The summary's collation limits, which must be the summary's own for its references to match.</summary>
    public CollationSettings Collation { get; init; } = CollationSettings.Default;

    public MomentLimits Moments { get; init; } = MomentLimits.Default;

    public int ImageTokensPerRequest { get; init; } = DefaultImageTokensPerRequest;

    /// <summary>
    /// Makes what recreates each moment's page, from the recording's
    /// documents and archive. By default the instrumented Chromium where
    /// the player finds it, without a window, its recreations under the
    /// worker's folder.
    /// </summary>
    public Func<RecordingFileDocuments, Recorder.Session.SessionPlaybackArchive, IMomentPages> Pages { get; init; } =
        (documents, archive) => new RecordedMomentPages(
            documents,
            archive,
            Recorder.Recreation.RecreationBrowser.DefaultExecutablePath(AppContext.BaseDirectory),
            Path.Combine(AnalysisLocation.Directory, "recreations"));

    private static async Task<(Recorder.Session.SessionPlaybackArchive, RecordingFileDocuments)> OpenAsync(
        string sessionDirectory,
        string filePath,
        CancellationToken cancellationToken)
    {
        var opened = await RecordingFilePlayback.OpenAsync(sessionDirectory, filePath, cancellationToken).ConfigureAwait(false);
        return (opened.Archive, opened.Documents ?? throw new StepFailedException("The recording file's browser documents could not be read."));
    }

    public const string MomentInstructions =
        "You describe one moment of a page visit from an accessibility test recording, for an accessibility auditor. " +
        "You are given a captured frame, a picture of the whole screen at that moment, labelled with its evidence reference; " +
        "why the recorder chose the moment; and the recorder's facts about the visit up to that moment, each beginning with an evidence reference such as [E3]. " +
        "Describe only what the picture and the facts support. Name sites, programs, windows, and pages only as the picture or the facts name them. " +
        "Where they do not settle something, say that it is unknown; do not guess. " +
        "A captured frame shows what was on the screen, not what the participant saw: say that the participant saw something only if the participant's capabilities, " +
        "as given, say that they work by sight; otherwise do not say what the participant saw. " +
        "What NVDA had to announce is the recorder's inference from the page, not a recording of speech: never say that NVDA said it or that the participant heard it. " +
        "Assistive technology observed running shows what was running, not what the participant is able to do. " +
        "Typed text is test data from short-lived test accounts and may be quoted as recorded. " +
        "Summarise what is on the screen as an overview; do not list each control, label, or time shown. " +
        "Answer in JSON with two sections: \"screen\", what was on the screen: the page and its layout, imagery, colour, and emphasis, " +
        "what was open, such as a dialog, a menu, or a popup, which part of the page was in view, and any other window in view; " +
        "and \"change\", what changed at this moment, as the reasons and facts give it, and what the picture shows of it. " +
        "Each section is a list of paragraphs, and each paragraph a list of statements. Each section holds only its own paragraphs. " +
        "A statement is {\"text\": ..., \"kind\": \"claim\", \"evidence\": [...]} citing every reference it rests on, " +
        "with every statement about the picture citing the picture's reference, " +
        "or {\"text\": ..., \"kind\": \"unknown\", \"evidence\": []} for something the picture and facts leave open. " +
        "Cite only references that appear in the input. Write plain sentences without Markdown. " +
        "Write the JSON on one line, with no indentation and no line breaks.";

    public const string PageInstructions =
        "You describe part of a web page from an accessibility test recording, for an accessibility auditor. " +
        "You are given tiles: pictures of the page as the recorder rebuilt it from what it recorded at one moment, top to bottom, " +
        "each labelled with its evidence reference and its place on the page in CSS pixels; which part of the page was on the screen at that moment; " +
        "why the recorder chose the moment; and the recorder's facts about it, each beginning with an evidence reference such as [E3]. " +
        "A tile is a recreation, not the page itself: where the recording lacks something, such as an image that was not loaded, the tile lacks it too, " +
        "so say that it is missing in the recreation, not that the page lacked it. " +
        "Describe only what the tiles show. Do not restate the facts you are given, such as the page's address, title, or size, its scroll position, " +
        "which tiles were on the screen, the images not loaded, or the requests refused: the moment's description is given them directly. " +
        "Describe the page in these tiles as an overview, top to bottom: its regions, headings, imagery, colour, and emphasis, " +
        "and every message, notice, alert, toast, error, dialog, or popup, however small, whether or not it was in the part on the screen. " +
        "For each, say where it is on the page and whether it was in the part on the screen. " +
        "Describe only what the tiles and the facts support. Name sites and pages only as the tiles or the facts name them. " +
        "Where they do not settle something, say that it is unknown; do not guess. " +
        "Never say what the participant saw, heard, or did from the tiles: they show the page, not the participant. " +
        "Do not list each control, link, or line of text. " +
        ItemNamed +
        "Answer in JSON with one section: \"page\", what these tiles show of the page. " +
        "The section is a list of paragraphs, and each paragraph a list of statements. " +
        "A statement is {\"text\": ..., \"kind\": \"claim\", \"evidence\": [...]} citing every reference it rests on, " +
        "with every statement about a tile citing that tile's reference, " +
        "or {\"text\": ..., \"kind\": \"unknown\", \"evidence\": []} for something the tiles and facts leave open. " +
        "Cite only references that appear in the input. Write plain sentences without Markdown. " +
        "Write the JSON on one line, with no indentation and no line breaks.";


    [GeneratedRegex(@"^\[(E\d+)\] (\d\d):(\d\d):(\d\d)\.(\d\d\d) ")]
    private static partial Regex TimedLine();

    [GeneratedRegex(@"\[(E\d+)\]")]
    private static partial Regex Reference();

    public async Task RunAsync(AnalysisTaskContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Task.AfterTaskId is not { } summaryTask)
        {
            throw new StepFailedException("Vision is added to a summary of the recording, and this task names none. Summarise the recording first.");
        }

        var (archive, documents, session, filePath) = await RecordingSummaryKind.OpenRecordingAsync(context, _open, cancellationToken).ConfigureAwait(false);
        try
        {
            var evidence = new RecordedSummaryEvidence(archive, documents, filePath);
            await DescribeAsync(context, summaryTask, session, evidence, evidence, Pages(documents, archive), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            (archive.Timeline as IDisposable)?.Dispose();
        }
    }

    internal async Task DescribeAsync(
        AnalysisTaskContext context,
        Guid summaryTask,
        string session,
        ISummaryEvidence evidence,
        IMomentEvidence momentEvidence,
        IMomentPages pages,
        CancellationToken cancellationToken)
    {
        var task = context.Task;
        var capabilities = await RecordingSummaryKind.CapabilitiesAsync(context, cancellationToken).ConfigureAwait(false);
        var collation = SummaryCollator.Collate(evidence, capabilities, Collation);
        if (collation.Visits.Count == 0)
        {
            throw new StepFailedException("The recording has no page visits, so there is nothing to add vision to.");
        }

        // The summary's descriptions are used only if they were written
        // from the same facts, with the same references.
        var described = new List<(VisitCollation Visit, AcceptedStep Step)>();
        foreach (var visit in collation.Visits)
        {
            var key = RecordingSummaryKind.VisitStepKey(visit);
            var given = await context.Catalog.AcceptedInputReferencesAsync(summaryTask, key, cancellationToken).ConfigureAwait(false);
            var step = await context.Catalog.AcceptedStepAsync(summaryTask, key, RecordingSummaryKind.VisitSections, cancellationToken).ConfigureAwait(false);
            if (given is null || step is null)
            {
                throw new StepFailedException($"The summary has no accepted description of visit {visit.Visit.Number}, so vision cannot be added to it.");
            }

            var now = RecordingSummaryKind.VisitReferences(collation, visit);
            if (!now.SetEquals(given))
            {
                // How they differ, so that a failure says what changed.
                static string Listed(IEnumerable<string> references)
                {
                    var list = references.OrderBy(reference => int.TryParse(reference.AsSpan(1), out var number) ? number : int.MaxValue).ToList();
                    return list.Count == 0 ? "none" : string.Join(", ", list);
                }

                throw new StepFailedException(
                    $"The recorder's facts for visit {visit.Visit.Number} are not the ones its summary was written from, so vision cannot be added to that summary. " +
                    $"The summary was given {given.Count.ToString(CultureInfo.InvariantCulture)} references and the recorder now gives " +
                    $"{now.Count.ToString(CultureInfo.InvariantCulture)}; only now: {Listed(now.Except(given))}; only then: {Listed(given.Except(now))}. " +
                    "Add vision again: it summarises the recording again first.");
            }

            described.Add((visit, step));
        }

        var chosen = collation.Visits.ToDictionary(visit => visit.Visit.Number, visit => MomentChooser.Choose(evidence, momentEvidence, visit.Visit, Moments));
        if (chosen.Values.All(moments => moments.Moments.Count == 0))
        {
            throw new StepFailedException("No captured frames were recorded during the page visits, so there is nothing to add vision from.");
        }

        var register = collation.Evidence;

        // Each unique picture kept once for the recording, the task's index
        // of its tiles' pictures and of its moment records, and each group
        // of tiles described so far in the recording, by its tiles' places
        // and keys (recording-summary.md, "Unique images across the
        // recording" and "The layered report").
        var store = new ImageStore(session, context.ImagesFolder);
        var index = new List<ImageIndexEntry>();
        var indexFile = Path.Combine([session, .. context.ImageIndexFile.Split('/')]);
        var records = new List<MomentRecord>();
        var recordsFile = Path.Combine([session, .. context.MomentIndexFile.Split('/')]);
        var groupsDescribed = new Dictionary<string, DescribedGroup>(StringComparer.Ordinal);
        var pictures = new Dictionary<string, ReportPicture>(StringComparer.Ordinal);
        var layersNotSent = new List<string>();
        var visitsSeen = new List<(VisitCollation Visit, IReadOnlyList<MomentDescription> Moments, int LeftOut, Knowledge Knowledge)>();
        foreach (var (visit, summary) in described)
        {
            var moments = chosen[visit.Visit.Number];
            var descriptions = new List<MomentDescription>();
            var previous = visit.Visit.Start.Time - 1;

            // The visit's knowledge starts as layer 1, its description from
            // the recorded data, and grows by each layer's additions.
            var knowledge = Knowledge.FromRecorded(summary.Claims);
            var groupsKnown = new HashSet<string>(StringComparer.Ordinal);
            foreach (var moment in moments.Moments)
            {
                var frame = moment.Frame;
                var frameReference = register.Add(frame.Time, FrameText(frame), null, $"frame:{frame.Time.ToString(CultureInfo.InvariantCulture)}");
                pictures[frameReference] = new ReportPicture(frameReference, frame.Path, $"The captured frame at {SummaryCollator.Time(frame.Time)}");
                var reasonReferences = moment.Reasons.Select(reason => register.Add(
                    reason.Time,
                    "Derived by the recorder, a reason for a moment: " + reason.Text,
                    reason.EventId,
                    $"moment:{visit.Visit.Number}:{reason.Kind}:{reason.Time.ToString(CultureInfo.InvariantCulture)}")).ToList();
                var (input, references) = MomentInput(collation, visit, moment, frameReference, reasonReferences, previous);
                var key = $"{RecordingSummaryKind.VisitStepKey(visit)}-moment-{moment.Number.ToString("D2", CultureInfo.InvariantCulture)}";
                var frameStep = await context.RunStepAsync(
                    key,
                    new ModelRequest(MomentTemplate, MomentTemplateVersion, MomentInstructions, input,
                        [new ModelImage(frameReference, MediaType(frame.AbsolutePath), await ReadFrameAsync(frame, cancellationToken).ConfigureAwait(false))],
                        MomentSections, references),
                    cancellationToken).ConfigureAwait(false);

                // The page at the moment's frame, recreated and pictured
                // whole, then described in groups of tiles.
                var relativeFolder = context.PicturesFolder;
                var page = await pages.RecreateAsync(
                    visit.Visit, moment, Path.Combine([session, .. relativeFolder.Split('/')]), relativeFolder, key, cancellationToken).ConfigureAwait(false);
                MomentDescription momentDescribed;
                IReadOnlyList<PageGroupDescription> groups = [];
                IReadOnlyList<StoredImage> images = [];
                if (page.Tiles.Count > 0)
                {
                    (page, images) = await StoreTilesAsync(store, page, cancellationToken).ConfigureAwait(false);
                    var tiles = page.Tiles;
                    index.AddRange(tiles.Select((tile, at) => new ImageIndexEntry(
                        visit.Visit.Number, moment.Number, frame.Time, ImageIndex.Clock(frame.Time), tile.Number, tile.Top, tile.Height, tile.Width,
                        tile.PixelWidth, tile.PixelHeight, images[at].Key, images[at].Basis, images[at].Path)));
                    await ImageIndex.WriteAsync(indexFile, index, cancellationToken).ConfigureAwait(false);
                    (momentDescribed, groups) = await DescribeTilesAsync(
                        context, collation, visit, moment, key, frameReference, reasonReferences, frameStep, page,
                        [.. images.Select(image => image.Key)], groupsDescribed, cancellationToken).ConfigureAwait(false);
                    foreach (var tile in page.Tiles)
                    {
                        var reference = momentDescribed.TileReferences![tile.Number - 1];
                        pictures[reference] = new ReportPicture(reference, tile.Path,
                            string.Create(CultureInfo.InvariantCulture, $"Tile {tile.Number} of {page.Tiles.Count} of the page as the recorder rebuilt it at {SummaryCollator.Time(frame.Time)}"));
                    }
                }
                else
                {
                    momentDescribed = new MomentDescription(moment, frameReference, reasonReferences, frameStep, page, []);
                }

                descriptions.Add(momentDescribed);
                records.Add(Record(visit, momentDescribed, key, images, groups));
                await MomentIndex.WriteAsync(recordsFile, records, cancellationToken).ConfigureAwait(false);

                // Layer 2: what the frame adds to the visit's knowledge.
                var (layer2Input, layer2References) = Layer2Input(collation, visit, momentDescribed, FactsSince(visit, previous, frame.Time), knowledge);
                var layer2 = await context.RunStepAsync(
                    $"{key}-layer-2",
                    new ModelRequest(Layer2Template, LayerTemplateVersion, Layer2Instructions, layer2Input, [], Knowledge.LayerSections, layer2References)
                    {
                        ChecksWording = true
                    },
                    cancellationToken).ConfigureAwait(false);
                knowledge.Add(layer2.Claims, 2, moment.Number);

                // Layer 3: what the web page adds, from the groups of tiles
                // not yet in the visit's knowledge.
                if (momentDescribed.Recreated)
                {
                    var unknown = groups.Where(group => !groupsKnown.Contains(group.Key)).ToList();
                    if (unknown.Count == 0)
                    {
                        var reason = "every group of its page's tiles is the same pictures at the same places as a group already in the visit's knowledge, so the web page adds nothing to it.";
                        context.StepNotSent($"{key}-layer-3", reason);
                        layersNotSent.Add($"{key}-layer-3");
                    }
                    else
                    {
                        var (layer3Input, layer3References) = Layer3Input(collation, visit, momentDescribed, groups, groupsKnown, knowledge);
                        var layer3 = await context.RunStepAsync(
                            $"{key}-layer-3",
                            new ModelRequest(Layer3Template, LayerTemplateVersion, Layer3Instructions, layer3Input, [], Knowledge.LayerSections, layer3References)
                            {
                                ChecksWording = true
                            },
                            cancellationToken).ConfigureAwait(false);
                        knowledge.Add(layer3.Claims, 3, moment.Number);
                        groupsKnown.UnionWith(unknown.Select(group => group.Key));
                    }
                }

                previous = frame.Time;
            }

            visitsSeen.Add((visit, descriptions, moments.LeftOut, knowledge));
        }

        // Chapter 1: each page, a URL visited, from every visit to it.
        var chapterPages = new List<PageChapter>();
        foreach (var byUrl in visitsSeen.GroupBy(seen => seen.Visit.Visit.Start.Url, StringComparer.Ordinal))
        {
            var number = chapterPages.Count + 1;
            var visitsTo = byUrl.ToList();
            var (pageInput, pageReferences) = PageChapterInput(collation, number, visitsTo.Select(seen => (seen.Visit, seen.Knowledge)).ToList());
            var pageStep = await context.RunStepAsync(
                $"page-{number.ToString("D2", CultureInfo.InvariantCulture)}",
                new ModelRequest(PageChapterTemplate, PageChapterTemplateVersion, PageChapterInstructions, pageInput, [], PageChapterSections, pageReferences)
                {
                    SamplingLeftOut = WritingLeavesOut, ChecksWording = true
                },
                cancellationToken).ConfigureAwait(false);
            chapterPages.Add(new PageChapter(number, byUrl.Key, visitsTo.Select(seen => seen.Visit.Title).LastOrDefault(title => title is { Length: > 0 }),
                [.. visitsTo.Select(seen => seen.Visit.Visit.Number)], pageStep));
        }

        // The settings at the start and their changes, read by the recorder,
        // their records added to the evidence after every step before the
        // story, so that those steps are given the same input as before
        // (recording-summary.md, "The executive summary and the settings in
        // use").
        var settings = evidence.Settings(collation.Visits[0].Visit.Start.Time);
        var settingReferences = RegisterSettings(register, settings);
        var changesOf = SettingChangesByVisit(collation, settings);

        // Chapter 3: the story, one part for each visit, then its opening.
        var parts = new Dictionary<int, VisitVision>();
        var storySoFar = new List<(VisitCollation Visit, AcceptedStep Step)>();
        foreach (var (visit, moments, leftOut, knowledge) in visitsSeen)
        {
            var section = chapterPages.First(pageOf => pageOf.Visits.Contains(visit.Visit.Number)).Number;
            var (storyInput, storyReferences) = StoryInput(collation, visit, moments, leftOut, knowledge, section, storySoFar,
                changesOf.GetValueOrDefault(visit.Visit.Number) ?? [], settingReferences);
            var story = await context.RunStepAsync(
                $"{RecordingSummaryKind.VisitStepKey(visit)}-story",
                new ModelRequest(StoryTemplate, StoryTemplateVersion, StoryInstructions, storyInput, [], StorySections, storyReferences)
                {
                    Forms = StoryForms, SamplingLeftOut = WritingLeavesOut, NeedsStatements = true, ChecksWording = true
                },
                cancellationToken).ConfigureAwait(false);
            storySoFar.Add((visit, story));
            parts[visit.Visit.Number] = new VisitVision(moments, leftOut, knowledge, story);
        }

        var (openingInput, openingReferences) = OpeningInput(collation, chapterPages, storySoFar, changesOf.GetValueOrDefault(0) ?? [], settingReferences);
        var opening = await context.RunStepAsync(
            "story-opening",
            new ModelRequest(OpeningTemplate, StoryTemplateVersion, OpeningInstructions, openingInput, [], OpeningSections, openingReferences)
            {
                Forms = StoryForms, SamplingLeftOut = WritingLeavesOut, NeedsStatements = true, ChecksWording = true
            },
            cancellationToken).ConfigureAwait(false);

        // Chapter 1: the executive summary, written last.
        var (executiveInput, executiveReferences) = ExecutiveInput(collation, chapterPages, opening, storySoFar, settings, settingReferences);
        var executive = await context.RunStepAsync(
            "executive-summary",
            new ModelRequest(ExecutiveTemplate, ExecutiveTemplateVersion, ExecutiveInstructions, executiveInput, [], ExecutiveSections, executiveReferences)
            {
                Forms = ExecutiveForms, SamplingLeftOut = WritingLeavesOut, NeedsStatements = true, ChecksWording = true
            },
            cancellationToken).ConfigureAwait(false);

        var recording = await context.Catalog.RecordingAsync(task.RecordingId, cancellationToken).ConfigureAwait(false);
        var attempts = await context.Catalog.AttemptsAsync(task.TaskId, cancellationToken).ConfigureAwait(false);
        var report = LayeredReport.Write(
            recording?.SessionKey ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(session)),
            collation,
            capabilities,
            attempts,
            Collation,
            DateTime.UtcNow,
            new VisionReportParts(parts, chapterPages, opening, pictures, Moments, summaryTask, ImageTokensPerRequest, context.ImagesFolder, context.MomentIndexFile, layersNotSent)
            {
                Executive = executive,
                Settings = settings,
                SettingReferences = settingReferences
            });
        await RecordingSummaryKind.WriteReportAsync(context, session, FileName, report, cancellationToken).ConfigureAwait(false);
    }

    // A moment's record for the index: its frame and the step that
    // describes it, and its page with its tiles and groups.
    private static MomentRecord Record(
        VisitCollation visit, MomentDescription moment, string key, IReadOnlyList<StoredImage> images, IReadOnlyList<PageGroupDescription> groups)
    {
        var frame = moment.Moment.Frame;
        var page = moment.Page;
        var tiles = moment.Recreated
            ? page!.Tiles.Select((tile, at) => new MomentRecordTile(
                tile.Number, tile.Top, tile.Height, images[at].Key, images[at].Path, moment.TileReferences![at], InView(page, tile))).ToList()
            : [];
        var recordGroups = groups.Select((group, at) =>
        {
            var stepKey = $"{key}-page-{(at + 1).ToString("D2", CultureInfo.InvariantCulture)}";
            return group.Earlier is { } earlier
                ? new MomentRecordGroup(at + 1, group.Tiles[0].Number, group.Tiles[^1].Number, earlier.StepKey, false, earlier.Visit, earlier.Moment)
                : new MomentRecordGroup(at + 1, group.Tiles[0].Number, group.Tiles[^1].Number, stepKey, true);
        }).ToList();
        return new MomentRecord(
            visit.Visit.Number,
            moment.Moment.Number,
            frame.Time,
            SummaryCollator.Time(frame.Time),
            [.. moment.Moment.Reasons.Select(reason => reason.Text)],
            frame.Path,
            moment.FrameReference,
            key,
            new MomentRecordPage(
                moment.Recreated,
                page?.NotRecreated,
                page?.Width ?? 0,
                page?.Height ?? 0,
                page?.ScrollY ?? 0,
                page?.ViewportHeight ?? 0,
                tiles,
                recordGroups));
    }

    // A recreated page's tiles kept in the store, each file the recreation
    // wrote removed once its picture is kept, so that the evidence list and
    // the requests give the store's file (decisions 1 and 3).
    private static async Task<(RecreatedPage Page, IReadOnlyList<StoredImage> Images)> StoreTilesAsync(
        ImageStore store, RecreatedPage page, CancellationToken cancellationToken)
    {
        var tiles = new List<PageTile>();
        var images = new List<StoredImage>();
        var folders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tile in page.Tiles)
        {
            var png = await ReadFileAsync(tile.AbsolutePath, "tile", cancellationToken).ConfigureAwait(false);
            var image = await store.KeepAsync(png, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(Path.GetFullPath(tile.AbsolutePath), Path.GetFullPath(image.AbsolutePath), StringComparison.Ordinal))
            {
                Remove(tile.AbsolutePath);
                if (Path.GetDirectoryName(tile.AbsolutePath) is { } folder)
                {
                    folders.Add(folder);
                }
            }

            images.Add(image);
            tiles.Add(tile with { Path = image.Path, AbsolutePath = image.AbsolutePath });
        }

        foreach (var folder in folders)
        {
            try
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
#pragma warning disable CA1031 // A folder the recreation wrote is left if it cannot be removed.
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
#pragma warning restore CA1031
            {
            }
        }

        return (page with { Tiles = tiles }, images);

        static void Remove(string path)
        {
            try
            {
                File.Delete(path);
            }
#pragma warning disable CA1031 // A tile the recreation wrote is left if it cannot be removed; its picture is in the store.
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
#pragma warning restore CA1031
            {
            }
        }
    }

    /// <summary>
    /// A group of tiles as it is known across the recording: each tile's
    /// place, size, and image key, in order (decision 4).
    /// </summary>
    internal static string GroupKey(IReadOnlyList<PageTile> group, IReadOnlyList<string> keys) =>
        string.Join(';', group.Select(tile => string.Create(CultureInfo.InvariantCulture,
            $"{tile.Top:R},{tile.Height:R},{tile.Width:R},{tile.PixelWidth},{tile.PixelHeight},{keys[tile.Number - 1]}")));

    // A moment's recreated page described in groups of tiles, each group the
    // same pictures at the same places as a group described at an earlier
    // moment of the recording given that group's answer instead of being
    // sent (recording-summary.md, "Unique images across the recording",
    // decisions 4 and 5).
    private async Task<(MomentDescription Described, IReadOnlyList<PageGroupDescription> Groups)> DescribeTilesAsync(
        AnalysisTaskContext context,
        RecordingCollation collation,
        VisitCollation visit,
        Moment moment,
        string key,
        string frameReference,
        IReadOnlyList<string> reasonReferences,
        AcceptedStep frameStep,
        RecreatedPage page,
        IReadOnlyList<string> keys,
        Dictionary<string, DescribedGroup> groupsDescribed,
        CancellationToken cancellationToken)
    {
        var frame = moment.Frame;
        var register = collation.Evidence;
        var groups = TileGroups.Group(page.Tiles, ImageTokensPerRequest);
        var earlier = groups.Select(group => groupsDescribed.GetValueOrDefault(GroupKey(group, keys))).ToList();

        // The derived fact that a tile is the same picture as an earlier
        // moment's is in the tile's own entry, citing the earlier tile, so
        // that the evidence list's numbering, and so the requests of the
        // steps after it, are as they were (decision 5).
        var sameAs = new Dictionary<int, string>();
        for (var index = 0; index < groups.Count; index++)
        {
            if (earlier[index] is not { } match)
            {
                continue;
            }

            for (var at = 0; at < groups[index].Count; at++)
            {
                sameAs[groups[index][at].Number] = string.Create(CultureInfo.InvariantCulture,
                    $"Derived by the recorder: the same picture, pixel for pixel, as tile {match.Tiles[at].Number} [{match.TileReferences[at]}] of the page as rebuilt at moment {match.Moment} of visit {match.Visit}, at {SummaryCollator.Time(match.Time)}");
            }
        }

        var tileReferences = page.Tiles.Select(tile => register.Add(
            frame.Time, TileText(moment, page, tile) + (sameAs.TryGetValue(tile.Number, out var same) ? ". " + same : ""), null,
            $"tile:{frame.Time.ToString(CultureInfo.InvariantCulture)}:{tile.Number.ToString(CultureInfo.InvariantCulture)}")).ToList();
        var pageSteps = new List<PageGroupDescription>();
        var notSent = new List<GroupNotSent>();
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var groupKey = GroupKey(group, keys);
            var stepKey = $"{key}-page-{(index + 1).ToString("D2", CultureInfo.InvariantCulture)}";
            if (earlier[index] is { } match)
            {
                context.StepNotSent(stepKey, string.Create(CultureInfo.InvariantCulture,
                    $"its tiles are the same pictures at the same places as group {match.Group} at moment {match.Moment} of visit {match.Visit}, at {SummaryCollator.Time(match.Time)}, so that group's answer, step {match.StepKey}, is used."));
                pageSteps.Add(new PageGroupDescription(group, match.Step, groupKey, match));
                notSent.Add(new GroupNotSent(index + 1, group[0].Number, group[^1].Number, match.Visit, match.Moment, match.Time, match.Group, match.StepKey));
                continue;
            }

            var (pageInput, pageReferences) = PageInput(collation, visit, moment, page, group, tileReferences, reasonReferences, index + 1, groups.Count);
            var images = new List<ModelImage>();
            foreach (var tile in group)
            {
                images.Add(new ModelImage(tileReferences[tile.Number - 1], "image/png", await ReadFileAsync(tile.AbsolutePath, "tile", cancellationToken).ConfigureAwait(false)));
            }

            var pageStep = await context.RunStepAsync(
                stepKey,
                new ModelRequest(PageTemplate, PageTemplateVersion, PageInstructions, pageInput, images, PageSections, pageReferences),
                cancellationToken).ConfigureAwait(false);
            pageSteps.Add(new PageGroupDescription(group, pageStep, groupKey));
            groupsDescribed.TryAdd(groupKey, new DescribedGroup(
                visit.Visit.Number, moment.Number, frame.Time, index + 1, stepKey, group, [.. group.Select(tile => tileReferences[tile.Number - 1])], pageStep));
        }

        return (new MomentDescription(moment, frameReference, reasonReferences, frameStep, page, tileReferences, notSent), pageSteps);
    }

    // The visit and the moment, as the layers' requests give them.
    private static StringBuilder MomentHeader(RecordingCollation collation, VisitCollation visit, MomentDescription moment, HashSet<string> references)
    {
        references.UnionWith(RecordingSummaryKind.CapabilityReferences(collation));
        var input = new StringBuilder()
            .Append(string.Join('\n', collation.CapabilityLines)).Append("\n\n")
            .Append(CultureInfo.InvariantCulture, $"Page visit {visit.Visit.Number}, derived by the recorder: {visit.Visit.Start.Url}")
            .Append(visit.Title is { Length: > 0 } title ? $", titled \"{title.Replace("\"", "'", StringComparison.Ordinal)}\"" : ", with no title recorded")
            .Append(".\n\n");
        return input;
    }

    private static void AppendReasons(StringBuilder input, MomentDescription moment, HashSet<string> references)
    {
        input.Append(CultureInfo.InvariantCulture, $"Moment {moment.Moment.Number} of the visit, at {SummaryCollator.Time(moment.Moment.Frame.Time)}. Why the recorder chose it:\n");
        for (var index = 0; index < moment.Moment.Reasons.Count; index++)
        {
            var reason = moment.Moment.Reasons[index];
            references.Add(moment.ReasonReferences[index]);
            input.Append(CultureInfo.InvariantCulture, $"[{moment.ReasonReferences[index]}] {SummaryCollator.Time(reason.Time)} {reason.Text}\n");
        }
    }

    /// <summary>
    /// A moment's layer 2 request: the capabilities, the visit, its
    /// knowledge so far with each statement's number, why the moment was
    /// chosen, the recorder's facts since the moment before, and the
    /// captured frame's description.
    /// </summary>
    internal static (string Input, HashSet<string> References) Layer2Input(
        RecordingCollation collation, VisitCollation visit, MomentDescription moment, IReadOnlyList<string> facts, Knowledge knowledge)
    {
        var references = new HashSet<string>(StringComparer.Ordinal) { moment.FrameReference };
        var input = MomentHeader(collation, visit, moment, references);
        input.Append("The visit's knowledge so far, in layers. These are inferences, not facts:\n");
        knowledge.Append(input, references, withNumbers: true);
        input.Append('\n');
        AppendReasons(input, moment, references);
        input.Append(facts.Count == 0
            ? "\nThe recorder has no facts of the visit since the moment before.\n"
            : "\nThe recorder's facts of the visit since the moment before:\n" + string.Join('\n', facts) + "\n");
        foreach (var line in facts)
        {
            foreach (Match match in Reference().Matches(line))
            {
                references.Add(match.Groups[1].Value);
            }
        }

        input.Append(CultureInfo.InvariantCulture, $"\nThe captured frame: [{moment.FrameReference}] {FrameText(moment.Moment.Frame)}.\n")
            .Append("The frame as a model described it, from the picture and the recorder's facts. These are inferences, not facts:\n");
        RecordingSummaryKind.AppendClaims(input, moment.FrameStep.Claims, references);
        return (input.ToString(), references);
    }

    /// <summary>
    /// A moment's layer 3 request: the capabilities, the visit, its
    /// knowledge so far with each statement's number, why the moment was
    /// chosen, the page as rebuilt, the part on the screen, what the
    /// recreation lacked, and the descriptions of the groups of tiles not
    /// yet in the visit's knowledge; a group already in it is named, not
    /// described again.
    /// </summary>
    internal static (string Input, HashSet<string> References) Layer3Input(
        RecordingCollation collation,
        VisitCollation visit,
        MomentDescription moment,
        IReadOnlyList<PageGroupDescription> groups,
        IReadOnlySet<string> groupsKnown,
        Knowledge knowledge)
    {
        var page = moment.Page!;
        var tileReferences = moment.TileReferences!;
        var references = new HashSet<string>(tileReferences, StringComparer.Ordinal);
        var input = MomentHeader(collation, visit, moment, references);
        input.Append("The visit's knowledge so far, in layers. These are inferences, not facts:\n");
        knowledge.Append(input, references, withNumbers: true);
        input.Append('\n');
        AppendReasons(input, moment, references);
        input.Append(CultureInfo.InvariantCulture,
                $"\nThe page as the recorder rebuilt it at this moment, {page.Width:0} by {page.Height:0} CSS pixels, pictured whole in {page.Tiles.Count} tiles. ")
            .Append(InViewText(moment.Moment, page, tileReferences)).Append('\n')
            .Append(RecreationNotes(page)).Append('\n');
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var named = string.Join(", ", group.Tiles.Select(tile => $"tile {tile.Number} [{tileReferences[tile.Number - 1]}], CSS pixels {tile.Top.ToString("0", CultureInfo.InvariantCulture)} to {(tile.Top + tile.Height).ToString("0", CultureInfo.InvariantCulture)}"));
            if (groupsKnown.Contains(group.Key))
            {
                input.Append(CultureInfo.InvariantCulture,
                    $"\nThe recreated page at {named}: derived by the recorder, the same pictures, pixel for pixel, at the same places, as tiles already in the visit's knowledge, so they are not described again.\n");
                continue;
            }

            if (group.Earlier is { } earlier)
            {
                // A group not sent: its tiles are the same pictures as an
                // earlier moment's, whose description is given, citing that
                // moment's tiles.
                references.UnionWith(earlier.TileReferences);
                var then = string.Join(", ", earlier.Tiles.Select((tile, at) => $"tile {tile.Number} [{earlier.TileReferences[at]}]"));
                input.Append(CultureInfo.InvariantCulture,
                        $"\nThe recreated page at {named}: derived by the recorder, the same pictures, pixel for pixel, at the same places, as {then} of the page as rebuilt at moment {earlier.Moment} of visit {earlier.Visit}, at {SummaryCollator.Time(earlier.Time)}. ")
                    .Append("So they were not sent again, and this is the description a model wrote of them at that moment, citing that moment's tiles; what it says was on the screen is of that moment. These are inferences, not facts:\n");
            }
            else
            {
                input.Append(CultureInfo.InvariantCulture, $"\nThe recreated page as a model described it from {named}. These are inferences, not facts:\n");
            }

            RecordingSummaryKind.AppendClaims(input, group.Step.Claims, references);
        }

        return (input.ToString(), references);
    }

    /// <summary>
    /// A page's request for chapter 3: its address and titles, the visits to
    /// it with what the recorder derived of them, the page's changes during
    /// each, and each visit's knowledge in layers, without the statements'
    /// numbers.
    /// </summary>
    internal static (string Input, HashSet<string> References) PageChapterInput(
        RecordingCollation collation, int number, IReadOnlyList<(VisitCollation Visit, Knowledge Knowledge)> visits)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        var titles = visits.Select(seen => seen.Visit.Title).Where(title => title is { Length: > 0 }).Distinct(StringComparer.Ordinal).ToList();
        var input = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"Page {number} of the report's chapter on the pages, derived by the recorder: {visits[0].Visit.Visit.Start.Url}")
            .Append(titles.Count == 0 ? ", with no title recorded" : ", titled " + string.Join(", then ", titles.Select(title => $"\"{title!.Replace("\"", "'", StringComparison.Ordinal)}\"")))
            .Append(CultureInfo.InvariantCulture, $". It was visited {visits.Count} {(visits.Count == 1 ? "time" : "times")}.\n");
        foreach (var (visit, knowledge) in visits)
        {
            input.Append(CultureInfo.InvariantCulture,
                $"\nVisit {visit.Visit.Number}, derived by the recorder: from {SummaryCollator.Time(visit.Visit.Start.Time)} to {SummaryCollator.Time(visit.Visit.End)}.\n");
            input.Append(visit.ChangesText).Append('\n');
            foreach (var line in visit.Changes)
            {
                foreach (Match match in Reference().Matches(line))
                {
                    references.Add(match.Groups[1].Value);
                }
            }

            input.Append("The visit's knowledge, in layers. These are inferences, not facts:\n");
            knowledge.Append(input, references, withNumbers: false);
        }

        return (input.ToString(), references);
    }

    /// <summary>
    /// A visit's request for its part of chapter 4: the capabilities, the
    /// recorder's facts of the visit, its knowledge in layers, its moments
    /// with their frames' and tiles' references, the section of chapter 3
    /// that describes its page, and the story so far.
    /// </summary>
    internal static (string Input, HashSet<string> References) StoryInput(
        RecordingCollation collation,
        VisitCollation visit,
        IReadOnlyList<MomentDescription> moments,
        int leftOut,
        Knowledge knowledge,
        int section,
        IReadOnlyList<(VisitCollation Visit, AcceptedStep Step)> storySoFar,
        IReadOnlyList<SettingChange>? changes = null,
        IReadOnlyDictionary<string, string>? settingReferences = null)
    {
        var references = new HashSet<string>(RecordingSummaryKind.VisitReferences(collation, visit), StringComparer.Ordinal);
        var input = new StringBuilder(RecordingSummaryKind.VisitInput(collation, visit)).Append("\n\n");
        AppendTimeline(input, visit, references);
        input.Append(CultureInfo.InvariantCulture, $"The visit's page is described in the report's chapter on the pages, section 3.{section}.\n\n")
            .Append("The visit's knowledge, in layers. These are inferences, not facts:\n");
        knowledge.Append(input, references, withNumbers: false);
        input.Append(moments.Count == 0
            ? "\nThe visit has no moments: no captured frames were recorded during it.\n"
            : "\nThe visit's moments, chosen by the recorder, with the pictures a part may show:\n");
        foreach (var moment in moments)
        {
            references.Add(moment.FrameReference);
            input.Append(CultureInfo.InvariantCulture, $"Moment {moment.Moment.Number}, at {SummaryCollator.Time(moment.Moment.Frame.Time)}: the captured frame [{moment.FrameReference}]");
            if (moment.Recreated)
            {
                var page = moment.Page!;
                var tiles = page.Tiles.Select(tile =>
                {
                    references.Add(moment.TileReferences![tile.Number - 1]);
                    return string.Create(CultureInfo.InvariantCulture,
                        $"tile {tile.Number} [{moment.TileReferences![tile.Number - 1]}], CSS pixels {tile.Top:0} to {tile.Top + tile.Height:0}{(InView(page, tile) ? ", on the screen" : "")}");
                });
                input.Append("; the page as the recorder rebuilt it, in tiles: ").Append(string.Join("; ", tiles));
            }
            else
            {
                input.Append("; the page was not recreated");
            }

            input.Append(".\n");
        }

        if (leftOut > 0)
        {
            input.Append(CultureInfo.InvariantCulture, $"{leftOut} more moments were left out to keep within the limit.\n");
        }

        AppendChanges(input, "during the visit", changes ?? [], settingReferences, references);
        if (storySoFar.Count == 0)
        {
            input.Append("\nThe story so far: this is the first part.\n");
        }
        else
        {
            input.Append("\nThe story so far, the parts for the visits before. These are inferences, not facts:\n");
            foreach (var (before, step) in storySoFar)
            {
                input.Append(CultureInfo.InvariantCulture, $"The part for visit {before.Visit.Number}:\n");
                AppendStory(input, step, references);
            }
        }

        return (input.ToString(), references);
    }

    /// <summary>
    /// The visit's inputs in time order, each with what the recorder found
    /// after it and before the next, so that each numbered step is an
    /// action with its response (target report, 2026-10-18: the steps gave
    /// the inputs alone, their responses gathered in another subsection).
    /// </summary>
    internal static void AppendTimeline(StringBuilder input, VisitCollation visit, ISet<string> references)
    {
        if (visit.Timeline.Count == 0)
        {
            return;
        }

        input.Append("The visit in time order, derived by the recorder: each input of the participant, and under it what the recorder found after it and before the next input. ")
            .Append("What follows an input was found after it; that the input caused it is shown only where a fact says so:\n");
        var any = false;
        foreach (var item in visit.Timeline)
        {
            if (item.Input)
            {
                any = true;
                input.Append("Input: ").Append(item.Line).Append('\n');
            }
            else
            {
                input.Append(any ? "  then: " : "Before the first input: ").Append(item.Line).Append('\n');
            }

            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(item.Line, @"^\[(E\d+)\]"))
            {
                references.Add(match.Groups[1].Value);
            }
        }

        input.Append('\n');
    }

    /// <summary>
    /// Chapter 2's opening request: the capabilities, the visits, the pages
    /// as chapter 3 names them, and the parts that follow.
    /// </summary>
    internal static (string Input, HashSet<string> References) OpeningInput(
        RecordingCollation collation,
        IReadOnlyList<PageChapter> pages,
        IReadOnlyList<(VisitCollation Visit, AcceptedStep Step)> parts,
        IReadOnlyList<SettingChange>? changes = null,
        IReadOnlyDictionary<string, string>? settingReferences = null)
    {
        var references = new HashSet<string>(RecordingSummaryKind.CapabilityReferences(collation), StringComparer.Ordinal);
        var input = new StringBuilder()
            .Append(string.Join('\n', collation.CapabilityLines)).Append("\n\n")
            .Append("The page visits, derived by the recorder:\n");
        foreach (var (visit, _) in parts)
        {
            input.Append(CultureInfo.InvariantCulture,
                $"Visit {visit.Visit.Number}, from {SummaryCollator.Time(visit.Visit.Start.Time)} to {SummaryCollator.Time(visit.Visit.End)}: {visit.Visit.Start.Url}")
                .Append(visit.Title is { Length: > 0 } title ? $", titled \"{title.Replace("\"", "'", StringComparison.Ordinal)}\"" : ", with no title recorded")
                .Append(".\n");
        }

        input.Append("\nThe pages, as the chapter on the pages names them:\n");
        foreach (var page in pages)
        {
            input.Append(CultureInfo.InvariantCulture, $"Section 3.{page.Number}: {LayeredReport.PageTitle(page)}, visits {string.Join(", ", page.Visits)}.\n");
        }

        AppendChanges(input, "before the first visit", changes ?? [], settingReferences, references);
        input.Append("\nThe chapter's parts, which follow the opening. These are inferences, not facts:\n");
        foreach (var (visit, step) in parts)
        {
            input.Append(CultureInfo.InvariantCulture, $"The part for visit {visit.Visit.Number}:\n");
            AppendStory(input, step, references);
        }

        return (input.ToString(), references);
    }

    /// <summary>
    /// The executive summary's request: the capabilities and the assistive
    /// technology observed running, the settings at the start with whether
    /// each is at its default, every change, the visits and pages, and the
    /// chapter on the user experience.
    /// </summary>
    internal static (string Input, HashSet<string> References) ExecutiveInput(
        RecordingCollation collation,
        IReadOnlyList<PageChapter> pages,
        AcceptedStep opening,
        IReadOnlyList<(VisitCollation Visit, AcceptedStep Step)> parts,
        RecordedSettings settings,
        IReadOnlyDictionary<string, string> settingReferences)
    {
        var references = new HashSet<string>(RecordingSummaryKind.CapabilityReferences(collation), StringComparer.Ordinal);
        var input = new StringBuilder()
            .Append(string.Join('\n', collation.CapabilityLines)).Append("\n\n");
        void Rows(string title, IReadOnlyList<SettingRow> rows, string? note)
        {
            input.Append(title).Append(":\n");
            if (note is not null)
            {
                input.Append(note).Append('\n');
            }

            foreach (var row in rows)
            {
                var reference = settingReferences.GetValueOrDefault(row.Source.Key);
                if (reference is not null)
                {
                    references.Add(reference);
                }

                input.Append(reference is null ? "" : $"[{reference}] ")
                    .Append(row.Group).Append(", ").Append(row.Setting).Append(": ").Append(row.Value)
                    .Append(" (").Append(LayeredReport.DefaultText(row)).Append(").\n");
            }

            input.Append('\n');
        }

        Rows("The assistive technology at the start, found by the recorder (the report's Appendix C)", settings.Assistive, settings.AssistiveNote);
        Rows("The Windows settings at the start, found by the recorder (Appendix A)", settings.Windows, settings.WindowsNote);
        Rows("The browser's settings at the start, and the values it sent to the first page visited, found by the recorder (Appendix B)", settings.Browser, settings.BrowserNote);
        AppendChanges(input, "during the recording", settings.Changes, settingReferences, references);
        input.Append("\nThe page visits, derived by the recorder:\n");
        foreach (var (visit, _) in parts)
        {
            input.Append(CultureInfo.InvariantCulture,
                $"Visit {visit.Visit.Number}, from {SummaryCollator.Time(visit.Visit.Start.Time)} to {SummaryCollator.Time(visit.Visit.End)}: {visit.Visit.Start.Url}")
                .Append(visit.Title is { Length: > 0 } title ? $", titled \"{title.Replace("\"", "'", StringComparison.Ordinal)}\"" : ", with no title recorded")
                .Append(".\n");
        }

        input.Append("\nThe pages, as the chapter on the pages names them:\n");
        foreach (var page in pages)
        {
            input.Append(CultureInfo.InvariantCulture, $"Section 3.{page.Number}: {LayeredReport.PageTitle(page)}, visits {string.Join(", ", page.Visits)}.\n");
        }

        input.Append("\nThe chapter on the user experience. These are inferences, not facts:\nIts opening:\n");
        AppendStory(input, opening, references);
        foreach (var (visit, step) in parts)
        {
            input.Append(CultureInfo.InvariantCulture, $"The part for visit {visit.Visit.Number}:\n");
            AppendStory(input, step, references);
        }

        return (input.ToString(), references);
    }

    /// <summary>
    /// Adds each setting's record to the evidence, once for each record, in
    /// the order of the appendices then the changes, and gives the
    /// reference of each record by its key.
    /// </summary>
    internal static Dictionary<string, string> RegisterSettings(EvidenceRegister register, RecordedSettings settings)
    {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(settings);
        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in settings.AllRows.Select(row => row.Source).Concat(settings.Changes.SelectMany(change => change.Sources)))
        {
            if (!references.ContainsKey(source.Key))
            {
                references[source.Key] = register.Add(source.Time, source.Text, source.EventId, source.Key);
            }
        }

        return references;
    }

    /// <summary>
    /// Each change by the visit it happens in, by its start: the last visit
    /// started at or before it; 0 for a change before the first visit,
    /// which the opening is given.
    /// </summary>
    internal static Dictionary<int, List<SettingChange>> SettingChangesByVisit(RecordingCollation collation, RecordedSettings settings)
    {
        var byVisit = new Dictionary<int, List<SettingChange>>();
        foreach (var change in settings.Changes)
        {
            var visit = collation.Visits.LastOrDefault(item => item.Visit.Start.Time <= change.Time)?.Visit.Number ?? 0;
            if (!byVisit.TryGetValue(visit, out var list))
            {
                list = [];
                byVisit[visit] = list;
            }

            list.Add(change);
        }

        return byVisit;
    }

    // The changes as a request gives them, each beginning with its references.
    private static void AppendChanges(
        StringBuilder input, string when, IReadOnlyList<SettingChange> changes, IReadOnlyDictionary<string, string>? settingReferences, ISet<string> references)
    {
        input.Append(CultureInfo.InvariantCulture,
            $"\nThe changes {when} to the accessibility settings, the assistive technology, and the Magnifier's view, found by the recorder:\n");
        if (changes.Count == 0)
        {
            input.Append("None were recorded.\n");
            return;
        }

        foreach (var change in changes)
        {
            var cited = change.Sources.Select(source => settingReferences?.GetValueOrDefault(source.Key)).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            references.UnionWith(cited);
            input.Append(string.Concat(cited.Select(reference => $"[{reference}] "))).Append(LayeredReport.ChangeText(change)).Append('\n');
        }
    }

    // A part of the story as a later request gives it: each statement with
    // its form and its evidence, under the name of its subsection where
    // the part has several.
    private static void AppendStory(StringBuilder input, AcceptedStep step, ISet<string> references)
    {
        string? section = null;
        foreach (var claim in step.Claims)
        {
            references.UnionWith(claim.Evidence);
            if (claim.Section != section)
            {
                section = claim.Section;
                if (StoryParts.FirstOrDefault(part => part.Section == section).Heading is { } heading)
                {
                    input.Append("  ").Append(heading).Append(":\n");
                }
            }

            input.Append("  (").Append(LayeredReport.FormOf(step, claim)).Append(") ").Append(claim.Text);
            if (claim.Evidence.Count > 0)
            {
                input.Append(' ').Append(string.Join(' ', claim.Evidence.Select(reference => $"[{reference}]")));
            }

            input.Append('\n');
        }
    }

    /// <summary>A captured frame as the evidence list gives it.</summary>
    public static string FrameText(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var magnified = frame.Magnification is > 1 and var level
            ? string.Create(CultureInfo.InvariantCulture, $", recorded with full screen magnification at {level * 100:0}%")
            : "";
        return string.Create(CultureInfo.InvariantCulture,
            $"The captured frame at {SummaryCollator.Time(frame.Time)}: the whole screen, {frame.Width} by {frame.Height} pixels{magnified}, file {frame.Path}");
    }

    /// <summary>
    /// A tile of a recreated page as the evidence list gives it, with its
    /// file in the store of unique images. A model's request gives it
    /// without the file, as the file was in the task's own folder and would
    /// have made the request differ at every run (owner, 2026-10-14;
    /// analysis-runner.md, "Reusing steps when run again"), and the request
    /// stays as it was.
    /// </summary>
    public static string TileText(Moment moment, RecreatedPage page, PageTile tile, bool withFile = true)
    {
        ArgumentNullException.ThrowIfNull(moment);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(tile);
        return string.Concat(
            FormattableString.Invariant($"Tile {tile.Number} of {page.Tiles.Count} of the page as the recorder rebuilt it at the captured frame at {SummaryCollator.Time(moment.Frame.Time)}: ") +
            FormattableString.Invariant($"CSS pixels {tile.Top:0} to {tile.Top + tile.Height:0} from the top of a page {page.Height:0} high and {page.Width:0} wide, ") +
            FormattableString.Invariant($"pictured at {tile.PixelWidth} by {tile.PixelHeight} pixels{(InView(page, tile) ? ", part of it on the screen at the frame" : ", none of it on the screen at the frame")}{(withFile ? $", file {tile.Path}" : "")}. ") +
            "A recreation from the recorded evidence, not the page itself: what the recording lacks, the recreation lacks");
    }

    // Whether any of a tile was in the viewport, as the recreation was
    // scrolled to the recorded position.
    internal static bool InView(RecreatedPage page, PageTile tile) =>
        tile.Top < page.ScrollY + page.ViewportHeight && tile.Top + tile.Height > page.ScrollY;

    // What the recreation lacked and how it was made, for the model and the report.
    internal static string RecreationNotes(RecreatedPage page)
    {
        var notes = new StringBuilder();
        notes.Append(page.Built
            ? "The recreation was reported built and painted, with its fonts ready. "
            : string.Create(CultureInfo.InvariantCulture, $"The recreation was not reported built within {RecordedMomentPages.BuildTimeout.TotalSeconds:0} s; the tiles are of the page as it was then. "));
        notes.Append(page.ImagesNotLoaded.Count == 0
            ? string.Create(CultureInfo.InvariantCulture, $"All {page.Images} of its images were loaded. ")
            : string.Create(CultureInfo.InvariantCulture,
                $"{page.ImagesNotLoaded.Count} of its {page.Images} images were not loaded in the recreation, so they are blank or missing in the tiles: ") +
              string.Join(", ", page.ImagesNotLoaded.Select(address => address.Length == 0 ? "an image with no address" : address)) + ". ");
        notes.Append(string.Create(CultureInfo.InvariantCulture,
            $"{page.RefusedRequests} of the page's requests were refused, as no answer to them was recorded."));
        return notes.ToString();
    }

    // Which part of the page was on the screen, and the magnification.
    internal static string InViewText(Moment moment, RecreatedPage page, IReadOnlyList<string> tileReferences)
    {
        var shown = page.Tiles.Where(tile => InView(page, tile)).Select(tile => $"tile {tile.Number} [{tileReferences[tile.Number - 1]}]").ToList();
        var magnified = moment.Frame.Magnification is > 1 and var level
            ? string.Create(CultureInfo.InvariantCulture, $" The screen was recorded with full screen magnification at {level * 100:0}%, so less of the page may have been on the screen than the viewport holds.")
            : "";
        return string.Concat(
            FormattableString.Invariant($"At the frame the page was scrolled to {page.ScrollY:0} CSS pixels from the top and {page.ScrollX:0} from the left, with a viewport {page.ViewportWidth:0} by {page.ViewportHeight:0}, ") +
            FormattableString.Invariant($"so the part on the screen was CSS pixels {page.ScrollY:0} to {page.ScrollY + page.ViewportHeight:0}: {(shown.Count == 0 ? "no tile" : string.Join(", ", shown))}.{magnified}"));
    }

    // A group of tiles' request: the capabilities, the visit, why the
    // moment was chosen, the part on the screen, what the recreation
    // lacked, and the tiles with their places on the page.
    internal static (string Input, HashSet<string> References) PageInput(
        RecordingCollation collation,
        VisitCollation visit,
        Moment moment,
        RecreatedPage page,
        IReadOnlyList<PageTile> group,
        IReadOnlyList<string> tileReferences,
        IReadOnlyList<string> reasonReferences,
        int number,
        int count)
    {
        var references = new HashSet<string>(RecordingSummaryKind.CapabilityReferences(collation), StringComparer.Ordinal);
        var input = new StringBuilder()
            .Append(string.Join('\n', collation.CapabilityLines)).Append("\n\n")
            .Append(CultureInfo.InvariantCulture, $"Page visit {visit.Visit.Number}, derived by the recorder: {visit.Visit.Start.Url}")
            .Append(visit.Title is { Length: > 0 } title ? $", titled \"{title.Replace("\"", "'", StringComparison.Ordinal)}\"" : ", with no title recorded")
            .Append(".\n\n")
            .Append(CultureInfo.InvariantCulture, $"Moment {moment.Number} of the visit, at {SummaryCollator.Time(moment.Frame.Time)}. Why the recorder chose it:\n");
        for (var index = 0; index < moment.Reasons.Count; index++)
        {
            references.Add(reasonReferences[index]);
            input.Append(CultureInfo.InvariantCulture, $"[{reasonReferences[index]}] {SummaryCollator.Time(moment.Reasons[index].Time)} {moment.Reasons[index].Text}\n");
        }

        foreach (var tile in page.Tiles)
        {
            references.Add(tileReferences[tile.Number - 1]);
        }

        input.Append(CultureInfo.InvariantCulture,
                $"\nThe page as the recorder rebuilt it at this moment, {page.Width:0} by {page.Height:0} CSS pixels, pictured whole in {page.Tiles.Count} tiles the height of the viewport. ")
            .Append(InViewText(moment, page, tileReferences)).Append('\n')
            .Append(RecreationNotes(page)).Append("\n\n")
            .Append(CultureInfo.InvariantCulture, $"This is group {number} of {count} of the tiles. Its pictures, top to bottom:\n");
        foreach (var tile in group)
        {
            var reference = tileReferences[tile.Number - 1];
            input.Append(CultureInfo.InvariantCulture, $"[{reference}] {TileText(moment, page, tile, withFile: false)}. It is labelled Picture {reference}.\n");
        }

        return (input.ToString(), references);
    }

    // A moment's request: the capabilities, the visit's address and title,
    // why the moment was chosen, and the visit's facts since the moment
    // before, with the picture's reference.
    internal static (string Input, HashSet<string> References) MomentInput(
        RecordingCollation collation,
        VisitCollation visit,
        Moment moment,
        string frameReference,
        IReadOnlyList<string> reasonReferences,
        long previous)
    {
        var references = new HashSet<string>(RecordingSummaryKind.CapabilityReferences(collation), StringComparer.Ordinal) { frameReference };
        var input = new StringBuilder()
            .Append(string.Join('\n', collation.CapabilityLines)).Append("\n\n")
            .Append(CultureInfo.InvariantCulture, $"Page visit {visit.Visit.Number}, derived by the recorder: {visit.Visit.Start.Url}")
            .Append(visit.Title is { Length: > 0 } title ? $", titled \"{title.Replace("\"", "'", StringComparison.Ordinal)}\"" : ", with no title recorded")
            .Append(".\n\n")
            .Append(CultureInfo.InvariantCulture, $"Moment {moment.Number} of the visit, at {SummaryCollator.Time(moment.Frame.Time)}. Why the recorder chose it:\n");
        for (var index = 0; index < moment.Reasons.Count; index++)
        {
            var reason = moment.Reasons[index];
            references.Add(reasonReferences[index]);
            input.Append(CultureInfo.InvariantCulture, $"[{reasonReferences[index]}] {SummaryCollator.Time(reason.Time)} {reason.Text}\n");
        }

        var facts = FactsSince(visit, previous, moment.Frame.Time);
        input.Append(facts.Count == 0
            ? "\nThe recorder has no facts of the visit since the moment before.\n"
            : "\nThe recorder's facts of the visit since the moment before:\n" + string.Join('\n', facts) + "\n");
        foreach (var line in facts)
        {
            foreach (Match match in Reference().Matches(line))
            {
                references.Add(match.Groups[1].Value);
            }
        }

        input.Append(CultureInfo.InvariantCulture, $"\nThe picture: [{frameReference}] {FrameText(moment.Frame)}. It is labelled Picture {frameReference}.");
        return (input.ToString(), references);
    }

    // The visit's facts after one time and up to another.
    internal static List<string> FactsSince(VisitCollation visit, long after, long until) =>
        [.. visit.Lines.Where(line => TimedLine().Match(line) is { Success: true } match && Within(match, after, until))];

    private static bool Within(Match match, long after, long until)
    {
        var time = ((long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60 + long.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)) * 60 +
            long.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture)) * 1_000_000_000 +
            long.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture) * 1_000_000;
        return time > after && time <= until;
    }

    private static string MediaType(string path) => Path.GetExtension(path).ToUpperInvariant() switch
    {
        ".JPG" or ".JPEG" => "image/jpeg",
        ".WEBP" => "image/webp",
        _ => "image/png"
    };

    private static async Task<byte[]> ReadFileAsync(string path, string what, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StepFailedException($"The {what} {path} could not be read: {exception.Message}");
        }
    }

    private static async Task<byte[]> ReadFrameAsync(CapturedFrame frame, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllBytesAsync(frame.AbsolutePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StepFailedException($"The captured frame {frame.AbsolutePath} could not be read: {exception.Message}");
        }
    }
}
