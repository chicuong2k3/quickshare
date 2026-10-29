namespace Unpoly.Blazor.Shadcn;

/// <summary>
/// shadcn exports <c>buttonVariants</c> so that things which are not buttons — a pagination
/// link, an anchor inside a card — can wear the same clothes without nesting a Button. This is
/// that export.
/// </summary>
/// <remarks>
/// Three substitutions from the upstream recipe: <c>text-sm</c>, <c>h-9</c> and <c>size-9</c>
/// read <c>--control-text</c> / <c>--control-h</c>, because hardcoding them pins every consumer
/// to one control size. Unknown variant or size names fall back to the default, which is what
/// cva does with an unmatched key. <c>extra</c> goes through <see cref="ClassMerge"/>, so a
/// caller's class replaces the recipe's rather than competing with it.
/// </remarks>
public static class ButtonVariants
{
    /// <summary>Everything every button wears, whatever its variant or size.</summary>
    public const string Base =
        "inline-flex shrink-0 items-center justify-center gap-2 rounded-md text-control " +
        "font-medium whitespace-nowrap transition-all outline-none focus-visible:border-ring " +
        "focus-visible:ring-[3px] focus-visible:ring-ring/50 disabled:pointer-events-none " +
        "disabled:opacity-50 aria-invalid:border-destructive aria-invalid:ring-destructive/20 " +
        "dark:aria-invalid:ring-destructive/40 [&_svg]:pointer-events-none [&_svg]:shrink-0 " +
        "[&_svg:not([class*='size-'])]:size-4";

    /// <summary>default | destructive | outline | secondary | ghost | link.</summary>
    public static string ForVariant(string variant) => variant switch
    {
        "destructive" => "bg-destructive text-white hover:bg-destructive/90 focus-visible:ring-destructive/20 dark:bg-destructive/60 dark:focus-visible:ring-destructive/40",
        "ghost" => "hover:bg-accent hover:text-accent-foreground dark:hover:bg-accent/50",
        "link" => "text-primary underline-offset-4 hover:underline",
        "outline" => "border bg-background shadow-xs hover:bg-accent hover:text-accent-foreground dark:border-input dark:bg-input/30 dark:hover:bg-input/50",
        "secondary" => "bg-secondary text-secondary-foreground hover:bg-secondary/80",
        _ => "bg-primary text-primary-foreground hover:bg-primary/90",
    };

    /// <summary>default | sm | lg | icon.</summary>
    public static string ForSize(string size) => size switch
    {
        "icon" => "size-control",
        "icon-lg" => "size-10",
        "icon-sm" => "size-8",
        "icon-xs" => "size-6 rounded-md [&_svg:not([class*='size-'])]:size-3",
        "lg" => "h-10 rounded-md px-6 has-[>svg]:px-4",
        "sm" => "h-8 gap-1.5 rounded-md px-3 has-[>svg]:px-2.5",
        "xs" => "h-6 gap-1 rounded-md px-2 text-xs has-[>svg]:px-1.5 [&_svg:not([class*='size-'])]:size-3",
        _ => "h-control px-4 py-2 has-[>svg]:px-3",
    };

    /// <summary>The whole recipe, plus any extra classes the caller wants after it.</summary>
    public static string Of(string variant = "default", string size = "default", string? extra = null) =>
        ClassMerge.Of(Base, ForVariant(variant), ForSize(size), extra);
}
