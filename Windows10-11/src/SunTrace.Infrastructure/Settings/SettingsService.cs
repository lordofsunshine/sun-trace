using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SunTrace.Core.Interfaces;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Settings;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsService(DataPaths paths, ILogger<SettingsService> logger)
    {
        SettingsPath = paths.SettingsPath;
        _logger = logger;
        Current = new AppSettings();
    }

    public AppSettings Current { get; private set; }

    public string SettingsPath { get; }

    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(SettingsPath))
            {
                Current = new AppSettings().Normalize();
                return;
            }

            await using var stream = File.OpenRead(SettingsPath);
            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            Current = (loaded ?? new AppSettings()).Normalize();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось прочитать настройки. Используются значения по умолчанию.");
            Current = new AppSettings().Normalize();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalized = settings.Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temporary = SettingsPath + ".tmp";
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, SettingsPath, overwrite: true);
            Current = normalized;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сохранить настройки.");
            throw;
        }
        finally
        {
            _gate.Release();
        }

        SettingsChanged?.Invoke(this, Current);
    }
}
