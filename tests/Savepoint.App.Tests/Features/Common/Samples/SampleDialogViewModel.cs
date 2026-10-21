using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;

namespace Savepoint.App.Tests.Features.Common.Samples;

public sealed partial class SampleDialogViewModel(string title = "Sample dialog") : DialogViewModel<string>(title)
{
    public bool Dismissible { get; set; } = true;

    public override bool CanDismiss => Dismissible;

    protected override string DismissResult => "dismissed";

    [RelayCommand]
    private void AnswerFirst() => Close("first");

    [RelayCommand]
    private void AnswerSecond() => Close("second");
}
