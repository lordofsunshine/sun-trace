using Microsoft.Extensions.Logging.Abstractions;
using SunTrace.Core.Enums;
using SunTrace.Core.Interfaces;
using SunTrace.Core.Models;
using SunTrace.Infrastructure.Recovery;

namespace SunTrace.IntegrationTests;

internal sealed class InMemoryComponentPauseService : IComponentPauseService
{
    private readonly IHistoryStore _store;

    public InMemoryComponentPauseService(IHistoryStore store)
    {
        _store = store;
    }

    public async Task<PauseResult> PauseAsync(
        ComponentSnapshot component,
        string? appId,
        string? appName,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var active = await _store.GetPausedAsync(true, cancellationToken).ConfigureAwait(false);
        if (active.Any(item => item.ComponentId == component.Id))
        {
            return new PauseResult { Message = "Компонент уже временно отключён." };
        }

        if (component.Pause is null)
        {
            return new PauseResult { Message = "Компонент нельзя временно отключить." };
        }

        var item = new PausedComponent
        {
            ComponentId = component.Id,
            ComponentType = component.Type,
            AppId = appId,
            AppName = appName ?? "Связь не определена",
            DisplayName = component.Name,
            Detail = component.Detail,
            Descriptor = component.Pause,
            PausedAt = DateTimeOffset.Now,
            Reason = reason ?? "Проверка",
            Status = PauseStatus.Active
        };

        await _store.AddPausedAsync(item, cancellationToken).ConfigureAwait(false);
        await _store.AppendEventsAsync([PauseLifecycleEventFactory.Create(item, true)], TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        return new PauseResult
        {
            Success = true,
            Message = "Компонент временно отключён.",
            Paused = item
        };
    }

    public async Task<RestoreResult> RestoreAsync(PausedComponent item, CancellationToken cancellationToken = default)
    {
        item.Status = PauseStatus.Restored;
        item.LastError = null;
        await _store.UpdatePausedAsync(item, cancellationToken).ConfigureAwait(false);
        await _store.AppendEventsAsync([PauseLifecycleEventFactory.Create(item, false)], TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        return new RestoreResult
        {
            Outcome = RestoreOutcome.Restored,
            Message = "Компонент восстановлен.",
            Item = item
        };
    }

    public Task<RestoreResult> VerifyAsync(PausedComponent item, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RestoreResult
        {
            Outcome = RestoreOutcome.Restored,
            Message = "Проверка завершена.",
            Item = item
        });

    public async Task<RestoreResult> KeepCurrentStateAsync(PausedComponent item, CancellationToken cancellationToken = default)
    {
        item.Status = PauseStatus.KeptCurrent;
        await _store.UpdatePausedAsync(item, cancellationToken).ConfigureAwait(false);
        return new RestoreResult
        {
            Outcome = RestoreOutcome.Conflict,
            Message = "Текущее состояние сохранено.",
            Item = item
        };
    }

    public Task ForgetAsync(PausedComponent item, CancellationToken cancellationToken = default) =>
        _store.RemovePausedAsync(item.Id, cancellationToken);
}
