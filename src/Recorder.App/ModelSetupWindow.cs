using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Recorder.Analysis;
using Recorder.Analysis.Setup;

namespace Recorder.App;

/// <summary>
/// Model setup (owner, 2026-11-06: "the install should be as
/// comprehensive as possible including model selection and download either
/// as part of the install or as config setup"): this PC's graphics cards
/// and memory, the models in model-catalog.json with how each compares with
/// them, and downloads that continue after a stop and are checked against
/// the published SHA-256. A model not in the list is added from a Hugging
/// Face address or a file already on the PC. Each model becomes a text
/// profile and, with its image add-on, a profile for images. See
/// docs/architecture/windows-installer.md, "Model setup".
/// </summary>
internal sealed class ModelSetupWindow : Window
{
    private const string ProgressActivityId = "Recorder.App.ModelSetup";
    private const string ModelsFolderName = "modelsFolder";

    private readonly string _settingsPath;
    private readonly PcHardware _hardware;
    private readonly ModelCatalog _catalog;
    private readonly IReadOnlyList<string> _catalogProblems;
    private readonly TextBox _folder;
    private readonly SelectableText _space;
    private readonly List<(CatalogModel Model, CheckBox Choose, CheckBox Images, SelectableText State)> _choices = [];
    private readonly SelectableText _chosenSize;
    private readonly ProgressBar _progress;
    private readonly SelectableText _status;
    private readonly TextBox _hfModel;
    private readonly TextBox _hfImages;
    private readonly List<Button> _startButtons = [];
    private readonly Button _cancel;
    private CancellationTokenSource? _running;
    private int _announcedTenth = -1;
    private string _announcedFile = "";

