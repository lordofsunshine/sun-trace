using SunTrace.Core.Enums;
using SunTrace.Core.Models;
using SunTrace.Core.Detection;

namespace SunTrace.Infrastructure.Monitoring;

public sealed partial class ScheduledTaskScanner
{
    private static ComponentSnapshot? CreateComponent(dynamic task, string folderPath)
    {
        var name = SafeString(() => (string)task.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var path = SafeString(() => (string)task.Path) ?? CombineTaskPath(folderPath, name);
        var enabled = SafeBool(() => (bool)task.Enabled);
        var state = SafeInt(() => (int)task.State);
        var author = SafeString(() => (string?)task.Author);
        var description = SafeString(() => (string?)task.Description);
        var actions = SafeString(() => DescribeActions(task)) ?? "Действия не описаны";
        var triggers = SafeInt(() => (int)task.Definition.Triggers.Count);
        var isMicrosoft = path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)
                          || (author?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ?? false);
        var isSecurity = path.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase)
                         || path.Contains("Security", StringComparison.OrdinalIgnoreCase)
                         || name.Contains("Defender", StringComparison.OrdinalIgnoreCase);
        var taskFolder = path.Contains('\\') ? path[..path.LastIndexOf('\\')] : "\\";
        if (string.IsNullOrEmpty(taskFolder))
        {
            taskFolder = "\\";
        }

        var supportsPause = enabled && !isMicrosoft && !isSecurity && !path.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase);

        return new ComponentSnapshot
        {
            Id = "task:" + path.ToLowerInvariant(),
            Type = ComponentType.ScheduledTask,
            Name = name,
            Detail = actions,
            ExecutablePath = PathHeuristics.ExtractExecutablePath(actions),
            Enabled = enabled,
            State = state switch
            {
                1 => "Отключено",
                2 => "В очереди",
                3 => "Готово",
                4 => "Выполняется",
                _ => "Неизвестно"
            },
            Owner = isMicrosoft ? "Microsoft" : string.IsNullOrWhiteSpace(author) ? "Издатель не указан" : author,
            Location = taskFolder,
            IsMicrosoft = isMicrosoft,
            IsSecuritySensitive = isSecurity,
            SupportsPause = supportsPause,
            Details = new Dictionary<string, string>
            {
                ["Задание"] = name,
                ["Папка задания"] = taskFolder,
                ["Триггеров"] = triggers.ToString(),
                ["Автор"] = author ?? "не указан",
                ["Описание"] = description ?? "-"
            },
            Pause = new PauseDescriptor
            {
                Kind = PauseTargetKind.ScheduledTask,
                TaskPath = path,
                TaskName = name,
                TaskFolder = taskFolder,
                OriginalEnabled = enabled
            }
        };
    }

    private static string? DescribeActions(dynamic task)
    {
        try
        {
            dynamic actions = task.Definition.Actions;
            var count = (int)actions.Count;
            var parts = new List<string>();
            for (var i = 1; i <= Math.Min(count, 5); i++)
            {
                dynamic action = actions.Item(i);
                var actionType = (int)action.Type;
                if (actionType == 0)
                {
                    var path = (string?)action.Path;
                    var arguments = (string?)action.Arguments;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        parts.Add(string.IsNullOrWhiteSpace(arguments) ? path : $"{path} {arguments}");
                    }
                }
                else if (actionType == 5)
                {
                    parts.Add("Обработчик COM");
                }
                else
                {
                    parts.Add($"Действие типа {actionType}");
                }
            }

            return parts.Count == 0 ? null : string.Join("; ", parts);
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeString(Func<string?> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return null;
        }
    }

    private static bool SafeBool(Func<bool> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return false;
        }
    }

    private static int SafeInt(Func<int> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return 0;
        }
    }
}
