using System.Runtime.InteropServices;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Monitoring;

public sealed partial class ScheduledTaskScanner
{
    public static bool TryReadTask(string taskPath, out ComponentSnapshot? task, out string? error)
    {
        task = null;
        error = null;
        object? service = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null)
            {
                error = "Планировщик заданий Windows недоступен.";
                return false;
            }

            service = Activator.CreateInstance(type);
            dynamic scheduler = service!;
            scheduler.Connect();
            var folderPath = taskPath.Contains('\\') ? taskPath[..taskPath.LastIndexOf('\\')] : "\\";
            if (string.IsNullOrEmpty(folderPath))
            {
                folderPath = "\\";
            }

            var taskName = taskPath[(taskPath.LastIndexOf('\\') + 1)..];
            dynamic folder = scheduler.GetFolder(folderPath);
            dynamic registered = folder.GetTask(taskName);
            try
            {
                task = CreateComponent(registered, folderPath);
            }
            finally
            {
                ReleaseCom((object?)registered);
                ReleaseCom((object?)folder);
            }

            if (task is null)
            {
                error = "Задание планировщика не найдено.";
                return false;
            }

            return true;
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x80070002)
        {
            error = "Задание планировщика не найдено.";
            return false;
        }
        catch (Exception ex)
        {
            error = "Не удалось прочитать задание: " + ex.Message;
            return false;
        }
        finally
        {
            if (service is not null && Marshal.IsComObject(service))
            {
                Marshal.ReleaseComObject(service);
            }
        }
    }

    public static bool TrySetEnabled(string taskPath, bool enabled, out string? error)
    {
        error = null;
        object? service = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null)
            {
                error = "Планировщик заданий Windows недоступен.";
                return false;
            }

            service = Activator.CreateInstance(type);
            dynamic scheduler = service!;
            scheduler.Connect();
            var folderPath = taskPath.Contains('\\') ? taskPath[..taskPath.LastIndexOf('\\')] : "\\";
            if (string.IsNullOrEmpty(folderPath))
            {
                folderPath = "\\";
            }

            var taskName = taskPath[(taskPath.LastIndexOf('\\') + 1)..];
            dynamic folder = scheduler.GetFolder(folderPath);
            dynamic task = folder.GetTask(taskName);
            bool actual;
            try
            {
                task.Enabled = enabled;
                actual = (bool)task.Enabled;
            }
            finally
            {
                ReleaseCom((object?)task);
                ReleaseCom((object?)folder);
            }

            if (actual != enabled)
            {
                error = "Планировщик не подтвердил изменение состояния задания.";
                return false;
            }

            return true;
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x80070005)
        {
            error = "Для изменения этого задания нужны права администратора.";
            return false;
        }
        catch (Exception ex)
        {
            error = "Не удалось изменить задание: " + ex.Message;
            return false;
        }
        finally
        {
            if (service is not null && Marshal.IsComObject(service))
            {
                Marshal.ReleaseComObject(service);
            }
        }
    }
}
