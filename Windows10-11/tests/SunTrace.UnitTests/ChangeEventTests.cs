using SunTrace.Core.Detection;
using SunTrace.Core.Enums;

namespace SunTrace.UnitTests;

public sealed class ChangeEventTests
{
    private readonly SnapshotComparer _comparer = new();
    private readonly ChangeEventFactory _factory = new();

    [Fact]
    public void AddedApplicationProducesReadableEvent()
    {
        var previous = TestData.Snapshot();
        var current = TestData.Snapshot(TestData.App());
        var diff = _comparer.Compare(previous, current);

        var events = _factory.Build(diff, previous, current, 42, TestData.Now);
        var item = Assert.Single(events);

        Assert.Equal("Программа установлена", item.Title);
        Assert.Equal("app-acme", item.AppId);
        Assert.Equal("Acme Tool", item.AppName);
        Assert.Equal(ChangeKind.Added, item.Kind);
        Assert.Equal(ComponentType.Application, item.ComponentType);
        Assert.Equal(ConfidenceLevel.High, item.Confidence);
        Assert.Equal(42, item.SnapshotId);
        Assert.Equal(TestData.Now, item.DetectedAt);
        Assert.Equal(32, item.Fingerprint.Length);
        Assert.Contains("1.0.0", item.Description);
    }

    [Fact]
    public void UpdatedApplicationEventMentionsBothVersions()
    {
        var previous = TestData.Snapshot(TestData.App(version: "1.0.0"));
        var current = TestData.Snapshot(TestData.App(version: "3.2.1"));
        var diff = _comparer.Compare(previous, current);

        var item = Assert.Single(_factory.Build(diff, previous, current, 7, TestData.Now));

        Assert.Equal("Обновление программы", item.Title);
        Assert.Contains("1.0.0", item.Description);
        Assert.Contains("3.2.1", item.Description);
        Assert.Contains(item.Details.Fields, field => field.Label == "Версия");
    }

    [Fact]
    public void ComponentEventIsLinkedToApplicationByInstallLocation()
    {
        var app = TestData.App();
        var previous = TestData.Snapshot(app);
        var current = TestData.Snapshot(app);
        current.Components.Add(TestData.Startup());
        var diff = _comparer.Compare(previous, current);

        var item = Assert.Single(_factory.Build(diff, previous, current, 11, TestData.Now));

        Assert.Equal(ComponentType.Startup, item.ComponentType);
        Assert.Equal(app.Id, item.AppId);
        Assert.Equal(app.Name, item.AppName);
        Assert.True(item.Confidence is ConfidenceLevel.High or ConfidenceLevel.Medium);
        Assert.NotEmpty(item.Details.Explanation ?? string.Empty);
    }

    [Fact]
    public void RepeatedBuildProducesSameFingerprintsWithoutDuplicates()
    {
        var previous = TestData.Snapshot();
        var current = TestData.Snapshot(TestData.App());
        current.Components.Add(TestData.Startup());
        var diff = _comparer.Compare(previous, current);

        var first = _factory.Build(diff, previous, current, 3, TestData.Now);
        var second = _factory.Build(diff, previous, current, 3, TestData.Now);

        Assert.Equal(2, first.Count);
        Assert.Equal(first.Select(e => e.Fingerprint), second.Select(e => e.Fingerprint));
        Assert.Equal(first.Count, first.Select(e => e.Fingerprint).Distinct().Count());
    }

    [Fact]
    public void FingerprintChangesWhenComponentStateChanges()
    {
        var enabled = ChangeEventFactory.CreateFingerprint("startup-acme", "Startup", ChangeKind.Modified, "enabled|True");
        var disabled = ChangeEventFactory.CreateFingerprint("startup-acme", "Startup", ChangeKind.Modified, "enabled|False");

        Assert.NotEqual(enabled, disabled);
        Assert.Equal(32, enabled.Length);
        Assert.Matches("^[0-9a-f]+$", enabled);
    }

    [Fact]
    public void RemovedApplicationEventKeepsHistoryIdentity()
    {
        var previous = TestData.Snapshot(TestData.App());
        var current = TestData.Snapshot();
        var diff = _comparer.Compare(previous, current);

        var item = Assert.Single(_factory.Build(diff, previous, current, 4, TestData.Now));

        Assert.Equal("Программа удалена", item.Title);
        Assert.Null(item.AppId);
        Assert.Equal("app-acme", item.Details.Technical["Идентификатор программы"]);
    }
}
