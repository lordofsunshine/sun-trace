using SunTrace.App.Localization;

namespace SunTrace.App.Services;

public sealed class DialogOptions
{
    public string Title { get; init; } = "SunTrace";

    public string Message { get; init; } = string.Empty;

    public string? Detail { get; init; }

    public string PrimaryText { get; init; } = LocalizedStrings.Instance["Ui.Ok"];

    public string? CancelText { get; init; }

    public string? ActionText { get; init; }

    public Action? Action { get; init; }

    public bool Danger { get; init; }
}

public sealed class ConflictDialogModel
{
    public string Title { get; init; } = LocalizedStrings.Instance["Ui.RestoreConflict"];

    public string Message { get; init; } = string.Empty;

    public string ItemName { get; init; } = string.Empty;

    public string? Detail { get; init; }
}
