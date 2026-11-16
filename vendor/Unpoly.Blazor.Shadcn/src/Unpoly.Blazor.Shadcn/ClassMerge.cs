using Microsoft.Extensions.Options;
using TailwindMerge;
using TailwindMerge.Models;

namespace Unpoly.Blazor.Shadcn;

/// <summary>
/// shadcn's <c>cn()</c>: joins class strings and lets the later one win when two of them set the
/// same CSS property.
/// </summary>
/// <remarks>
/// <para>
/// This is <a href="https://github.com/dcastil/tailwind-merge">tailwind-merge</a>, through its
/// .NET port. Without it "later wins" is not true — two classes of equal specificity are resolved
/// by stylesheet order, which is Tailwind's order and not the caller's, so
/// <c>Class="h-12"</c> against a variant's <c>h-control</c> loses about half the time and looks
/// like a caching bug when it does.
/// </para>
/// <para>
/// The three tokens this library adds are registered below. tailwind-merge keeps classes it does
/// not recognise, so an unregistered <c>h-control</c> would not conflict with <c>h-12</c> and
/// both would survive — the exact failure the merge is here to prevent, and a silent one.
/// </para>
/// <para>
/// One shared instance, because tailwind-merge caches parsed classes and a per-render instance
/// would throw that away every time. It is replaced wholesale by <see cref="Configure"/> rather
/// than mutated, so a reader is never racing a half-applied config.
/// </para>
/// </remarks>
public static class ClassMerge
{
    static TwMerge _merge = Build(null);

    /// <summary>
    /// Teaches the merge about an application's own utilities. Call once at startup, before
    /// anything renders.
    /// </summary>
    /// <example>
    /// <code>
    /// ClassMerge.Configure(config => config.Extend(new ExtendedConfig
    /// {
    ///     ClassGroups = new() { ["h"] = [new ClassGroup("h", "sidebar")] },
    /// }));
    /// </code>
    /// </example>
    public static void Configure(Action<TwMergeConfig> configure) => _merge = Build(configure);

    /// <summary>Joins the parts, drops the blanks, and resolves conflicts in favour of the last.</summary>
    public static string Of(params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

        // No short-circuit for a single part, however tempting. One string can hold a conflict of
        // its own — a recipe whose size sets `gap-1.5` over a base's `gap-2` arrives here already
        // joined — and skipping the merge there would make Of() return different results for the
        // same classes depending on how they happened to be split across arguments. tailwind-merge
        // caches parsed classes, so the second render costs a dictionary lookup.
        return kept.Length == 0 ? string.Empty : _merge.Merge(kept!) ?? string.Empty;
    }

    static TwMerge Build(Action<TwMergeConfig>? configure)
    {
        var config = TwMergeConfig.Default();

        // --control-h, --control-text and --radius-control are this library's, so tailwind-merge
        // has never heard of them. Each joins the group of the utility it stands in for, which is
        // what makes `Class="h-12"` beat the variant's `h-control`.
        config.Extend(new ExtendedConfig
        {
            ClassGroups = new()
            {
                ["h"] = new ClassGroup("h", new object[] { "control" }),
                ["size"] = new ClassGroup("size", new object[] { "control" }),
                ["font-size"] = new ClassGroup("text", new object[] { "control" }),
            },
        });

        configure?.Invoke(config);
        return new TwMerge(Options.Create(config));
    }
}
