using Microsoft.Extensions.DependencyInjection;
using SunTrace.Core.Interfaces;
using SunTrace.Infrastructure.Monitoring;
using SunTrace.Infrastructure.Recovery;
using SunTrace.Infrastructure.Services;
using SunTrace.Infrastructure.Settings;
using SunTrace.Infrastructure.Storage;
using SunTrace.Infrastructure.Windows;

namespace SunTrace.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSunTraceInfrastructure(this IServiceCollection services, DataPaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton<SignatureReader>();
        services.AddSingleton<FileMetadataReader>();
        services.AddSingleton<ShortcutResolver>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IHistoryStore, SqliteHistoryStore>();
        services.AddSingleton<IAppIconProvider, AppIconService>();
        services.AddSingleton<IReportExporter, ReportExporter>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<AutostartService>();
        services.AddSingleton<SystemChangeWatcher>();
        services.AddSingleton<MonitoringService>();
        services.AddSingleton<TrackingService>();
        services.AddSingleton<ElevationHost>();
        services.AddSingleton<IScanCoordinator, ScanCoordinator>();

        services.AddSingleton<SoftwareInventoryScanner>();
        services.AddSingleton<StartupScanner>();
        services.AddSingleton<ServiceScanner>();
        services.AddSingleton<ScheduledTaskScanner>();
        services.AddSingleton<ProgramFilesScanner>();

        services.AddSingleton<ISystemScanner, SystemScanner>();
        services.AddSingleton<IComponentPauseService, ComponentPauseService>();

        return services;
    }
}
