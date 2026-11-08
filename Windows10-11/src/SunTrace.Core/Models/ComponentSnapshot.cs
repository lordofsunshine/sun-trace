using SunTrace.Core.Enums;

namespace SunTrace.Core.Models;

public sealed class ComponentSnapshot
{
    public string Id { get; set; } = string.Empty;

    public ComponentType Type { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Detail { get; set; }

    public string? ExecutablePath { get; set; }

    public string? Publisher { get; set; }

    public string? SignaturePublisher { get; set; }

    public bool Enabled { get; set; } = true;

    public string? StartMode { get; set; }

    public string? State { get; set; }

    public string? Owner { get; set; }

    public string? Location { get; set; }

    public long? SizeBytes { get; set; }

    public DateTimeOffset? ModifiedUtc { get; set; }

    public bool IsMicrosoft { get; set; }

    public bool IsSecuritySensitive { get; set; }

    public bool SupportsPause { get; set; }

    public PauseDescriptor? Pause { get; set; }

    public Dictionary<string, string> Details { get; set; } = new();

    public string TypeLabel => Type switch
    {
        ComponentType.Startup => "Автозагрузка",
        ComponentType.Service => "Служба",
        ComponentType.ScheduledTask => "Задание планировщика",
        ComponentType.File => "Файл",
        ComponentType.Folder => "Папка",
        ComponentType.Registry => "Реестр",
        ComponentType.Application => "Приложение",
        _ => "Компонент"
    };
}

public sealed class PauseDescriptor
{
    public PauseTargetKind Kind { get; set; }

    public string? RegistryPath { get; set; }

    public string? ApproveRegistryPath { get; set; }

    public string? ValueName { get; set; }

    public string? FilePath { get; set; }

    public string? TaskPath { get; set; }

    public string? TaskName { get; set; }

    public string? TaskFolder { get; set; }

    public bool OriginalEnabled { get; set; } = true;

    public bool OriginalApproveValueExisted { get; set; }

    public string? OriginalApproveValueBase64 { get; set; }

    public string? OriginalDetail { get; set; }

    public string? OriginalSignature { get; set; }

    public bool RequiresElevation => Kind == PauseTargetKind.ScheduledTask;
}
