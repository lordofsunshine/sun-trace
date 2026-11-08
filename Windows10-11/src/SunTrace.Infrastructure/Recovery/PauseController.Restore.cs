using Microsoft.Win32;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;
using SunTrace.Infrastructure.Windows;

namespace SunTrace.Infrastructure.Recovery;

public static partial class PauseController
{
    public static RestoreResult Restore(PauseDescriptor descriptor)
    {
        if (!PausePolicy.TryValidate(descriptor, out var policyError))
        {
            return new RestoreResult { Outcome = RestoreOutcome.Failed, Message = policyError };
        }

        try
        {
            return descriptor.Kind switch
            {
                PauseTargetKind.RegistryStartup => RestoreRegistry(descriptor),
                PauseTargetKind.StartupFolder => RestoreStartupFolder(descriptor),
                PauseTargetKind.ScheduledTask => RestoreTask(descriptor),
                _ => new RestoreResult { Outcome = RestoreOutcome.Failed, Message = "Неизвестный тип компонента." }
            };
        }
        catch (Exception ex)
        {
            return new RestoreResult { Outcome = RestoreOutcome.Failed, Message = "Ошибка восстановления: " + ex.Message };
        }
    }

    public static RestoreResult Verify(PauseDescriptor descriptor)
    {
        if (!PausePolicy.TryValidate(descriptor, out var policyError))
        {
            return new RestoreResult { Outcome = RestoreOutcome.Failed, Message = policyError };
        }

        try
        {
            return descriptor.Kind switch
            {
                PauseTargetKind.RegistryStartup => VerifyRegistry(descriptor),
                PauseTargetKind.StartupFolder => VerifyStartupFolder(descriptor),
                PauseTargetKind.ScheduledTask => VerifyTask(descriptor),
                _ => new RestoreResult { Outcome = RestoreOutcome.Failed, Message = "Неизвестный тип компонента." }
            };
        }
        catch (Exception ex)
        {
            return new RestoreResult { Outcome = RestoreOutcome.Failed, Message = "Не удалось проверить компонент: " + ex.Message };
        }
    }

    private static RestoreResult RestoreRegistry(PauseDescriptor descriptor)
    {
        var command = RegistryReader.GetString(descriptor.RegistryPath!, descriptor.ValueName!, ViewFor(descriptor));
        if (command is null)
        {
            return new RestoreResult { Outcome = RestoreOutcome.Missing, Message = "Элемент автозагрузки больше не существует." };
        }

        if (!MatchesSignature(command, true, null, descriptor.OriginalSignature))
        {
            return new RestoreResult
            {
                Outcome = RestoreOutcome.Conflict,
                Message = "Команда запуска изменилась после отключения. Чтобы не перезаписать чужие изменения, SunTrace не будет восстанавливать этот элемент автоматически."
            };
        }

        return RestoreApproveValue(descriptor, descriptor.RegistryPath!, descriptor.ValueName!, command);
    }

    private static RestoreResult RestoreStartupFolder(PauseDescriptor descriptor)
    {
        if (!File.Exists(descriptor.FilePath))
        {
            return new RestoreResult { Outcome = RestoreOutcome.Missing, Message = "Файл автозагрузки больше не существует." };
        }

        if (!MatchesSignature(descriptor.FilePath, true, null, descriptor.OriginalSignature))
        {
            return new RestoreResult { Outcome = RestoreOutcome.Conflict, Message = "Файл автозагрузки изменился после отключения." };
        }

        return RestoreApproveValue(descriptor, descriptor.FilePath!, descriptor.ValueName!, descriptor.FilePath);
    }

    private static RestoreResult RestoreApproveValue(PauseDescriptor descriptor, string targetPath, string valueName, string signatureSource)
    {
        var current = RegistryReader.GetBinary(descriptor.ApproveRegistryPath!, valueName);
        var original = DecodeOriginal(descriptor);

        if (!descriptor.OriginalApproveValueExisted)
        {
            if (current is null || !IsDisabled(current))
            {
                return new RestoreResult { Outcome = RestoreOutcome.Restored, Message = "Элемент уже включён." };
            }

            if (!RegistryReader.DeleteValue(descriptor.ApproveRegistryPath!, valueName))
            {
                return new RestoreResult { Outcome = RestoreOutcome.Failed, Message = "Не удалось снять отметку об отключении." };
            }

            var afterDelete = RegistryReader.GetBinary(descriptor.ApproveRegistryPath!, valueName);
            if (afterDelete is not null && IsDisabled(afterDelete))
            {
                return new RestoreResult { Outcome = RestoreOutcome.Failed, Message = "Windows не подтвердила восстановление элемента." };
            }

            return new RestoreResult { Outcome = RestoreOutcome.Restored, Message = "Элемент автозагрузки восстановлен." };
        }

        if (current is not null && original is not null && current.SequenceEqual(original))
        {
            return new RestoreResult { Outcome = RestoreOutcome.Restored, Message = "Элемент уже находится в исходном состоянии." };
        }

        if (!RegistryReader.WriteBinary(descriptor.ApproveRegistryPath!, valueName, original ?? CreateEnabledValue()))
        {
            return new RestoreResult { Outcome = RestoreOutcome.Failed, Message = "Не удалось записать исходное состояние." };
        }

        var afterWrite = RegistryReader.GetBinary(descriptor.ApproveRegistryPath!, valueName);
        if (afterWrite is null || original is null || !afterWrite.SequenceEqual(original))
        {
            return new RestoreResult { Outcome = RestoreOutcome.Failed, Message = "Windows не подтвердила восстановление исходного состояния." };
        }

        return new RestoreResult { Outcome = RestoreOutcome.Restored, Message = "Элемент автозагрузки восстановлен в исходное состояние." };
    }

