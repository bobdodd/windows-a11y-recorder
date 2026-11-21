namespace Recorder.Contracts;

/// <summary>
/// Static accessibility evidence for the selected package version. A component
/// declaration is not a measured screen, even when it has a stable resource ID.
/// </summary>
public sealed record AndroidApkAccessibilityEvidence(
    int SchemaVersion,
    IReadOnlyList<AndroidApkAccessibilityItem> Items,
    IReadOnlyList<AndroidApkAccessibilityCoverage> Coverage);

/// <param name="Kind">bundled, declared, or symbol. Runtime observations have a separate recording source.</param>
/// <param name="Scope">app or component. The latter does not imply a match to a screen visit.</param>
/// <param name="Category">The accessibility topic, independent of framework.</param>
/// <param name="Split">APK basename only, never the installation path.</param>
/// <param name="Locator">Archive entry and, where known, element or property.</param>
/// <param name="Value">The declared or bundled value, not an inference.</param>
public sealed record AndroidApkAccessibilityItem(
    string Kind, string Scope, string Category, string Split, string Locator, string Value);

/// <summary>What was actually inspected, recognized but not decoded, or incomplete.</summary>
public sealed record AndroidApkAccessibilityCoverage(string Reader, string Status, string Detail);
