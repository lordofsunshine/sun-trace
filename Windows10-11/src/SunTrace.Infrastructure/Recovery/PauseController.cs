using Microsoft.Win32;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;
using SunTrace.Infrastructure.Windows;

namespace SunTrace.Infrastructure.Recovery;

public sealed class PauseStartResult
{
    public bool Success { get; init; }

    public bool RequiresElevation { get; init; }

    public string Message { get; init; } = string.Empty;

    public PauseDescriptor? Descriptor { get; init; }
}

public static partial class PauseController
{
    public static bool RequiresElevation(PauseDescriptor descriptor)
    {
        if (descriptor.Kind == PauseTargetKind.ScheduledTask)
        {
            return true;
        }

        return IsMachinePath(descriptor.RegistryPath) || IsMachinePath(descriptor.ApproveRegistryPath);
    }

    public static PauseStartResult Pause(PauseDescriptor descriptor)
    {
        var prepared = Prepare(descriptor);
        if (!prepared.Success || prepared.Descriptor is null)
        {
            return prepared;
        }

        return Apply(prepared.Descriptor);
    }

    public static PauseStartResult Prepare(PauseDescriptor descriptor)
    {
        if (!PausePolicy.TryValidate(descriptor, out var error))
        {
            return new PauseStartResult { Message = error };
        }

        try
        {
            return descriptor.Kind switch
            {
                PauseTargetKind.RegistryStartup => PrepareRegistry(descriptor),
                PauseTargetKind.StartupFolder => PrepareStartupFolder(descriptor),
                PauseTargetKind.ScheduledTask => PrepareTask(descriptor),
                _ => new PauseStartResult { Message = "Этот тип компонента пока нельзя временно отключить." }
            };
        }
        catch (Exception ex)
        {
            return new PauseStartResult { Message = "Не удалось отключить компонент: " + ex.Message };
        }
    }

    public static PauseStartResult Apply(PauseDescriptor prepared)
    {
        if (!PausePolicy.TryValidate(prepared, out var error))
        {
            return new PauseStartResult { Message = error };
        }

        try
        {
            return prepared.Kind switch
            {
                PauseTargetKind.RegistryStartup or PauseTargetKind.StartupFolder => ApplyApprove(prepared),
                PauseTargetKind.ScheduledTask => ApplyTask(prepared),
                _ => new PauseStartResult { Message = "Этот тип компонента пока нельзя временно отключить." }
            };
        }
        catch (Exception ex)
        {
            return new PauseStartResult { Message = "Не удалось отключить компонент: " + ex.Message };
        }
    }

    internal static RegistryView ViewFor(PauseDescriptor descriptor) =>
        descriptor.ApproveRegistryPath?.TrimEnd('\\').EndsWith(@"\Run32", StringComparison.OrdinalIgnoreCase) == true
            ? RegistryView.Registry32
            : RegistryView.Registry64;

    private static PauseStartResult PrepareRegistry(PauseDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.RegistryPath)
            || string.IsNullOrWhiteSpace(descriptor.ApproveRegistryPath)
            || string.IsNullOrWhiteSpace(descriptor.ValueName))
        {
            return new PauseStartResult { Message = "Недостаточно данных для отключения элемента." };
        }

        var command = RegistryReader.GetString(descriptor.RegistryPath, descriptor.ValueName, ViewFor(descriptor));
        if (command is null)
        {
            return new PauseStartResult { Message = "Элемент больше не существует в автозагрузке." };
        }

        var currentApprove = RegistryReader.GetBinary(descriptor.ApproveRegistryPath, descriptor.ValueName);
        if (IsDisabled(currentApprove))
        {
            return new PauseStartResult { Message = "Этот элемент уже отключён в Windows." };
        }

        var updated = Copy(descriptor);
        updated.OriginalEnabled = true;
        updated.OriginalDetail = command;
        updated.OriginalSignature = Signature(command, true, null);
        updated.OriginalApproveValueExisted = currentApprove is not null;
        updated.OriginalApproveValueBase64 = currentApprove is null ? null : Convert.ToBase64String(currentApprove);

        return new PauseStartResult { Success = true, Descriptor = updated, Message = "Элемент автозагрузки готов к отключению." };
    }

    private static PauseStartResult PrepareStartupFolder(PauseDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.FilePath)
            || string.IsNullOrWhiteSpace(descriptor.ApproveRegistryPath)
            || string.IsNullOrWhiteSpace(descriptor.ValueName))
        {
            return new PauseStartResult { Message = "Недостаточно данных для отключения элемента." };
        }

        if (!File.Exists(descriptor.FilePath))
        {
            return new PauseStartResult { Message = "Файл автозагрузки больше не существует." };
        }

        var currentApprove = RegistryReader.GetBinary(descriptor.ApproveRegistryPath, descriptor.ValueName);
        if (IsDisabled(currentApprove))
        {
            return new PauseStartResult { Message = "Этот элемент уже отключён в Windows." };
        }

        var updated = Copy(descriptor);
        updated.OriginalEnabled = true;
        updated.OriginalDetail = descriptor.FilePath;
        updated.OriginalSignature = Signature(descriptor.FilePath, true, null);
        updated.OriginalApproveValueExisted = currentApprove is not null;
        updated.OriginalApproveValueBase64 = currentApprove is null ? null : Convert.ToBase64String(currentApprove);

        return new PauseStartResult { Success = true, Descriptor = updated, Message = "Элемент автозагрузки готов к отключению." };
    }

    private static PauseStartResult PrepareTask(PauseDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.TaskPath))
        {
            return new PauseStartResult { Message = "Недостаточно данных задания планировщика." };
        }

        if (!Monitoring.ScheduledTaskScanner.TryReadTask(descriptor.TaskPath, out var task, out var error) || task is null)
        {
            return new PauseStartResult { Message = error ?? "Задание планировщика не найдено." };
        }

        if (!task.Enabled)
        {
            return new PauseStartResult { Message = "Задание уже отключено." };
        }

        var updated = Copy(descriptor);
        updated.OriginalEnabled = true;
        updated.OriginalDetail = task.Detail;
        updated.OriginalSignature = Signature(task.Detail, true, task.Details.GetValueOrDefault("Триггеров"));

        return new PauseStartResult { Success = true, Descriptor = updated, Message = "Задание планировщика готово к отключению." };
    }

    private static PauseStartResult ApplyApprove(PauseDescriptor prepared)
    {
        if (!RegistryReader.WriteBinary(prepared.ApproveRegistryPath!, prepared.ValueName!, CreateDisabledValue()))
        {
            return new PauseStartResult
            {
                RequiresElevation = !RegistryReader.IsElevated,
                Message = "Нет доступа к параметрам автозагрузки. Возможно, нужны права администратора."
            };
        }

        if (!IsDisabled(RegistryReader.GetBinary(prepared.ApproveRegistryPath!, prepared.ValueName!)))
        {
            return new PauseStartResult { Message = "Windows не подтвердила отключение элемента." };
        }

        return new PauseStartResult
        {
            Success = true,
            Descriptor = prepared,
            Message = "Элемент автозагрузки временно отключён."
        };
    }

    private static PauseStartResult ApplyTask(PauseDescriptor prepared)
    {
        if (!Monitoring.ScheduledTaskScanner.TrySetEnabled(prepared.TaskPath!, false, out var error))
        {
            return new PauseStartResult
            {
                RequiresElevation = !RegistryReader.IsElevated,
                Message = error ?? "Не удалось отключить задание."
            };
        }

        return new PauseStartResult
        {
            Success = true,
            Descriptor = prepared,
            Message = "Задание планировщика временно отключено."
        };
    }
}