    private static RestoreResult RestoreTask(PauseDescriptor descriptor)
    {
        if (!Monitoring.ScheduledTaskScanner.TryReadTask(descriptor.TaskPath!, out var task, out var error) || task is null)
        {
            return new RestoreResult { Outcome = RestoreOutcome.Missing, Message = error ?? "Задание планировщика не найдено." };
        }

        if (!MatchesSignature(task.Detail, true, task.Details.GetValueOrDefault("Триггеров"), descriptor.OriginalSignature))
        {
            return new RestoreResult
            {
                Outcome = RestoreOutcome.Conflict,
                Message = "Параметры задания изменились после отключения. SunTrace не будет перезаписывать чужие изменения."
            };
        }

        if (task.Enabled)
        {
            return new RestoreResult { Outcome = RestoreOutcome.Restored, Message = "Задание уже включено." };
        }

        if (!Monitoring.ScheduledTaskScanner.TrySetEnabled(descriptor.TaskPath!, true, out error))
        {
            return new RestoreResult
            {
                Outcome = RestoreOutcome.RequiresElevation,
                Message = error ?? "Не удалось включить задание. Возможно, нужны права администратора."
            };
        }

        if (!Monitoring.ScheduledTaskScanner.TryReadTask(descriptor.TaskPath!, out var after, out _) || after is null || !after.Enabled)
        {
            return new RestoreResult { Outcome = RestoreOutcome.Failed, Message = "Планировщик не подтвердил включение задания." };
        }

        return new RestoreResult { Outcome = RestoreOutcome.Restored, Message = "Задание планировщика восстановлено." };
    }

    private static RestoreResult VerifyRegistry(PauseDescriptor descriptor)
    {
        var command = RegistryReader.GetString(descriptor.RegistryPath!, descriptor.ValueName!, ViewFor(descriptor));
        if (command is null)
        {
            return new RestoreResult { Outcome = RestoreOutcome.Missing, Message = "Элемент автозагрузки больше не существует." };
        }

        if (!MatchesSignature(command, true, null, descriptor.OriginalSignature))
        {
            return new RestoreResult { Outcome = RestoreOutcome.Conflict, Message = "Команда запуска изменилась после отключения." };
        }

        var approve = RegistryReader.GetBinary(descriptor.ApproveRegistryPath!, descriptor.ValueName!);
        return IsDisabled(approve)
            ? new RestoreResult { Outcome = RestoreOutcome.Restored, Message = "Элемент по-прежнему отключён. Восстановление возможно." }
            : new RestoreResult { Outcome = RestoreOutcome.Conflict, Message = "Элемент уже включён другой программой или пользователем." };
    }

    private static RestoreResult VerifyStartupFolder(PauseDescriptor descriptor)
    {
        if (!File.Exists(descriptor.FilePath))
        {
            return new RestoreResult { Outcome = RestoreOutcome.Missing, Message = "Файл автозагрузки больше не существует." };
        }

        var approve = RegistryReader.GetBinary(descriptor.ApproveRegistryPath!, descriptor.ValueName!);
        return IsDisabled(approve)
            ? new RestoreResult { Outcome = RestoreOutcome.Restored, Message = "Компонент по-прежнему отключён." }
            : new RestoreResult { Outcome = RestoreOutcome.Conflict, Message = "Компонент уже включён." };
    }

    private static RestoreResult VerifyTask(PauseDescriptor descriptor)
    {
        if (!Monitoring.ScheduledTaskScanner.TryReadTask(descriptor.TaskPath!, out var task, out var error) || task is null)
        {
            return new RestoreResult { Outcome = RestoreOutcome.Missing, Message = error ?? "Задание не найдено." };
        }

        if (!MatchesSignature(task.Detail, true, task.Details.GetValueOrDefault("Триггеров"), descriptor.OriginalSignature))
        {
            return new RestoreResult { Outcome = RestoreOutcome.Conflict, Message = "Параметры задания изменились." };
        }

        return task.Enabled
            ? new RestoreResult { Outcome = RestoreOutcome.Conflict, Message = "Задание уже включено." }
            : new RestoreResult { Outcome = RestoreOutcome.Restored, Message = "Задание по-прежнему отключено." };
    }
}
