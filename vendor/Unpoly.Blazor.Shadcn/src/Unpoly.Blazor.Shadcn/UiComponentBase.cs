using Microsoft.AspNetCore.Components;

namespace Unpoly.Blazor.Shadcn;

/// <summary>
/// What every shadcn component shares: a <c>data-slot</c> root, a class string the caller can
/// extend, and pass-through for everything else — which is how <c>up-target</c>, <c>up-poll</c>
/// and the rest of Unpoly reach the DOM without a single component knowing Unpoly exists.
/// </summary>
/// <remarks>
/// <para>
/// <c>Class</c> wins over the component's own recipe, because <see cref="ClassMerge"/> is
/// tailwind-merge: <c>Class="h-12"</c> removes the variant's <c>h-control</c> rather than sitting
/// next to it and losing on stylesheet order. Layout classes the variant does not set — margins,
/// grid placement, width — are simply appended, as before.
/// </para>
/// </remarks>
public abstract class UiComponentBase : ComponentBase
{
    /// <summary>Extra classes, appended after the component's own — see the remarks on merging.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>What the component wraps.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Everything else, splatted onto the root element. This is how <c>up-target</c>,
    /// <c>up-poll</c>, <c>up-validate</c> and the rest of Unpoly reach the DOM without a single
    /// component having to know Unpoly exists.
    /// </summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>shadcn's <c>cn()</c>. See <see cref="ClassMerge"/>.</summary>
    protected static string Cn(params string?[] parts) => ClassMerge.Of(parts);
}
