using System.Windows;
using Recorder.Collectors.Android;

namespace Recorder.App;

// The Android menu (docs/architecture/android-implementation-plan.md).
// Part 0 adds its first item, Android setup, which downloads adb.
public partial class MainWindow
{
    private void AndroidSetupMenuItem_Click(object sender, RoutedEventArgs e) =>
        new AndroidSetupWindow(this, AndroidTools.DataFolder, AppContext.BaseDirectory).ShowDialog();
}
