using System.Windows;

namespace Recorder.App;

// Saving the open recording as an MP4, with the picture as the player shows
// it and the sound at the player's levels. See
// docs/architecture/mp4-export.md.
public partial class MainWindow
{
    private void SaveMp4Button_Click(object sender, RoutedEventArgs e)
    {
        if (_playbackArchive is not { } archive || archive.Frames.Count == 0)
        {
            return;
        }

        if (_isPlaying)
        {
            PausePlayback();
        }

        var gains = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["microphone"] = MicrophoneVolumeSlider.Value,
            ["system"] = SystemAudioVolumeSlider.Value,
            ["screen-reader"] = ScreenReaderVolumeSlider.Value
        };
        new ExportMp4Window(this, archive, _showWholeScreen, gains).ShowDialog();
    }
}
