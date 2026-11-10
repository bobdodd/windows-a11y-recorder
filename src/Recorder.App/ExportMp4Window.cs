using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Recorder.Export;
using Recorder.Session;

namespace Recorder.App;

/// <summary>
/// Saves a whole recording as an MP4 (owner, 2026-11-07: "Next feature is
/// to add MP4 generation from the recording with optionally mic, system,
/// and where available screen-reader audio", and "Whole recording - editing
/// is not part of this tool"). The picture is the player's view; the sound
/// is one track of the chosen sounds mixed at the player's levels and then
/// each chosen sound's own track. See docs/architecture/mp4-export.md.
/// </summary>
internal sealed class ExportMp4Window : Window
{
    private const string ProgressActivityId = "Recorder.App.ExportMp4";

    // The sounds in the order their own tracks follow the mixed track.
    private static readonly (string Stream, string Name)[] Sounds =
    [
        ("microphone", "Microphone"),
        // A recording of a phone has the phone's sound in the system
        // sound's place (Android slice 4b).
        (Recorder.Contracts.AndroidSoundRecords.StreamName, "Phone sound"),
        ("system", "System sound"),
        ("screen-reader", "Screen reader")
    ];

    private readonly SessionPlaybackArchive _archive;
    private readonly bool _showWholeScreen;
    private readonly IReadOnlyDictionary<string, double> _gains;
    private readonly List<(SessionAudioTrack Track, string Name, CheckBox Choose)> _choices = [];
    private readonly TextBox _file;
    private readonly Button _save;
    private readonly Button _stop;
    private readonly Button _browse;
    private readonly ProgressBar _progress;
    private readonly SelectableText _status;
    private CancellationTokenSource? _running;
    private int _announcedTenth = -1;

