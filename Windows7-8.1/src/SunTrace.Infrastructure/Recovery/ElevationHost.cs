using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;
using SunTrace.Infrastructure.Windows;

namespace SunTrace.Infrastructure.Recovery;

public enum ElevationOperation
{
    Pause = 0,
    Restore = 1
}

public sealed class ElevationRequest
{
    public ElevationOperation Operation { get; set; }

    public PauseDescriptor Descriptor { get; set; } = new();
}

public sealed class ElevationResult
{
    public bool Success { get; set; }

    public bool Cancelled { get; set; }

    public string Message { get; set; } = string.Empty;

    public RestoreOutcome Outcome { get; set; }
}

public sealed class ElevationHost
{
    private const string Switch = "--elevated-op";
    private const int MaxPayloadLength = 16000;
    private const int ExitSuccess = 0;
    private const int ExitFailed = 2;
    private const int ExitInvalid = 3;
    private const int ExitNotElevated = 4;
    private const int ExitRestoreBase = 10;

    private readonly ILogger<ElevationHost> _logger;

    public ElevationHost(ILogger<ElevationHost> logger)
    {
        _logger = logger;
    }

    public async Task<ElevationResult?> RunAsync(ElevationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var host = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(host))
            {
                return new ElevationResult { Message = "Не удалось определить путь к программе." };
            }

            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)));
            if (payload.Length > MaxPayloadLength)
            {
                return new ElevationResult { Message = "Запрос слишком большой." };
            }

            var arguments = string.Empty;
            if (Path.GetFileName(host).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                var assembly = Environment.GetCommandLineArgs().FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(assembly) && assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    arguments = $"\"{assembly}\" ";
                }
            }

            arguments += $"{Switch} \"{payload}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = host,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new ElevationResult { Message = "Не удалось запустить запрос прав администратора." };
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return FromExitCode(request, process.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _logger.LogInformation("Запрос прав администратора отменён пользователем.");
            return new ElevationResult { Cancelled = true, Message = "Запрос прав администратора отменён." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка выполнения операции с повышением прав.");
            return new ElevationResult { Message = "Ошибка повышения прав: " + ex.Message };
        }
    }

    public static bool IsElevationRequest(string[] args, out string? payload)
    {
        payload = null;
        var index = Array.IndexOf(args, Switch);
        if (index < 0 || args.Length < index + 2)
        {
            return false;
        }

        payload = args[index + 1];
        return true;
    }

    public static int ExecuteFromCommandLine(string payload)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaxPayloadLength)
            {
                return ExitInvalid;
            }

            if (!RegistryReader.IsElevated)
            {
                return ExitNotElevated;
            }

            var request = JsonSerializer.Deserialize<ElevationRequest>(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            if (request?.Descriptor is null || !Enum.IsDefined(request.Operation))
            {
                return ExitInvalid;
            }

            if (!PausePolicy.TryValidate(request.Descriptor, out _))
            {
                return ExitInvalid;
            }

            if (request.Operation == ElevationOperation.Pause)
            {
                return PauseController.Pause(request.Descriptor).Success ? ExitSuccess : ExitFailed;
            }

            return ExitRestoreBase + (int)PauseController.Restore(request.Descriptor).Outcome;
        }
        catch (Exception)
        {
            return ExitFailed;
        }
    }

    private static ElevationResult FromExitCode(ElevationRequest request, int exitCode)
    {
        if (request.Operation == ElevationOperation.Pause)
        {
            if (exitCode == ExitSuccess)
            {
                return new ElevationResult
                {
                    Success = true,
                    Message = request.Descriptor.Kind == PauseTargetKind.ScheduledTask
                        ? "Задание планировщика временно отключено."
                        : "Элемент автозагрузки временно отключён."
                };
            }

            return new ElevationResult { Message = ExitMessage(exitCode, "Не удалось отключить компонент с правами администратора.") };
        }

        var outcome = exitCode - ExitRestoreBase;
        if (outcome is >= (int)RestoreOutcome.Restored and <= (int)RestoreOutcome.RequiresElevation)
        {
            var value = (RestoreOutcome)outcome;
            return new ElevationResult
            {
                Success = value == RestoreOutcome.Restored,
                Outcome = value,
                Message = value switch
                {
                    RestoreOutcome.Restored => "Компонент восстановлен.",
                    RestoreOutcome.Conflict => "Состояние компонента изменилось после отключения. Автоматическое восстановление не выполнено.",
                    RestoreOutcome.Missing => "Компонент больше не существует.",
                    RestoreOutcome.RequiresElevation => "Для восстановления нужны права администратора.",
                    _ => "Не удалось восстановить компонент."
                }
            };
        }

        return new ElevationResult
        {
            Outcome = RestoreOutcome.Failed,
            Message = ExitMessage(exitCode, "Не удалось восстановить компонент с правами администратора.")
        };
    }

    private static string ExitMessage(int exitCode, string fallback) => exitCode switch
    {
        ExitInvalid => "Запрос отклонён: параметры компонента недопустимы.",
        ExitNotElevated => "Операция не получила права администратора.",
        _ => fallback
    };
}
