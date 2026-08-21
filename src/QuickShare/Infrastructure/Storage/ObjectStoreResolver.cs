namespace QuickShare.Infrastructure.Storage;

/// <summary>
/// Resolves which configuration section describes the object store (issue #171).
///
/// The choice is made once at startup — the current <c>ObjectStore</c> section when it is
/// configured, otherwise the legacy <c>R2</c> section — because <c>Configure&lt;T&gt;(section)</c>
/// can only bind one of them, and leaving the provider to guess from two half-populated option
/// objects is how an upgrade silently points at the wrong bucket.
/// </summary>
internal static class ObjectStoreResolver
{
    public static ObjectStoreOptions Resolve(IConfiguration configuration)
    {
        var current = configuration.GetSection(ObjectStoreOptions.Section).Get<ObjectStoreOptions>() ?? new();
        if (current.IsConfigured)
            return current;

        // Connection details fall back to the legacy section, but the **region table does not**
        // (issue #119). Regions are a separate concern from which section holds the credentials:
        // a deployment still configured under `R2` that adds `ObjectStore:Regions` expects
        // residency to work, and dropping the table here would silently disable it with no error
        // anywhere — the same class of silent failure the fallback exists to prevent.
        var legacy = configuration.GetSection(R2Options.Section).Get<R2Options>() ?? new();
        var resolved = ObjectStoreOptions.FromR2(legacy);
        resolved.Regions.AddRange(current.Regions);

        return resolved;
    }
}
