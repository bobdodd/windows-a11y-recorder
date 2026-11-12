using System.Threading.Channels;

namespace Recorder.Collectors.Android;

/// <summary>
/// The phone's accessibility events, through the companion app's
/// connection (android-implementation-plan.md, slice 7).
/// </summary>
public interface IPhoneAccessibilityEvents
{
    /// <summary>
    /// Asks the companion app for every accessibility event from now on.
    /// The events come in the order they arrived, and the reader completes
    /// when the connection ends. Null when there is no connection to ask.
    /// </summary>
    Task<ChannelReader<ReceivedAccessibilityEvent>?> RequestAccessibilityEventsAsync(CancellationToken cancellation);
}

/// <summary>
/// Snapshots of the phone's accessibility tree, through the companion
/// app's connection (android-implementation-plan.md, slice 7, step 7b).
/// </summary>
public interface IPhoneAccessibilityTrees
{
    /// <summary>
    /// Asks the companion app for a snapshot of every window now and more on
    /// each change. The snapshots come in the order they arrived, and the
    /// reader completes when the connection ends. Null when there is no
    /// connection to ask.
    /// </summary>
    Task<ChannelReader<ReceivedAccessibilityTree>?> RequestAccessibilityTreesAsync(CancellationToken cancellation);
}

/// <summary>
/// Screenshots of the phone's display, through the companion app's
/// connection (android-checks.md, step 4a).
/// </summary>
public interface IPhoneScreenshots
{
    /// <summary>
    /// Asks the companion app for a screenshot now and one each time a
    /// window settles. The screenshots and refusals come in the order they
    /// arrived, and the reader completes when the connection ends. Null
    /// when there is no connection to ask.
    /// </summary>
    Task<ChannelReader<ReceivedScreenshot>?> RequestScreenshotsAsync(CancellationToken cancellation);
}

/// <summary>
/// Notices that a phone setting was written, through the companion app's
/// connection (android-implementation-plan.md, slice 8, step 8a).
/// </summary>
public interface IPhoneSettingChanges
{
    /// <summary>
    /// Asks the companion app to say each time one of the settings is
    /// written from now on. The notices come in the order they arrived, and
    /// the reader completes when the connection ends. Null when there is no
    /// connection to ask.
    /// </summary>
    Task<ChannelReader<ReceivedSettingChange>?> RequestSettingChangesAsync(
        IReadOnlyList<Recorder.Contracts.AndroidSetting> settings,
        CancellationToken cancellation);
}