    public ExportMp4Window(
        Window owner,
        SessionPlaybackArchive archive,
        bool showWholeScreen,
        IReadOnlyDictionary<string, double> gains)
    {
        Owner = owner;
        _archive = archive;
        _showWholeScreen = showWholeScreen;
        _gains = gains;
        Title = "Save as MP4";
        Width = 760;
        Height = 640;
        MinWidth = 480;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var format = VideoSchedule.FormatOf(archive.Frames, archive.FrameSource);
        var phone = archive.FrameSource == SessionFrameSource.Phone;
        var page = new StackPanel { Margin = new Thickness(16) };
        page.Children.Add(Heading("Save the recording as MP4", 1));
        page.Children.Add(Paragraph(
            $"The whole recording, {FormatDuration(archive.DurationNanoseconds)}, as H.264 video of {format.Width} by " +
            $"{format.Height} pixels at {format.FramesPerSecond} frames a second. Each recorded frame is shown from when " +
            "it was captured until the next, to within half a video frame, so the video keeps the recording's timing." +
            (phone
                ? " The phone sends a picture whenever its screen changes, up to 120 a second. Of pictures less than a " +
                  "sixtieth of a second apart, only the last before each video frame is shown; the recording keeps them all."
                : "")));
        page.Children.Add(Paragraph(MagnifiedView.AnyChanged(archive.Frames)
            ? showWholeScreen
                ? "Picture: the whole screen as captured, with the participant's view outlined, as the player shows it " +
                  "now. To save the participant's view instead, close this window and turn off Whole screen."
                : "Picture: the participant's view, the part of the screen Magnifier showed in the colors it showed, as " +
                  "the player shows it now. To save the whole screen instead, close this window and turn on Whole screen."
            : phone
                ? "Picture: the phone's screen as recorded, each picture as large as fits, upright as the phone was held."
                : "Picture: the screen as captured."));

        page.Children.Add(Heading("Sound", 2));
        var tracks = archive.AudioTracks.ToDictionary(track => track.Stream, StringComparer.Ordinal);
        foreach (var (stream, name) in Sounds)
        {
            if (!tracks.TryGetValue(stream, out var track))
            {
                continue;
            }

            var gain = gains.TryGetValue(stream, out var value) ? Math.Min(0, value) : 0;
            var choose = new CheckBox
            {
                Content = $"{name}, mixed at {FormatGain(gain)}",
                IsChecked = true,
                MinHeight = 44,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _choices.Add((track, name, choose));
            page.Children.Add(choose);
        }

        if (_choices.Count == 0)
        {
            page.Children.Add(Paragraph("This recording has no sound; the MP4 has video only."));
        }
        else
        {
            if (tracks.ContainsKey("system") && !tracks.ContainsKey("screen-reader"))
            {
                page.Children.Add(Paragraph(
                    "This recording has no separate screen reader sound: NVDA was not running when it started, or " +
                    "Windows could not separate its sound, or it was made before the recorder could. Any speech it " +
                    "has is part of the system sound."));
            }

            page.Children.Add(Paragraph(
                "The MP4's first sound track is the chosen sounds mixed at the player's levels. Each chosen sound then " +
                "has a track of its own at its recorded level, in the order listed here, for editing elsewhere. The " +
                "own tracks are marked as alternatives to the first, so players play only the first; some, such as " +
                "VLC, let you choose another."));
        }

        page.Children.Add(Heading("File", 2));
        _file = new TextBox { MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center, Text = DefaultPath() };
        AutomationProperties.SetName(_file, "MP4 file");
        var fileRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 4, 0, 4) };
        var fileLabel = new Label { Content = "MP4 _file:", Target = _file, VerticalAlignment = VerticalAlignment.Center };
        _browse = Command("_Browse...", Browse);
        DockPanel.SetDock(fileLabel, Dock.Left);
        DockPanel.SetDock(_browse, Dock.Right);
        fileRow.Children.Add(fileLabel);
        fileRow.Children.Add(_browse);
        fileRow.Children.Add(_file);
        page.Children.Add(fileRow);

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        _save = Command("_Save MP4", () => _ = SaveAsync());
        _stop = Command("S_top", () => _running?.Cancel());
        _stop.IsEnabled = false;
        buttons.Children.Add(_save);
        buttons.Children.Add(_stop);
        page.Children.Add(buttons);

        _progress = new ProgressBar { Height = 20, Minimum = 0, Maximum = 1000, Margin = new Thickness(0, 4, 0, 4) };
        AutomationProperties.SetName(_progress, "MP4 progress");
        page.Children.Add(_progress);
        _status = Paragraph("");
        AutomationProperties.SetName(_status, "MP4 status");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        page.Children.Add(_status);

        var close = new Button { Content = "Close", MinWidth = 88, MinHeight = 44, IsCancel = true, Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        page.Children.Add(close);

        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Closing += OnClosing;
    }

