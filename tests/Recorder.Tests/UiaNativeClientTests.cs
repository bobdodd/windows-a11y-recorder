using Recorder.Collectors.Automation;

namespace Recorder.Tests;

// The UI Automation collector's text for the native client's numeric
// identifiers and values, and its handler guard. The names are those the
// managed client, System.Windows.Automation, gave on the target machine; see
// docs/architecture/uia-native-client.md.
public sealed class UiaNativeClientTests
{
    [Theory]
    [InlineData(20002, "AutomationElementIdentifiers.StructureChangedEvent")]
    [InlineData(20005, "AutomationElementIdentifiers.AutomationFocusChangedEvent")]
    [InlineData(20009, "InvokePatternIdentifiers.InvokedEvent")]
    [InlineData(20012, "SelectionItemPatternIdentifiers.ElementSelectedEvent")]
    [InlineData(20015, "TextPatternIdentifiers.TextChangedEvent")]
    public void NamesEachSubscribedEventAsTheManagedClientDid(int eventId, string name)
    {
        Assert.Equal(name, UiaEvidenceText.EventName(eventId));
    }

    [Theory]
    [InlineData(30005, "AutomationElementIdentifiers.NameProperty")]
    [InlineData(30008, "AutomationElementIdentifiers.HasKeyboardFocusProperty")]
    [InlineData(30010, "AutomationElementIdentifiers.IsEnabledProperty")]
    [InlineData(30045, "ValuePatternIdentifiers.ValueProperty")]
    [InlineData(30086, "TogglePatternIdentifiers.ToggleStateProperty")]
    [InlineData(30070, "ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty")]
    public void NamesEachChangedPropertyAsTheManagedClientDid(int propertyId, string name)
    {
        Assert.Equal(name, UiaEvidenceText.PropertyName(propertyId));
    }

    [Fact]
    public void SubscribesToEveryNamedProperty()
    {
        Assert.All(
            UiaEvidenceText.ChangedPropertyIds,
            id => Assert.Contains("Identifiers.", UiaEvidenceText.PropertyName(id), StringComparison.Ordinal));
        Assert.Equal(13, UiaEvidenceText.SnapshotPropertyIds.Count);
        Assert.Equal(13, UiaEvidenceText.SnapshotPropertyIds.Distinct().Count());
    }

    [Theory]
    [InlineData(50000, "ControlType.Button")]
    [InlineData(50004, "ControlType.Edit")]
    [InlineData(50020, "ControlType.Text")]
    [InlineData(50028, "ControlType.DataGrid")]
    [InlineData(50033, "ControlType.Pane")]
    [InlineData(50038, "ControlType.Separator")]
    public void NamesControlTypesAsTheManagedClientDid(int id, string name)
    {
        Assert.Equal(name, UiaEvidenceText.ControlTypeName(id));
    }

    [Theory]
    [InlineData(50039)]
    [InlineData(50040)]
    [InlineData(49999)]
    public void GivesNoNameForAControlTypeTheManagedClientDidNotName(int id)
    {
        Assert.Null(UiaEvidenceText.ControlTypeName(id));
    }

    [Fact]
    public void GivesNoControlTypeForAValueThatIsNotAnIdentifier()
    {
        Assert.Null(UiaEvidenceText.ControlTypeName(null));
        Assert.Null(UiaEvidenceText.ControlTypeName("Button"));
    }

    [Theory]
    [InlineData(0, "ChildAdded")]
    [InlineData(1, "ChildRemoved")]
    [InlineData(2, "ChildrenInvalidated")]
    [InlineData(3, "ChildrenBulkAdded")]
    [InlineData(4, "ChildrenBulkRemoved")]
    [InlineData(5, "ChildrenReordered")]
    public void NamesStructureChangesAsTheManagedClientDid(int changeType, string name)
    {
        Assert.Equal(name, UiaEvidenceText.StructureChangeName(changeType));
    }

    [Theory]
    [InlineData(30086, 0, "Off")]
    [InlineData(30086, 1, "On")]
    [InlineData(30086, 2, "Indeterminate")]
    [InlineData(30070, 0, "Collapsed")]
    [InlineData(30070, 1, "Expanded")]
    [InlineData(30070, 2, "PartiallyExpanded")]
    [InlineData(30070, 3, "LeafNode")]
    public void NamesStateValuesByTheirMember(int propertyId, int value, string name)
    {
        Assert.Equal(name, UiaEvidenceText.NormalizeValue(propertyId, value));
    }

    [Fact]
    public void WritesOtherValuesAsTheManagedClientDid()
    {
        Assert.Null(UiaEvidenceText.NormalizeValue(30005, null));
        Assert.Equal("true", UiaEvidenceText.NormalizeValue(30008, true));
        Assert.Equal("false", UiaEvidenceText.NormalizeValue(30010, false));
        Assert.Equal("Next", UiaEvidenceText.NormalizeValue(30005, "Next"));
        Assert.Equal("", UiaEvidenceText.NormalizeValue(30045, ""));
    }

    [Fact]
    public void ReadsABoundingRectangleAsLeftTopWidthAndHeight()
    {
        Assert.Equal((10.0, 20.0, 300.0, 40.0), UiaEvidenceText.Rectangle(new[] { 10.0, 20.0, 300.0, 40.0 }));
        Assert.Null(UiaEvidenceText.Rectangle(null));
        Assert.Null(UiaEvidenceText.Rectangle(new[] { 1.0, 2.0 }));
    }

    [Fact]
    public void ReadsPropertyValuesOnlyOfTheirType()
    {
        Assert.Equal(42, UiaEvidenceText.Integer(42));
        Assert.Null(UiaEvidenceText.Integer("42"));
        Assert.Equal("Edit", UiaEvidenceText.Text("Edit"));
        Assert.Null(UiaEvidenceText.Text(7));
        Assert.True(UiaEvidenceText.Boolean(true));
        Assert.Null(UiaEvidenceText.Boolean(1));
    }

    [Theory]
    [InlineData(unchecked((int)0x80040201), "element-not-available")]
    [InlineData(unchecked((int)0x80010108), "element-not-available")]
    [InlineData(unchecked((int)0x800706BA), "element-not-available")]
    [InlineData(unchecked((int)0x80131509), "element-property-read-failed")]
    [InlineData(unchecked((int)0x80131505), "uia-provider-error")]
    [InlineData(unchecked((int)0x80004005), "uia-provider-error")]
    public void FlagsAFailedReadAsTheManagedClientsExceptionDid(int resultCode, string flag)
    {
        Assert.Equal(flag, UiaEvidenceText.ReadFailureFlag(resultCode));
    }

    [Fact]
    public void ReportsAFaultingHandlerAndKeepsHandlingLaterEvents()
    {
        var faults = 0;
        var handled = new List<int>();

        UiaHandlerGuard.Run(() => handled.Add(1), () => faults++);
        UiaHandlerGuard.Run(() => throw new InvalidCastException(), () => faults++);
        UiaHandlerGuard.Run(() => throw new ArgumentNullException("runtimeId"), () => faults++);
        UiaHandlerGuard.Run(() => handled.Add(4), () => faults++);

        Assert.Equal(2, faults);
        Assert.Equal([1, 4], handled);
    }
}
