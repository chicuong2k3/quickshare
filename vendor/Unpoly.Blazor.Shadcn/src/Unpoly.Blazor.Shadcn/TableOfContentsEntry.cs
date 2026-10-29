namespace Unpoly.Blazor.Shadcn;

/// <summary>One link in a <c>TableOfContents</c>.</summary>
/// <param name="Id">The id of the heading on the page; the link points at <c>#Id</c>.</param>
/// <param name="Text">The link text, usually the heading's own text.</param>
/// <param name="Level">Nesting depth, 1 for a top-level heading. Rendered as <c>data-level</c> on the item.</param>
public sealed record TableOfContentsEntry(string Id, string Text, int Level = 1);
