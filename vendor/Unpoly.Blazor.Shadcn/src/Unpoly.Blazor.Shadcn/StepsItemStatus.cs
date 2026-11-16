namespace Unpoly.Blazor.Shadcn;

/// <summary>Status of a StepsItem — mirrors Lumeo StepsItemStatus.</summary>
public enum StepsItemStatus
{
    /// <summary>
    /// Indicates that the step is in its default state, neither active nor completed.
    /// </summary>
    Default,
    /// <summary>
    /// Indicates that the step is currently active or in progress.
    /// </summary>
    Error
}
