using SunTrace.Core.Detection;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.UnitTests;

public sealed class SnapshotDetectionTests
{
    private readonly SnapshotComparer _comparer = new();

    [Fact]
    public void AddedApplicationIsDetected()
    {
        var previous = TestData.Snapshot();
        var current = TestData.Snapshot(TestData.App());

        var diff = _comparer.Compare(previous, current);

        var change = Assert.Single(diff.AppChanges);
        Assert.Equal(ChangeKind.Added, change.Kind);
        Assert.Equal("app-acme", change.AppId);
        Assert.False(change.IsUpdate);
        Assert.Equal(1, diff.AddedCount);
    }

    [Fact]
    public void UpdatedApplicationIsDetected()
    {
        var previous = TestData.Snapshot(TestData.App(version: "1.0.0"));
        var current = TestData.Snapshot(TestData.App(version: "2.1.0"));

        var diff = _comparer.Compare(previous, current);

        var change = Assert.Single(diff.AppChanges);
        Assert.Equal(ChangeKind.Modified, change.Kind);
        Assert.True(change.IsUpdate);
        Assert.Contains(change.Fields, f => f.Label == "Версия" && f.Before == "1.0.0" && f.After == "2.1.0");
    }

    [Fact]
    public void RemovedApplicationIsDetected()
    {
        var previous = TestData.Snapshot(TestData.App());
        var current = TestData.Snapshot();

        var diff = _comparer.Compare(previous, current);

        var change = Assert.Single(diff.AppChanges);
        Assert.Equal(ChangeKind.Removed, change.Kind);
        Assert.Equal("app-acme", change.AppId);
        Assert.Null(change.After);
    }

    [Fact]
    public void IdenticalSnapshotsProduceNoChanges()
    {
        var previous = TestData.Snapshot(TestData.App());
        var current = TestData.Snapshot(TestData.App());

        var diff = _comparer.Compare(previous, current);

        Assert.False(diff.HasChanges);
        Assert.Empty(diff.AppChanges);
        Assert.Empty(diff.ComponentChanges);
    }

    [Fact]
    public void AddedComponentIsDetectedInCompleteSection()
    {
        var previous = TestData.Snapshot();
        var current = TestData.Snapshot();
        current.Components.Add(TestData.Startup());

        var diff = _comparer.Compare(previous, current);

        var change = Assert.Single(diff.ComponentChanges);
        Assert.Equal(ChangeKind.Added, change.Kind);
        Assert.Equal(ComponentType.Startup, change.Type);
        Assert.Equal("startup-acme", change.Key);
    }

    [Fact]
    public void RemovedComponentIsDetectedInCompleteSection()
    {
        var previous = TestData.Snapshot();
        previous.Components.Add(TestData.Startup());
        var current = TestData.Snapshot();

        var diff = _comparer.Compare(previous, current);

        var change = Assert.Single(diff.ComponentChanges);
        Assert.Equal(ChangeKind.Removed, change.Kind);
        Assert.NotNull(change.Before);
        Assert.Null(change.After);
    }

    [Theory]
    [InlineData(SectionStatus.Partial)]
    [InlineData(SectionStatus.Unavailable)]
    public void IncompleteSectionSuppressesAddedAndRemovedComponents(SectionStatus status)
    {
        var previous = TestData.Snapshot();
        previous.Components.Add(TestData.Startup());
        var current = TestData.Snapshot();
        current.Components.Add(TestData.Startup("startup-new", "New Agent", @"C:\Program Files\Acme\new.exe"));
        current.Sections[ScanSection.Startup] = new SectionReport
        {
            Section = ScanSection.Startup,
            Status = status,
            ItemsScanned = 0,
            Message = "Раздел недоступен"
        };

        var diff = _comparer.Compare(previous, current);

        Assert.Empty(diff.ComponentChanges);
    }

    [Fact]
    public void ApplicationChangesSurviveUnavailableComponentSection()
    {
        var previous = TestData.Snapshot(TestData.App(version: "1.0.0"));
        var current = TestData.Snapshot(TestData.App(version: "1.4.0"));
        current.Sections[ScanSection.Services] = new SectionReport
        {
            Section = ScanSection.Services,
            Status = SectionStatus.Unavailable,
            Message = "Нет доступа"
        };

        var diff = _comparer.Compare(previous, current);

        var change = Assert.Single(diff.AppChanges);
        Assert.True(change.IsUpdate);
    }

    [Fact]
    public void ComponentChangeWithoutDetectableFieldsIsIgnored()
    {
        var previous = TestData.Snapshot();
        var component = TestData.Startup();
        previous.Components.Add(component);
        var current = TestData.Snapshot();
        current.Components.Add(TestData.Startup());

        var diff = _comparer.Compare(previous, current);

        Assert.Empty(diff.ComponentChanges);
    }
}