    public ModelSetupWindow(Window owner, string settingsPath, string baseDirectory)
    {
        Owner = owner;
        _settingsPath = settingsPath;
        Title = "Set up analysis models";
        Width = 900;
        Height = 760;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _hardware = PcHardware.Read();
        (_catalog, _catalogProblems) = ModelCatalog.Read(Path.Combine(baseDirectory, ModelCatalog.FileName));

        var page = new StackPanel { Margin = new Thickness(16) };
        page.Children.Add(Heading("Set up analysis models", 1));
        page.Children.Add(Paragraph(
            "The recorder's analysis runs a model on this PC with llama.cpp. Choose models to download, or add a model file " +
            "already on this PC. Each model is added as a profile for text and, with its image add-on, a second profile for " +
            "images. Sizes are in GB of 1,073,741,824 bytes, as Windows shows them; Hugging Face's figures are about 7% larger."));

        page.Children.Add(Heading("This PC", 2));
        foreach (var line in HardwareLines(baseDirectory))
        {
            page.Children.Add(Paragraph(line));
        }

        page.Children.Add(Heading("Where the models are saved", 2));
        _folder = new TextBox { MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center, Text = ModelsFolder() };
        AutomationProperties.SetName(_folder, "Models folder");
        _folder.TextChanged += (_, _) => ShowSpace();
        var folderRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 4, 0, 4) };
        var folderLabel = new Label { Content = "_Models folder:", Target = _folder, VerticalAlignment = VerticalAlignment.Center };
        var browse = Command("_Browse...", BrowseFolder);
        DockPanel.SetDock(folderLabel, Dock.Left);
        DockPanel.SetDock(browse, Dock.Right);
        folderRow.Children.Add(folderLabel);
        folderRow.Children.Add(browse);
        folderRow.Children.Add(_folder);
        page.Children.Add(folderRow);
        _space = Paragraph("");
        page.Children.Add(_space);

        page.Children.Add(Heading("Models to download", 2));
        if (_catalogProblems.Count > 0)
        {
            page.Children.Add(Paragraph("Problems with the list of models: " + string.Join(" ", _catalogProblems)));
        }

        var first = true;
        foreach (var model in _catalog.Models)
        {
            page.Children.Add(Choice(model, first && NoProfiles()));
            first = false;
        }

        _chosenSize = Paragraph("");
        page.Children.Add(_chosenSize);
        var downloadButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        downloadButtons.Children.Add(Start("_Download and add the chosen models", DownloadChosenAsync));
        _cancel = Command("_Stop the download", () => _running?.Cancel());
        _cancel.IsEnabled = false;
        downloadButtons.Children.Add(_cancel);
        page.Children.Add(downloadButtons);

        _progress = new ProgressBar { Height = 20, Minimum = 0, Maximum = 1000, Margin = new Thickness(0, 4, 0, 4) };
        AutomationProperties.SetName(_progress, "Download progress");
        page.Children.Add(_progress);
        _status = Paragraph("");
        AutomationProperties.SetName(_status, "Model setup status");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        page.Children.Add(_status);

        page.Children.Add(Heading("Add a model from Hugging Face", 2));
        page.Children.Add(Paragraph(
            "For a model that is not in the list: the address of its .gguf file on Hugging Face, from the file's page or its " +
            "download link. Its size and SHA-256 are read from Hugging Face before the download, and it is fixed to the " +
            "repository's commit at that moment. Files that need a Hugging Face sign-in cannot be added here."));
        _hfModel = Field(page, "Model file _address:", "Hugging Face model file address");
        _hfImages = Field(page, "Image add-on address (_optional):", "Hugging Face image add-on file address, optional");
        var hfButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        hfButtons.Children.Add(Start("Download and add from _Hugging Face", DownloadFromHuggingFaceAsync));
        page.Children.Add(hfButtons);

        page.Children.Add(Heading("Add a model file on this PC", 2));
        page.Children.Add(Paragraph(
            "A .gguf file already on this PC, and its image add-on if it has one. The file's SHA-256 is computed and recorded " +
            "with the profile, which takes a few minutes for a large file."));
        var fileButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        fileButtons.Children.Add(Start("Choose a model _file...", AddLocalFileAsync));
        page.Children.Add(fileButtons);

        var close = new Button { Content = "Close", MinWidth = 88, MinHeight = 44, IsCancel = true, Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        page.Children.Add(close);

        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Closing += OnClosing;
        ShowSpace();
        ShowChosenSize();
        ShowDownloaded();
    }

    private IEnumerable<string> HardwareLines(string baseDirectory)
    {
        if (_hardware.GraphicsCards.Count == 0)
        {
            yield return "Graphics card: none with its memory reported by Windows or NVIDIA's driver.";
        }

        foreach (var card in _hardware.GraphicsCards)
        {
            yield return $"Graphics card: {card.Name}, {ByteSize.Gigabytes(card.MemoryBytes)}" +
                (card.Driver is null ? "" : $", driver {card.Driver}") + $" (as reported by {card.Source}).";
        }

        if (!_hardware.NvidiaDriver)
        {
            yield return "NVIDIA's driver was not found (no nvidia-smi.exe in Windows' System32 folder). The installed llama.cpp server " +
                "uses NVIDIA graphics cards; without one, models run on the processor, many times slower.";
        }

        yield return $"Memory: {ByteSize.Gigabytes(_hardware.MemoryBytes)}.";
        var (settings, _) = AnalysisSettingsFile.Read(_settingsPath);
        var server = settings.LocalServer?.Executable;
        yield return server is null
            ? $"Model server: none set. The installed one is {ModelSetup.BundledServer(baseDirectory)}" +
              (File.Exists(ModelSetup.BundledServer(baseDirectory)) ? "." : ", which is not there.")
            : $"Model server: {server}" + (File.Exists(server) ? "." : ", which is not there.");
    }

    private Border Choice(CatalogModel model, bool chosen)
    {
        var panel = new StackPanel();
        var size = model.DownloadSize(false);
        var choose = new CheckBox
        {
            Content = $"{model.Name}, {ByteSize.Gigabytes(size)}",
            IsChecked = chosen,
            MinHeight = 44,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        };
        choose.Checked += (_, _) => ShowChosenSize();
        choose.Unchecked += (_, _) => ShowChosenSize();
        panel.Children.Add(choose);
        panel.Children.Add(Paragraph(model.Description));
        panel.Children.Add(Paragraph(FitComparison.Describe(size, _hardware)));
        if (model.Note is { } note)
        {
            panel.Children.Add(Paragraph(note));
        }

        panel.Children.Add(Paragraph($"Licence: {model.Licence ?? "not given in the list"}. From {model.Source}. Server arguments: {model.Arguments}."));
        var images = new CheckBox
        {
            Content = model.ImageAddOn is { } addOn
                ? $"Also download its image add-on, {ByteSize.Gigabytes(addOn.Size)}, for the summary with vision"
                : "No image add-on in the list",
            IsChecked = model.ImageAddOn is not null,
            IsEnabled = model.ImageAddOn is not null,
            MinHeight = 44,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24, 0, 0, 0)
        };
        images.Checked += (_, _) => ShowChosenSize();
        images.Unchecked += (_, _) => ShowChosenSize();
        panel.Children.Add(images);
        var state = Paragraph("");
        panel.Children.Add(state);
        if (FitComparison.TooLarge(size, _hardware))
        {
            choose.IsChecked = false;
            choose.IsEnabled = false;
        }

        _choices.Add((model, choose, images, state));
        return new Border
        {
            BorderBrush = SystemColors.ActiveBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 4, 0, 4),
            Child = panel
        };
    }

    private void ShowChosenSize()
    {
        var bytes = _choices.Where(choice => choice.Choose.IsChecked == true)
            .Sum(choice => choice.Model.DownloadSize(choice.Images.IsChecked == true));
        var count = _choices.Count(choice => choice.Choose.IsChecked == true);
        _chosenSize.Text = count == 0
            ? "No model is chosen."
            : $"{count} {(count == 1 ? "model" : "models")} chosen, {ByteSize.Gigabytes(bytes)} to download, less any already downloaded.";
    }

    private void ShowSpace()
    {
        var free = PcHardware.FreeSpace(_folder.Text);
        _space.Text = free > 0 ? $"Free space on that drive: {ByteSize.Gigabytes(free)}." : "The free space on that drive could not be read.";
    }

    private void ShowDownloaded()
    {
        foreach (var (model, _, _, state) in _choices)
        {
            var path = ModelDownloader.TargetPath(_folder.Text, model.Model);
            state.Text = File.Exists(path) && ModelDownloader.ReadHash(path) == model.Model.Sha256
                ? $"Already downloaded and checked: {path}"
                : File.Exists(path + ModelDownloader.PartialSuffix)
                    ? "Partly downloaded; the download continues from where it stopped."
                    : "";
        }
    }

    private async Task DownloadChosenAsync(CancellationToken cancellation)
    {
        var chosen = _choices.Where(choice => choice.Choose.IsChecked == true).ToList();
        if (chosen.Count == 0)
        {
            Say("Choose at least one model to download.");
            return;
        }

        var folder = SaveModelsFolder();
        using var http = Client(redirects: true);
        var downloader = new ModelDownloader(http);
        var added = new List<string>();
        foreach (var (model, _, images, _) in chosen)
        {
            Say($"Downloading {model.Name}.");
            var modelPath = await downloader.DownloadAsync(model.Model, folder, Progress(), cancellation);
            string? addOnPath = null;
            if (images.IsChecked == true && model.ImageAddOn is { } addOn)
            {
                addOnPath = await downloader.DownloadAsync(addOn, folder, Progress(), cancellation);
            }

            added.AddRange(AddProfiles(model.Name, model.Id, model.Quantization, modelPath, model.Model.Sha256, addOnPath, model.Arguments, model.Generation));
            ShowDownloaded();
        }

        Done(added);
    }

    private async Task DownloadFromHuggingFaceAsync(CancellationToken cancellation)
    {
        var modelAddress = HuggingFaceFile.DownloadAddress(_hfModel.Text);
        var addOnText = _hfImages.Text.Trim();
        var addOnAddress = addOnText.Length == 0 ? null : HuggingFaceFile.DownloadAddress(addOnText);
        if (modelAddress is null || (addOnText.Length > 0 && addOnAddress is null))
        {
            Say("Give the address of a .gguf file on huggingface.co, from its page (with /blob/) or its download link (with /resolve/).");
            return;
        }

        var folder = SaveModelsFolder();
        Say("Reading the file's size and SHA-256 from Hugging Face.");
        using var head = Client(redirects: false);
        var model = await HuggingFaceFile.DescribeAsync(head, modelAddress, cancellation);
        var addOn = addOnAddress is null ? null : await HuggingFaceFile.DescribeAsync(head, addOnAddress, cancellation);
        var size = model.Size + (addOn?.Size ?? 0);
        if (FitComparison.TooLarge(model.Size, _hardware))
        {
            Say(FitComparison.Describe(model.Size, _hardware));
            return;
        }

        Say($"{model.Name}, {ByteSize.Gigabytes(size)} with its image add-on if given. {FitComparison.Describe(model.Size, _hardware)}");
        using var http = Client(redirects: true);
        var downloader = new ModelDownloader(http);
        var modelPath = await downloader.DownloadAsync(model, folder, Progress(), cancellation);
        var addOnPath = addOn is null ? null : await downloader.DownloadAsync(addOn, folder, Progress(), cancellation);
        var name = Path.GetFileNameWithoutExtension(model.Name);
        Done(AddProfiles(name, ModelSetup.ModelName(model.Name), ModelSetup.Quantization(model.Name), modelPath, model.Sha256, addOnPath,
            DefaultArguments, DefaultGeneration));
    }

    private async Task AddLocalFileAsync(CancellationToken cancellation)
    {
        var dialog = new OpenFileDialog { Title = "Choose a model file", Filter = "Model files (*.gguf)|*.gguf", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var modelPath = dialog.FileName;
        string? addOnPath = null;
        if (MessageBox.Show(this, "Does this model have an image add-on file (often named mmproj) to choose?", "Image add-on",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            var addOnDialog = new OpenFileDialog
            {
                Title = "Choose the image add-on file",
                Filter = "Model files (*.gguf)|*.gguf",
                CheckFileExists = true,
                InitialDirectory = Path.GetDirectoryName(modelPath)
            };
            if (addOnDialog.ShowDialog(this) == true)
            {
                addOnPath = addOnDialog.FileName;
            }
        }

        var size = new FileInfo(modelPath).Length;
        Say($"Computing the SHA-256 of {Path.GetFileName(modelPath)}, {ByteSize.Gigabytes(size)}. {FitComparison.Describe(size, _hardware)}");
        var sha = ModelDownloader.ReadHash(modelPath) ??
            await ModelDownloader.Sha256Async(modelPath, Path.GetFileName(modelPath), Progress(), cancellation);
        var name = Path.GetFileNameWithoutExtension(modelPath);
        Done(AddProfiles(name, ModelSetup.ModelName(modelPath), ModelSetup.Quantization(modelPath), modelPath, sha, addOnPath,
            DefaultArguments, DefaultGeneration));
    }

    // A model not in the list starts with the list's settings: flash
    // attention, a 32,768-token context, and the layers placed by
    // llama.cpp's fit; 8,192 output tokens, temperature 0, seed 1, no
    // thinking. All can be changed in the model profiles window.
    private const string DefaultArguments = "-fa on -c 32768";
    private static readonly GenerationSettings DefaultGeneration = new(8192, 0, 1, false);

    private IReadOnlyList<string> AddProfiles(
        string name,
        string model,
        string quantization,
        string modelPath,
        string sha256,
        string? addOnPath,
        string arguments,
        GenerationSettings generation)
    {
        AnalysisPlayer.EnsureSettingsFile(_settingsPath);
        var text = File.ReadAllText(_settingsPath);
        var profiles = ModelSetup.Profiles(name, model, quantization, modelPath, sha256, addOnPath, arguments, generation, ModelSetup.Port(text));
        File.WriteAllText(_settingsPath, ModelSetup.WithProfiles(text, profiles));
        return [.. profiles.Select(profile => profile["name"]!.GetValue<string>())];
    }

    private void Done(IReadOnlyList<string> added)
    {
        var (settings, _) = AnalysisSettingsFile.Read(_settingsPath);
        var text = settings.Defaults.GetValueOrDefault(ModelCapabilities.Text);
        var image = settings.Defaults.GetValueOrDefault(ModelCapabilities.Image);
        _progress.Value = _progress.Maximum;
        Say($"Added {(added.Count == 1 ? "the profile" : "the profiles")} {string.Join(" and ", added.Select(item => $"\"{item}\""))}. " +
            $"The default for text is {(text is null ? "not set" : $"\"{text}\"")}; for images, {(image is null ? "not set" : $"\"{image}\"")}. " +
            "Defaults and settings can be changed in the model profiles window.");
    }

    // Each start button runs one job at a time, with the others and the
    // folder disabled until it ends, and its failure shown, not thrown.
    private Button Start(string content, Func<CancellationToken, Task> job)
    {
        var button = Command(content, () => { });
        button.Click += async (_, _) =>
        {
            if (_running is not null)
            {
                return;
            }

            _running = new CancellationTokenSource();
            SetRunning(true);
            try
            {
                await job(_running.Token);
            }
            catch (OperationCanceledException)
            {
                Say("Stopped. A download continues from where it stopped when it is started again.");
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Say(exception.Message);
            }
            finally
            {
                _running.Dispose();
                _running = null;
                SetRunning(false);
                ShowSpace();
                ShowDownloaded();
            }
        };
        _startButtons.Add(button);
        return button;
    }

    private void SetRunning(bool running)
    {
        foreach (var button in _startButtons)
        {
            button.IsEnabled = !running;
        }

        _folder.IsEnabled = !running;
        _cancel.IsEnabled = running;
        if (!running)
        {
            _announcedTenth = -1;
        }
    }

    private Progress<DownloadProgress> Progress() => new(progress =>
    {
        var fraction = progress.Total == 0 ? 1 : (double)progress.Done / progress.Total;
        _progress.Value = fraction * _progress.Maximum;
        var verb = progress.Stage == "Checking" ? "Checking the SHA-256 of" : "Downloading";
        _status.Text = $"{verb} {progress.FileName}: {ByteSize.Gigabytes(progress.Done)} of {ByteSize.Gigabytes(progress.Total)}, " +
            $"{fraction:P0}, {ModelDownloader.Rate(progress.BytesPerSecond)}.";
        // Spoken at each tenth, not at every update.
        var tenth = (int)(fraction * 10);
        var key = progress.Stage + progress.FileName;
        if (key != _announcedFile || tenth != _announcedTenth)
        {
            _announcedFile = key;
            _announcedTenth = tenth;
            Announce(_status.Text);
        }
    });

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

        if (MessageBox.Show(this, "A download or check is running. Stop it and close? A download continues from where it stopped when it is started again.",
                "Stop the download?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _running.Cancel();
        }
        else
        {
            eventArgs.Cancel = true;
        }
    }

    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose the models folder" };
        if (Directory.Exists(_folder.Text))
        {
            dialog.InitialDirectory = _folder.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            _folder.Text = dialog.FolderName;
            ShowDownloaded();
        }
    }

    private bool NoProfiles() => AnalysisSettingsFile.Read(_settingsPath).Settings.Profiles.Count == 0;

    // The models folder is kept in the settings file, as "modelsFolder",
    // so it is offered again; by default it is beside the recorder's data.
    private string ModelsFolder()
    {
        try
        {
            if (File.Exists(_settingsPath) &&
                JsonNode.Parse(File.ReadAllText(_settingsPath))?[ModelsFolderName] is JsonValue value &&
                value.TryGetValue<string>(out var folder) && folder.Length > 0)
            {
                return folder;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
        }

        return Path.Combine(Path.GetDirectoryName(_settingsPath)!, "models");
    }

    private string SaveModelsFolder()
    {
        var folder = Path.GetFullPath(_folder.Text.Trim());
        Directory.CreateDirectory(folder);
        AnalysisPlayer.EnsureSettingsFile(_settingsPath);
        var document = JsonNode.Parse(File.ReadAllText(_settingsPath)) as JsonObject
            ?? throw new InvalidDataException("The analysis settings file does not hold a JSON object.");
        document[ModelsFolderName] = folder;
        File.WriteAllText(_settingsPath, document.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return folder;
    }

    private static HttpClient Client(bool redirects)
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = redirects }) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WindowsA11yRecorder/" + (typeof(ModelSetupWindow).Assembly.GetName().Version?.ToString(3) ?? "0"));
        return client;
    }

    private TextBox Field(Panel page, string label, string name)
    {
        var box = new TextBox { MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, name);
        page.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0, 4, 0, 2) });
        page.Children.Add(box);
        return box;
    }

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
