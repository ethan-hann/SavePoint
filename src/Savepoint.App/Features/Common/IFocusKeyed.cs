namespace Savepoint.App.Features.Common;

/// <summary>An entry of a list that can be rebuilt while someone is using the keyboard on it, and so names itself across the rebuild.</summary>
public interface IFocusKeyed
{
    /// <summary>What names the thing the entry stands for across a rebuild; empty when the entry cannot be found again.</summary>
    string FocusKey { get; }
}
