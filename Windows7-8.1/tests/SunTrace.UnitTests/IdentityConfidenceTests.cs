using SunTrace.Core.Detection;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.UnitTests;

public sealed class IdentityConfidenceTests
{
    [Fact]
    public void ApplicationIdIgnoresNameNoiseAndPublisherSuffixes()
    {
        var first = TestData.App(name: "Acme Tool (x64)", publisher: "Acme, Inc.");
        var second = TestData.App(name: "Acme Tool", publisher: "ACME LLC");

        Assert.Equal(AppIdentity.CreateId(first), AppIdentity.CreateId(second));
        Assert.Equal("acme tool", AppIdentity.NormalizeName("Acme Tool 1.2.3"));
        Assert.Equal("acme", AppIdentity.NormalizePublisher("Acme, Inc."));
    }

    [Fact]
    public void SameProductMatchesChangedRegistryIdentity()
    {
        var first = TestData.App(id: "registry-one", publisher: "Acme, Inc.");
        var second = TestData.App(id: "registry-two", publisher: "Acme LLC");

        Assert.True(AppIdentity.AreSameProduct(first, second));
        Assert.True(AppIdentity.NamesRelated("Acme Desktop Suite", "Acme Suite"));
        Assert.False(AppIdentity.NamesRelated("Acme Tool", "Other Product"));
    }

    [Fact]
    public void NormalizedPathRemovesQuotesAndExecutableName()
    {
        var normalized = AppIdentity.NormalizedPath("\"C:\\Program Files\\Acme\\AcmeTool.exe\"");

        Assert.Equal(@"c:\program files\acme", normalized);
        Assert.True(PathHeuristics.IsUnder(@"C:\Program Files\Acme\agent.exe", @"C:\Program Files\Acme"));
        Assert.False(PathHeuristics.IsUnder(@"C:\Program Files\Other\agent.exe", @"C:\Program Files\Acme"));
        Assert.False(PathHeuristics.IsUsableInstallLocation(@"C:\Program Files"));
    }

    [Fact]
    public void ExtractsExecutableFromQuotedCommand()
    {
        var path = PathHeuristics.ExtractExecutablePath("\"C:\\Program Files\\Acme\\agent.exe\" --background");

        Assert.Equal(@"C:\Program Files\Acme\agent.exe", path);
    }

    [Fact]
    public void ComponentInInstallFolderHasHighConfidence()
    {
        var app = TestData.App();
        var component = TestData.Startup();

        var assessment = new ConfidenceEvaluator().Evaluate(component, [app]);

        Assert.Equal(ConfidenceLevel.High, assessment.Level);
        Assert.Equal(app.Id, assessment.App?.Id);
        Assert.Contains(assessment.Reasons, reason => reason.Contains("папке установки", StringComparison.Ordinal));
    }

    [Fact]
    public void UnrelatedComponentHasUnknownConfidence()
    {
        var component = new ComponentSnapshot
        {
            Id = "startup-unknown",
            Type = ComponentType.Startup,
            Name = "Zeta Helper",
            Detail = @"C:\Other\zeta.exe",
            ExecutablePath = @"C:\Other\zeta.exe"
        };

        var assessment = new ConfidenceEvaluator().Evaluate(component, [TestData.App()]);

        Assert.Equal(ConfidenceLevel.Unknown, assessment.Level);
        Assert.Null(assessment.App);
    }

    [Fact]
    public void AssociationIndexUsesLongestMatchingInstallFolder()
    {
        var broad = TestData.App(id: "app-broad", name: "Acme Bundle", location: @"C:\Program Files\Acme");
        var narrow = TestData.App(id: "app-narrow", name: "Acme Agent", location: @"C:\Program Files\Acme\Agent");
        var component = TestData.Startup(executable: @"C:\Program Files\Acme\Agent\agent.exe");

        var resolved = new AppAssociationIndex([broad, narrow]).Resolve(component);

        Assert.Equal("app-narrow", resolved);
    }

    [Fact]
    public void StartupInTempFolderRequiresAttention()
    {
        var component = new ComponentSnapshot
        {
            Id = "startup-temp",
            Type = ComponentType.Startup,
            Name = "Temp Agent",
            Detail = Path.Combine(Path.GetTempPath(), "agent.exe"),
            ExecutablePath = Path.Combine(Path.GetTempPath(), "agent.exe"),
            Enabled = true
        };

        var (level, note) = ImportanceEvaluator.Evaluate(component, ComponentType.Startup, ChangeKind.Added, ConfidenceLevel.High);

        Assert.Equal(ImportanceLevel.Attention, level);
        Assert.NotNull(note);
    }

    [Fact]
    public void StartupInProgramFilesNeedsReviewOnly()
    {
        var (level, note) = ImportanceEvaluator.Evaluate(TestData.Startup(), ComponentType.Startup, ChangeKind.Added, ConfidenceLevel.High);

        Assert.Equal(ImportanceLevel.Check, level);
        Assert.NotNull(note);
    }
}
