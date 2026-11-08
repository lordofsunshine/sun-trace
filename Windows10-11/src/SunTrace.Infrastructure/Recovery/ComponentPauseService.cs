using Microsoft.Extensions.Logging;
using SunTrace.Core.Detection;
using SunTrace.Core.Enums;
using SunTrace.Core.Interfaces;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Recovery;

public sealed class ComponentPauseService : IComponentPauseService
{
    private readonly IHistoryStore _store;
    private readonly ElevationHost _elevation;
    private readonly ILogger<ComponentPauseService> _logger;

    public ComponentPauseService(IHistoryStore store, ElevationHost elevation, ILogger<ComponentPauseService> logger)
    {
        _store = store;
        _elevation = elevation;
        _logger = logger;
    }

    public async Task<PauseResult> PauseAsync(
        ComponentSnapshot component,
        string? appId,
        string? appName,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (component.Pause is null || !component.SupportsPause || component.IsMicrosoft || component.IsSecuritySensitive)
        {
            return new PauseResult { Message = "Этот компонент нельзя временно отключить." };
        }

        if (component.Type is not (ComponentType.Startup or ComponentType.ScheduledTask))
        {
            return new PauseResult { Message = "В первой версии SunTrace можно отключать только элементы автозагрузки и задания сторонних программ." };
        }

        var existing = await _store.GetPausedAsync(true, cancellationToken).ConfigureAwait(false);
        if (existing.Any(p => p.ComponentId == component.Id))
        {
            return new PauseResult { Message = "Этот компонент уже временно отключён через SunTrace." };
        }

        var prepared = PauseController.Prepare(component.Pause);
        if (!prepared.Success || prepared.Descriptor is null)
        {
            return new PauseResult
            {
                RequiresElevation = prepared.RequiresElevation,
                Message = prepared.Message
            };
        }

        PauseStartResult start;
        if (PauseController.RequiresElevation(prepared.Descriptor) && !Windows.RegistryReader.IsElevated)
        {
            var elevated = await _elevation.RunAsync(new ElevationRequest
            {
                Operation = ElevationOperation.Pause,
                Descriptor = component.Pause
            }, cancellationToken).ConfigureAwait(false);

            if (elevated is null || elevated.Cancelled)
            {
                return new PauseResult
                {
                    RequiresElevation = true,
                    Message = "Для отключения этого компонента нужны права администратора. Действие отменено."
                };
            }

            start = new PauseStartResult
            {
                Success = elevated.Success,
                Message = elevated.Message,
                Descriptor = prepared.Descriptor
            };
        }
        else
        {
            start = PauseController.Apply(prepared.Descriptor);
        }

        if (!start.Success || start.Descriptor is null)
        {
            return new PauseResult
            {
                RequiresElevation = start.RequiresElevation,
                Message = start.Message
            };
        }

        var item = new PausedComponent
        {
            ComponentId = component.Id,
            ComponentType = component.Type,
            AppId = appId,
            AppName = appName ?? "Связь не определена",
            DisplayName = component.Name,
            Detail = component.Detail,
            Descriptor = start.Descriptor,
            PausedAt = DateTimeOffset.Now,
            Reason = reason,
            Status = PauseStatus.Active
        };

        try
        {
            await _store.AddPausedAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сохранить сведения о Safe Pause. Изменение отменяется.");
            await RollbackAsync(start.Descriptor).ConfigureAwait(false);
            return new PauseResult { Message = "Не удалось сохранить сведения об отключении. Изменение отменено." };
        }

        try
        {
            await RecordEventAsync(item, paused: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось записать событие Safe Pause.");
        }

        _logger.LogInformation("Safe Pause: отключён компонент {Component} ({Id}).", item.DisplayName, item.ComponentId);
        return new PauseResult { Success = true, Message = start.Message, Paused = item };
    }

    public async Task<RestoreResult> RestoreAsync(PausedComponent item, CancellationToken cancellationToken = default)
    {
        var restored = await ExecuteRestoreAsync(item.Descriptor, cancellationToken).ConfigureAwait(false);
        var result = new RestoreResult { Outcome = restored.Outcome, Message = restored.Message, Item = item };
        await ApplyRestoreOutcomeAsync(item, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public Task<RestoreResult> VerifyAsync(PausedComponent item, CancellationToken cancellationToken = default)
    {
        var check = PauseController.Verify(item.Descriptor);
        return Task.FromResult(new RestoreResult { Outcome = check.Outcome, Message = check.Message, Item = item });
    }

    public async Task<RestoreResult> KeepCurrentStateAsync(PausedComponent item, CancellationToken cancellationToken = default)
    {
        item.Status = PauseStatus.KeptCurrent;
        item.LastError = null;
        await _store.UpdatePausedAsync(item, cancellationToken).ConfigureAwait(false);
        return new RestoreResult
        {
            Outcome = RestoreOutcome.Conflict,
            Message = "SunTrace больше не будет пытаться восстановить этот компонент.",
            Item = item
        };
    }

    public async Task ForgetAsync(PausedComponent item, CancellationToken cancellationToken = default)
    {
        await _store.RemovePausedAsync(item.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RestoreResult> ExecuteRestoreAsync(PauseDescriptor descriptor, CancellationToken cancellationToken)
    {
        if (!PauseController.RequiresElevation(descriptor) || Windows.RegistryReader.IsElevated)
        {
            return PauseController.Restore(descriptor);
        }

        var elevated = await _elevation.RunAsync(new ElevationRequest
        {
            Operation = ElevationOperation.Restore,
            Descriptor = descriptor
        }, cancellationToken).ConfigureAwait(false);

        if (elevated is null || elevated.Cancelled)
        {
            return new RestoreResult
            {
                Outcome = RestoreOutcome.RequiresElevation,
                Message = "Для восстановления нужны права администратора. Действие отменено."
            };
        }

        return new RestoreResult { Outcome = elevated.Outcome, Message = elevated.Message };
    }

    private async Task RollbackAsync(PauseDescriptor descriptor)
    {
        try
        {
            var result = await ExecuteRestoreAsync(descriptor, CancellationToken.None).ConfigureAwait(false);
            if (result.Outcome != RestoreOutcome.Restored)
            {
                _logger.LogError("Не удалось откатить Safe Pause: {Message}", result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось откатить Safe Pause.");
        }
    }

    private async Task ApplyRestoreOutcomeAsync(PausedComponent item, RestoreResult result, CancellationToken cancellationToken)
    {
        switch (result.Outcome)
        {
            case RestoreOutcome.Restored:
                item.Status = PauseStatus.Restored;
                item.LastError = null;
                await _store.UpdatePausedAsync(item, cancellationToken).ConfigureAwait(false);
                await RecordEventAsync(item, paused: false, cancellationToken).ConfigureAwait(false);
                break;
            case RestoreOutcome.Missing:
                item.Status = PauseStatus.TargetMissing;
                item.LastError = result.Message;
                await _store.UpdatePausedAsync(item, cancellationToken).ConfigureAwait(false);
                break;
            case RestoreOutcome.Conflict:
                item.Status = PauseStatus.Conflict;
                item.LastError = result.Message;
                await _store.UpdatePausedAsync(item, cancellationToken).ConfigureAwait(false);
                break;
            case RestoreOutcome.Failed:
            case RestoreOutcome.RequiresElevation:
                item.Status = PauseStatus.Failed;
                item.LastError = result.Message;
                await _store.UpdatePausedAsync(item, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task RecordEventAsync(PausedComponent item, bool paused, CancellationToken cancellationToken)
    {
        var eventItem = PauseLifecycleEventFactory.Create(item, paused);
        await _store.AppendEventsAsync([eventItem], TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
    }
}
