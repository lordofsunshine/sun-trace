using SunTrace.Core.Models;

namespace SunTrace.App.Services;

public enum RestoreConflictChoice
{
    Cancel,
    Retry,
    KeepCurrent
}

public interface IDialogService
{
    bool Confirm(string title, string message, string? confirmText = null, string? cancelText = null, bool danger = false, string? detail = null);

    void Inform(string title, string message, string? detail = null);

    void InformWithAction(string title, string message, string detail, string actionText, Action action);

    RestoreConflictChoice ResolveRestoreConflict(PausedComponent item, string message);

    string? PickFile(string title, string filter);

    void ShowTracker();
}
