namespace Unpoly.Blazor.Shadcn;

/// <summary>
/// shadcn exports <c>navigationMenuTriggerStyle</c> so that a top-level link — one with no panel
/// under it — can wear what the triggers beside it wear. Without it, half a menu bar is a styled
/// button and the other half is bare text.
/// </summary>
/// <remarks>
/// <c>text-sm</c> and <c>h-9</c> read <c>--control-text</c> / <c>--control-h</c>, as in
/// <see cref="ButtonVariants"/> and for the same reason: hardcoding them pins every consumer to
/// one control size.
/// </remarks>
public static class NavigationMenuTriggerStyle
{
    /// <summary>The recipe, plus any extra classes the caller wants after it.</summary>
    public const string Value =
        "group inline-flex h-control w-max items-center justify-center rounded-md " +
        "bg-background px-4 py-2 text-control font-medium transition-[color,box-shadow] " +
        "outline-none hover:bg-accent hover:text-accent-foreground focus:bg-accent " +
        "focus:text-accent-foreground focus-visible:ring-[3px] focus-visible:ring-ring/50 " +
        "focus-visible:outline-1 disabled:pointer-events-none disabled:opacity-50 " +
        "data-[state=open]:bg-accent/50 data-[state=open]:text-accent-foreground " +
        "data-[state=open]:hover:bg-accent data-[state=open]:focus:bg-accent";

    /// <summary>The recipe merged with a caller's classes, so theirs replace rather than compete.</summary>
    public static string Of(string? extra = null) => ClassMerge.Of(Value, extra);
}
