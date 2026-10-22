using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Games;

/// <summary>
/// Copies a profile under a new name. The copy starts with the same game version and mods; its save history starts
/// fresh, so nothing about the original changes.
/// </summary>
public sealed class DuplicateProfileDialogViewModel : ProfileFormDialogViewModel
{
    private readonly Func<string, Task<OperationResult<Profile>>> _duplicate;

    /// <param name="sourceName">The name of the profile being copied.</param>
    /// <param name="duplicate">Makes the copy through the Api.</param>
    /// <param name="text">Formats the dialog's copy.</param>
    public DuplicateProfileDialogViewModel(string sourceName, Func<string, Task<OperationResult<Profile>>> duplicate, UiFormatter text)
        : base(text.Message("games.duplicateProfile.title", ("name", sourceName)), text.Message("games.duplicateProfile.defaultName", ("name", sourceName)), text)
    {
        _duplicate = duplicate;
        Body = text.Message("games.duplicateProfile.body", ("name", sourceName));
    }

    /// <summary>What the copy keeps and what it starts over with.</summary>
    public string Body { get; }

    /// <inheritdoc />
    public override string Description => Body;

    /// <inheritdoc />
    protected override async Task<ErrorInfo?> SubmitAsync()
    {
        var copied = await _duplicate(Name.Trim());
        return copied.Ok ? null : copied.Error;
    }
}
