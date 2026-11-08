using Microsoft.Extensions.Logging;
using SunTrace.Core.Enums;
using SunTrace.Core.Interfaces;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Monitoring;

public sealed class SystemScanner : ISystemScanner
{
    private const int TotalSteps = 5;

    private readonly SoftwareInventoryScanner _inventory;
    private readonly StartupScanner _startup;
    private readonly ServiceScanner _services;
    private readonly ScheduledTaskScanner _tasks;
    private readonly ProgramFilesScanner _files;
    private readonly ILogger<SystemScanner> _logger;

    public SystemScanner(
        SoftwareInventoryScanner inventory,
        StartupScanner startup,
        ServiceScanner services,
        ScheduledTaskScanner tasks,
        ProgramFilesScanner files,
        ILogger<SystemScanner> logger)
    {
        _inventory = inventory;
        _startup = startup;
        _services = services;
        _tasks = tasks;
        _files = files;
        _logger = logger;
    }

    public async Task<SystemSnapshot> ScanAsync(ScanReason reason, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capturedAt = DateTimeOffset.Now;
        var completed = 0;

        SectionResult Execute(ScanSection section, string message, string failure, Func<SectionResult> scan)
        {
            progress?.Report(CreateProgress(section, message, Volatile.Read(ref completed)));
            var previousPriority = Thread.CurrentThread.Priority;
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            try
            {
                return scan();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, failure);
                return SectionResult.Unavailable(section, ex.Message);
            }
            finally
            {
                Thread.CurrentThread.Priority = previousPriority;
                Interlocked.Increment(ref completed);
            }
        }

        var inventoryTask = Task.Run(() => Execute(
            ScanSection.Applications,
            "Читаем список установленных программ",
            "Ошибка чтения списка программ.",
            () =>
            {
                var result = _inventory.Scan(cancellationToken);
                return SectionResult.From(ScanSection.Applications, result.IsComplete, result.Warnings, result.Apps, null);
            }), cancellationToken);

        var startupTask = Task.Run(() => Execute(
            ScanSection.Startup,
            "Проверяем автозагрузку",
            "Ошибка чтения автозагрузки.",
            () =>
            {
                var result = _startup.Scan(cancellationToken);
                return SectionResult.From(ScanSection.Startup, result.IsComplete, result.Warnings, null, result.Components);
            }), cancellationToken);

        var servicesTask = Task.Run(() => Execute(
            ScanSection.Services,
            "Читаем службы Windows",
            "Ошибка чтения служб.",
            () =>
            {
                var result = _services.Scan(cancellationToken);
                return SectionResult.From(ScanSection.Services, result.IsComplete, result.Warnings, null, result.Components);
            }), cancellationToken);

        var tasksTask = Task.Run(() => Execute(
            ScanSection.ScheduledTasks,
            "Проверяем задания планировщика",
            "Ошибка чтения заданий планировщика.",
            () =>
            {
                var result = _tasks.Scan(cancellationToken);
                return SectionResult.From(ScanSection.ScheduledTasks, result.IsComplete, result.Warnings, null, result.Components);
            }), cancellationToken);

        var inventory = await inventoryTask.ConfigureAwait(false);

        var files = await Task.Run(() => Execute(
            ScanSection.Files,
            "Проверяем файлы программ",
            "Ошибка чтения файлов программ.",
            () =>
            {
                var result = _files.Scan(inventory.Apps, cancellationToken);
                return SectionResult.From(ScanSection.Files, result.IsComplete, result.Warnings, null, result.Components);
            }), cancellationToken).ConfigureAwait(false);

        var background = await Task.WhenAll(startupTask, servicesTask, tasksTask).ConfigureAwait(false);
        SectionResult[] sections = [inventory, background[0], background[1], background[2], files];

        var snapshot = new SystemSnapshot
        {
            CapturedAt = capturedAt,
            Reason = reason,
            Apps = inventory.Apps.ToList()
        };

        foreach (var section in sections)
        {
            snapshot.Sections[section.Section] = section.ToReport();
        }

        snapshot.Components = sections
            .SelectMany(section => section.Components)
            .GroupBy(component => component.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(component => component.Id, StringComparer.Ordinal)
            .ToList();

        progress?.Report(new ScanProgress
        {
            Section = ScanSection.Files,
            Message = "Сканирование завершено",
            CompletedSections = TotalSteps,
            TotalSections = TotalSteps,
            IsFinished = true
        });

        return snapshot;
    }

    private static ScanProgress CreateProgress(ScanSection section, string message, int completed) => new()
    {
        Section = section,
        Message = message,
        CompletedSections = completed,
        TotalSections = TotalSteps
    };

    private sealed record SectionResult(
        ScanSection Section,
        SectionStatus Status,
        string? Message,
        IReadOnlyList<InstalledApp> Apps,
        IReadOnlyList<ComponentSnapshot> Components)
    {
        public static SectionResult From(
            ScanSection section,
            bool isComplete,
            IReadOnlyCollection<string> warnings,
            IReadOnlyList<InstalledApp>? apps,
            IReadOnlyList<ComponentSnapshot>? components) =>
            new(
                section,
                isComplete ? SectionStatus.Complete : SectionStatus.Partial,
                warnings.Count == 0 ? null : string.Join(" ", warnings.Distinct()),
                apps ?? Array.Empty<InstalledApp>(),
                components ?? Array.Empty<ComponentSnapshot>());

        public static SectionResult Unavailable(ScanSection section, string message) =>
            new(section, SectionStatus.Unavailable, message, Array.Empty<InstalledApp>(), Array.Empty<ComponentSnapshot>());

        public SectionReport ToReport() => new()
        {
            Section = Section,
            Status = Status,
            ItemsScanned = Apps.Count + Components.Count,
            Message = Message
        };
    }
}
