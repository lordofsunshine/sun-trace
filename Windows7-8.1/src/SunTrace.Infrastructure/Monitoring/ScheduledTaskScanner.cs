using System.Runtime.InteropServices;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Monitoring;

public sealed class TaskScanResult
{
    public List<ComponentSnapshot> Components { get; init; } = new();

    public List<string> Warnings { get; init; } = new();

    public bool IsComplete { get; init; }
}

public sealed partial class ScheduledTaskScanner
{
    private const int MaxDepth = 10;

    public TaskScanResult Scan(CancellationToken cancellationToken)
    {
        var components = new List<ComponentSnapshot>();
        var warnings = new List<string>();
        object? service = null;

        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null)
            {
                return new TaskScanResult
                {
                    IsComplete = false,
                    Warnings = ["Планировщик заданий Windows недоступен."]
                };
            }

            service = Activator.CreateInstance(type);
            if (service is null)
            {
                return new TaskScanResult
                {
                    IsComplete = false,
                    Warnings = ["Не удалось подключиться к планировщику заданий."]
                };
            }

            dynamic scheduler = service;
            scheduler.Connect();
            dynamic root = scheduler.GetFolder("\\");
            try
            {
                EnumerateFolder(root, components, warnings, cancellationToken, 0);
            }
            finally
            {
                ReleaseCom((object?)root);
            }
        }
        catch (COMException ex)
        {
            return new TaskScanResult
            {
                IsComplete = false,
                Warnings = [$"Планировщик заданий вернул ошибку: {ex.Message}"]
            };
        }
        catch (Exception ex)
        {
            return new TaskScanResult
            {
                IsComplete = false,
                Warnings = ["Не удалось прочитать задания планировщика: " + ex.Message]
            };
        }
        finally
        {
            if (service is not null && Marshal.IsComObject(service))
            {
                Marshal.ReleaseComObject(service);
            }
        }

        return new TaskScanResult
        {
            Components = components.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            Warnings = warnings,
            IsComplete = true
        };
    }

    private static void EnumerateFolder(dynamic folder, List<ComponentSnapshot> components, List<string> warnings, CancellationToken cancellationToken, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var folderPath = SafeString(() => (string)folder.Path) ?? "\\";

        try
        {
            dynamic tasks = folder.GetTasks(1);
            var count = SafeInt(() => (int)tasks.Count);
            for (var i = 1; i <= count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? taskObject = null;
                try
                {
                    dynamic task = tasks.Item(i);
                    taskObject = task;
                    var component = CreateComponent(task, folderPath);
                    if (component is not null)
                    {
                        components.Add(component);
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Не удалось прочитать задание: {ex.Message}");
                }
                finally
                {
                    ReleaseCom(taskObject);
                }
            }

            ReleaseCom((object?)tasks);
        }
        catch (Exception ex)
        {
            warnings.Add($"Папка заданий {folderPath} недоступна: {ex.Message}");
        }

        try
        {
            dynamic subFolders = folder.GetFolders(0);
            var count = SafeInt(() => (int)subFolders.Count);
            for (var i = 1; i <= count; i++)
            {
                object? childObject = null;
                try
                {
                    dynamic child = subFolders.Item(i);
                    childObject = child;
                    EnumerateFolder(child, components, warnings, cancellationToken, depth + 1);
                }
                catch (Exception ex)
                {
                    warnings.Add("Не удалось прочитать папку заданий: " + ex.Message);
                }
                finally
                {
                    ReleaseCom(childObject);
                }
            }

            ReleaseCom((object?)subFolders);
        }
        catch (Exception ex)
        {
            warnings.Add($"Вложенные папки заданий {folderPath} недоступны: {ex.Message}");
        }
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private static string CombineTaskPath(string folder, string name) =>
        folder.TrimEnd('\\') + "\\" + name;
}
