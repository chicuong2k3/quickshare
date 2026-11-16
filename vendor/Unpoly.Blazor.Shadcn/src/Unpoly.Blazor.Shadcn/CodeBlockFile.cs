namespace Unpoly.Blazor.Shadcn;

/// <summary>A named source file in a multi-file CodeBlock.</summary>
public sealed record CodeBlockFile(string Filename, string Language, string Code);