    private string DefaultPath()
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(_archive.SessionDirectory));
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrEmpty(folder))
        {
            folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        return Path.Combine(folder, (string.IsNullOrEmpty(name) ? "recording" : name) + ".mp4");
    }

    private void Browse()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save the recording as MP4",
            Filter = "MP4 video (*.mp4)|*.mp4",
            DefaultExt = ".mp4",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = Path.GetFileName(_file.Text)
        };
        var folder = Path.GetDirectoryName(_file.Text);
        if (Directory.Exists(folder))
        {
            dialog.InitialDirectory = folder;
        }

        if (dialog.ShowDialog(this) == true)
        {
            _file.Text = dialog.FileName;
        }
    }

    private async Task SaveAsync()
    {
        string path;
        try
        {
            path = Path.GetFullPath(_file.Text.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Say($"The file name cannot be used: {exception.Message}");
            return;
        }

        if (!path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            path += ".mp4";
            _file.Text = path;
        }

        if (!Directory.Exists(Path.GetDirectoryName(path)))
        {
            Say($"The folder {Path.GetDirectoryName(path)} does not exist.");
            return;
        }

        if (File.Exists(path) && MessageBox.Show(
                this,
                $"{path} already exists. Replace it?",
                "Replace the file?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var chosen = _choices.Where(choice => choice.Choose.IsChecked == true).ToList();
        using var running = new CancellationTokenSource();
        _running = running;
        SetRunning(true);
        Say("Saving the MP4.");
        var progress = new Progress<Mp4ExportProgress>(ShowProgress);
        var completion = new TaskCompletionSource<Mp4ExportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var archive = _archive;
        var wholeScreen = _showWholeScreen;
        var gains = _gains;
        var token = running.Token;

        // The pictures are drawn with WPF, which needs a single-threaded
        // apartment, on a thread of the export's own so the window stays
        // responsive.
        var thread = new Thread(() =>
        {
            try
            {
                var sources = chosen.Select(choice => new ExportAudioSource(
                        choice.Track.Stream,
                        choice.Track.AbsolutePath,
                        choice.Track.StartNanoseconds,
                        archive.AudioBuffers?.ReadAudioBuffers(choice.Track.Stream, token) ?? [],
                        gains.TryGetValue(choice.Track.Stream, out var gain) ? gain : 0))
                    .ToList();
                completion.TrySetResult(Mp4Exporter.Export(
                    new Mp4ExportRequest(path, archive.Frames, archive.DurationNanoseconds, sources, archive.FrameSource),
                    new WpfFrameRenderer(wholeScreen),
                    progress,
                    token));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "MP4 export"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        try
        {
            var result = await completion.Task;
            _progress.Value = _progress.Maximum;
            Say(Describe(result, chosen.Select(choice => (choice.Track.Stream, choice.Name)).ToList()));
        }
        catch (OperationCanceledException)
        {
            _progress.Value = 0;
            Say("Stopped. No MP4 was saved.");
        }
        catch (MediaFoundationUnavailableException exception)
        {
            Say(exception.Message);
        }
        catch (Exception exception)
        {
            Say($"The MP4 could not be saved: {exception.Message}");
        }
        finally
        {
            _running = null;
            SetRunning(false);
        }
    }

    private static string Describe(Mp4ExportResult result, IReadOnlyList<(string Stream, string Name)> chosen)
    {
        var lines = new List<string>
        {
            $"Saved {result.Path}: {result.Format.Width} by {result.Format.Height} pixels, " +
            (result.AudioTracks == 0
                ? "no sound."
                : $"{result.AudioTracks} sound tracks: mixed, then {string.Join(", ", chosen.Select(sound => sound.Name.ToLowerInvariant()))}.") +
            $" Encoded with the {result.Encoder}."
        };
        if (result.EarlierFailures.Count > 0)
        {
            lines.Add("Earlier attempts failed: " + string.Join(" ", result.EarlierFailures));
        }

        foreach (var (stream, name) in chosen)
        {
            if (!result.Placements.TryGetValue(stream, out var plan))
            {
                continue;
            }

            if (!plan.FromBufferRecords)
            {
                lines.Add($"{name}: no buffer records, so the sound is played straight through from where it started, as the player does.");
                continue;
            }

            var parts = new List<string>();
            if (plan.SilencesInserted > 0)
            {
                parts.Add($"{plan.SilencesInserted} silences where nothing was recorded, {FormatSeconds(plan.SilenceFrames, plan.SampleRate)} in all, kept as silence");
            }

            if (plan.FramesSkipped > 0)
            {
                parts.Add($"{FormatSeconds(plan.FramesSkipped, plan.SampleRate)} left out where the sound ran ahead of its recorded times");
            }

            if (parts.Count > 0)
            {
                lines.Add($"{name}: {string.Join("; ", parts)}.");
            }
        }

        return string.Join(Environment.NewLine, lines);

        static string FormatSeconds(long frames, int sampleRate) =>
            string.Create(CultureInfo.CurrentCulture, $"{(double)frames / sampleRate:0.0} s");
    }

    private void ShowProgress(Mp4ExportProgress progress)
    {
        var fraction = progress.TotalNanoseconds <= 0 ? 1 : Math.Clamp((double)progress.DoneNanoseconds / progress.TotalNanoseconds, 0, 1);
        _progress.Value = fraction * _progress.Maximum;
        _status.Text = $"Saving the MP4: {FormatDuration(progress.DoneNanoseconds)} of {FormatDuration(progress.TotalNanoseconds)}, {fraction:P0}.";
        // Spoken at each tenth, not at every frame.
        var tenth = (int)(fraction * 10);
        if (tenth != _announcedTenth)
        {
            _announcedTenth = tenth;
            Announce(_status.Text);
        }
    }

    private void SetRunning(bool running)
    {
        _save.IsEnabled = !running;
        _browse.IsEnabled = !running;
        _file.IsEnabled = !running;
        foreach (var choice in _choices)
        {
            choice.Choose.IsEnabled = !running;
        }

        _stop.IsEnabled = running;
        if (!running)
        {
            _announcedTenth = -1;
        }
    }

    private void Say(string message)
    {
        _status.Text = message;
        Announce(message);
    }

    private void Announce(string message)
    {
        var peer = UIElementAutomationPeer.FromElement(_status) ?? UIElementAutomationPeer.CreatePeerForElement(_status);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, message, ProgressActivityId);
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_running is null)
        {
            return;
        }

        if (MessageBox.Show(this, "The MP4 is being saved. Stop and close? No MP4 is saved.",
                "Stop saving?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _running.Cancel();
        }
        else
        {
            eventArgs.Cancel = true;
        }
    }

    private static string FormatDuration(long nanoseconds)
    {
        var time = TimeSpan.FromTicks(Math.Max(0, nanoseconds) / 100);
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static string FormatGain(double gain) =>
        gain == 0 ? "0 dB" : string.Create(CultureInfo.CurrentCulture, $"{gain:0} dB");

    private static SelectableText Heading(string text, int level)
    {
        var heading = new SelectableText
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            FontSize = level == 1 ? 22 : 17,
            Margin = new Thickness(0, level == 1 ? 0 : 16, 0, 6)
        };
        AutomationProperties.SetHeadingLevel(heading, level == 1 ? AutomationHeadingLevel.Level1 : AutomationHeadingLevel.Level2);
        return heading;
    }

    private static SelectableText Paragraph(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };

    private static Button Command(string content, Action click)
    {
        var button = new Button { Content = content, MinWidth = 44, MinHeight = 44, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 8, 0) };
        button.Click += (_, _) => click();
        return button;
    }
}

/// <summary>
/// Draws a frame for an MP4 export as the player draws it, with
/// <see cref="MainWindow.FrameImage(SessionVideoFrame, BitmapSource, bool, out string)"/>,
/// scaled to the export's size with its shape kept and black around it.
/// Must be used on a thread in a single-threaded apartment.
/// </summary>
internal sealed class WpfFrameRenderer(bool showWholeScreen) : IExportFrameRenderer
{
    public void Render(SessionVideoFrame frame, int width, int height, byte[] destination)
    {
        BitmapSource bitmap;
        using (var stream = new FileStream(frame.AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            bitmap = image;
        }

        var source = MainWindow.FrameImage(frame, bitmap, showWholeScreen, out _);
        // The bitmap's own size in pixels, not in WPF's units, which
        // depend on the resolution written in the PNG.
        var (sourceWidth, sourceHeight) = source is BitmapSource drawn
            ? ((double)drawn.PixelWidth, (double)drawn.PixelHeight)
            : (source.Width, source.Height);
        var (x, y, drawnWidth, drawnHeight) = VideoSchedule.Placement(sourceWidth, sourceHeight, width, height);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
            context.DrawImage(source, new Rect(x, y, drawnWidth, drawnHeight));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.CopyPixels(destination, width * 4, 0);
    }
}
