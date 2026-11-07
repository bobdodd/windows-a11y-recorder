using System.IO;
using System.Windows;
using Recorder.Analysis;
using Recorder.Analysis.Setup;

namespace Recorder.App;

// The installed app's analysis configuration (owner, 2026-11-06: "I expect
// the installer to produce a fully working app that I can start. There may
// be some config after install"). At each start the settings file is
// pointed at the llama.cpp server installed beside the programs, unless it
// names one that is there; and with no profile, or when the installer's
// last page starts the app with --setup-models, model setup is offered.
// See docs/architecture/windows-installer.md, "First start".
public partial class MainWindow
{
    public const string SetUpModelsArgument = "--setup-models";

    private void UseInstalledModelServer()
    {
        var path = AnalysisSettingsFile.DefaultPath;
        try
        {
            var text = File.Exists(path) ? File.ReadAllText(path) : null;
            if (ModelSetup.WithBundledServer(text, ModelSetup.BundledServer(AppContext.BaseDirectory), File.Exists) is { } changed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, changed);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowAnalysisStatus($"The analysis settings could not be pointed at the installed model server: {exception.Message}");
        }
    }

    private void OfferModelSetup()
    {
        var asked = Environment.GetCommandLineArgs().Skip(1).Any(argument => string.Equals(argument, SetUpModelsArgument, StringComparison.OrdinalIgnoreCase));
        if (!asked)
        {
            if (_analysisSettings.Profiles.Count > 0 || _analysisSettingsProblems.Count > 0)
            {
                return;
            }

            if (MessageBox.Show(
                    this,
                    "No model is set up for the recorder's analysis, so recordings can be made and played but not summarised. Set up a model now? " +
                    "It can also be done later from the Analysis menu, Set up models.",
                    "Set up analysis models?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }
        }

        ShowModelSetup();
    }

    private void ShowModelSetup()
    {
        new ModelSetupWindow(this, AnalysisSettingsFile.DefaultPath, AppContext.BaseDirectory).ShowDialog();
        ReadAnalysisSettings();
        UpdateAnalysisControls();
    }

    private void ModelSetupMenuItem_Click(object sender, RoutedEventArgs e) => ShowModelSetup();
}
