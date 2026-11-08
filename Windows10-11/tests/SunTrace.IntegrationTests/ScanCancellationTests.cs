using Microsoft.Extensions.Logging.Abstractions;
using SunTrace.Core.Enums;
using SunTrace.Core.Interfaces;
using SunTrace.Core.Models;
using SunTrace.Infrastructure.Monitoring;
using SunTrace.Infrastructure.Settings;

namespace SunTrace.IntegrationTests;

public sealed class ScanCancellationTests
{
    [Fact]
    public async Task CancellingDuringScanReleasesCoordinatorAndAllowsNextScan()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var settings = new SettingsService(environment.Paths, NullLogger<SettingsService>.Instance);
        await settings.LoadAsync();
        var scanner = new CancellableScanner();
        var coordinator = new ScanCoordinator(
            scanner,
            environment.Store,
            settings,
            NullLogger<ScanCoordinator>.Instance);
        using var cancellation = new CancellationTokenSource();

        var running = coordinator.ScanAsync(ScanReason.Manual, null, null, null, cancellation.Token);
        await scanner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.False(coordinator.IsScanning);
        Assert.Null(await environment.Store.GetLatestSnapshotAsync());

        var outcome = await coordinator.ScanAsync(ScanReason.Manual, null, null, null, CancellationToken.None);
        Assert.True(outcome.IsFirstScan);
        Assert.True(outcome.SnapshotId > 0);
        Assert.NotNull(await environment.Store.GetLatestSnapshotAsync());
    }

    [Fact]
    public async Task ScanWithoutHistoryDoesNotWriteDatabaseWhenCancelled()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var settings = new SettingsService(environment.Paths, NullLogger<SettingsService>.Instance);
        await settings.LoadAsync();
        var scanner = new CancellableScanner();
        var coordinator = new ScanCoordinator(
            scanner,
            environment.Store,
            settings,
            NullLogger<ScanCoordinator>.Instance);
        using var cancellation = new CancellationTokenSource();

        var running = coordinator.ScanWithoutHistoryAsync(ScanReason.Manual, null, cancellation.Token);
        await scanner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.False(coordinator.IsScanning);
        Assert.Null(await environment.Store.GetLatestSnapshotAsync());
    }

    private sealed class CancellableScanner : ISystemScanner
    {
        private int _calls;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<SystemSnapshot> ScanAsync(
            ScanReason reason,
            IProgress<ScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return SnapshotFixtures.Snapshot(DateTimeOffset.Now);
        }
    }
}
