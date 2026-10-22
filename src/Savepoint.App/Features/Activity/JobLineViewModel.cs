namespace Savepoint.App.Features.Activity;

/// <summary>One line of a job's details, ready to show.</summary>
public sealed class JobLineViewModel
{
    /// <param name="number">Where the line stands in everything the job has written, counting from the first.</param>
    /// <param name="text">What the line says, already localized.</param>
    /// <param name="isStep">True for a step in words, false for a file path.</param>
    public JobLineViewModel(long number, string text, bool isStep)
    {
        Number = number;
        Text = text;
        IsStep = isStep;
    }

    /// <summary>Where the line stands in everything the job has written, counting from the first.</summary>
    public long Number { get; }

    /// <summary>What the line says.</summary>
    public string Text { get; }

    /// <summary>True for a step in words, false for a file path.</summary>
    public bool IsStep { get; }
}
