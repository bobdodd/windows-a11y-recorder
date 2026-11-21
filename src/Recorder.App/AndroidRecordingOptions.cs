using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Recorder.Collectors.Android;
using Recorder.Contracts;

namespace Recorder.App;

/// <summary>The optional Android controls in the same dialog as the recording name.</summary>
internal sealed class AndroidRecordingOptions : StackPanel
{
    private readonly AndroidRecordingPreflight _phone;
    private readonly ComboBox _apps = new() { MinHeight = 44, MaxDropDownHeight = 240 };
    private readonly CheckBox _collect = new()
    {
        Content = "Collect app _text sizes before recording", IsChecked = false,
        MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center
    };
    private readonly Button _refresh = new()
    {
        Content = "_Refresh visible apps", MinHeight = 44, Padding = new Thickness(10, 0, 10, 0),
        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0)
    };
    private readonly SelectableText _status = new() { TextWrapping = TextWrapping.Wrap };

    public AndroidRecordingOptions(AndroidRecordingPreflight phone, CancellationToken cancellation)
    {
        _phone = phone;
        Margin = new Thickness(0, 0, 0, 8);
        Children.Add(new Label { Content = "Visible Android _app:", Target = _apps, Padding = new Thickness(0) });
        AutomationProperties.SetName(_apps, "Visible Android app");
        Children.Add(_apps);
        Children.Add(_refresh);
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        Children.Add(_status);
        Children.Add(_collect);
        Children.Add(new SelectableText
        {
            Text = "Optional. Bring the selected app to the foreground on the phone. " +
                "The snapshot includes text sizes, not text or entered values. Leaving this off records the phone as usual.",
            TextWrapping = TextWrapping.Wrap
        });
        Loaded += async (_, _) => await RefreshAsync(cancellation);
        _refresh.Click += async (_, _) => await RefreshAsync(cancellation);
    }

    public async Task<AndroidTextSizeSnapshot?> CollectAsync(CancellationToken cancellation)
    {
        if (_collect.IsChecked != true)
        {
            return null;
        }

        if (_apps.SelectedItem is not string package)
        {
            throw new AndroidTextSizeRefusedException("Select a visible Android app before requesting text sizes.");
        }

        SetStatus("Reading text sizes from the selected app before recording.");
        try
        {
            return await _phone.ReadTextSizesAsync(package, cancellation);
        }
        finally
        {
            if (!cancellation.IsCancellationRequested)
            {
                SetStatus("");
            }
        }
    }

    private async Task RefreshAsync(CancellationToken cancellation)
    {
        _refresh.IsEnabled = false;
        _apps.IsEnabled = false;
        var selected = _apps.SelectedItem;
        _apps.ItemsSource = null;
        SetStatus("Looking for visible Android apps.");
        try
        {
            var apps = await _phone.VisibleAppsAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            _apps.ItemsSource = apps;
            _apps.IsEnabled = apps.Count > 0;
            _apps.SelectedItem = selected;
            SetStatus(apps.Count == 0
                ? "No visible apps were found. Open the app on the unlocked phone and refresh."
                : "Choose the app by package name if you want a text-size snapshot.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The dialog was closed.
        }
        catch (Exception e) when (e is IOException or Win32Exception)
        {
            if (!cancellation.IsCancellationRequested)
            {
                SetStatus("Visible apps could not be listed. Check the phone connection and refresh. You can still record with the checkbox off.");
            }
        }
        finally
        {
            _refresh.IsEnabled = true;
        }
    }

    private void SetStatus(string value)
    {
        _status.Text = value;
        var peer = UIElementAutomationPeer.FromElement(_status) ?? UIElementAutomationPeer.CreatePeerForElement(_status);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
