namespace QuickShare.Features.Workspaces.Folders;

/// <summary>
/// Resolves the settings an upload into a folder starts from (issue #245).
///
/// Two rules, and the difference between them is the whole feature:
///
/// * **Unlocked defaults are a prefill.** They reach the upload form, the sender sees them and
///   can change any of them. The server does not second-guess an explicit choice — a "default"
///   that silently overrides what the sender typed is not a default, it is a policy wearing a
///   friendlier word, and the sender would have no way to tell which one they got.
/// * **Locked defaults are policy.** A workspace admin flips <c>DefaultsLocked</c> and the
///   commit path enforces them: the expiry is replaced, privacy mode is forced on, and a commit
///   with no password is refused when one is required. Refused, not silently generated — the
///   server cannot invent a password and then have nobody to tell it to.
///
/// Inheritance walks child → parent and takes the first folder that expresses an opinion, per
/// setting. A child that says nothing inherits; a child that says something overrides. The lock
/// is the exception: it travels *down* from any ancestor, because a compliance rule a parent
/// sets must not be escapable by uploading one level deeper.
/// </summary>
public static class FolderDefaults
{
    /// <summary>
    /// Folds a chain ordered nearest-first (the target folder, then its parent) into one set
    /// of effective settings. An empty chain means "no folder opinion at all".
    /// </summary>
    public static WorkspaceFolder.EffectiveDefaults Resolve(IReadOnlyList<WorkspaceFolder> chain)
    {
        double? expiryHours = null;
        var passwordRequired = false;
        string? passwordHint = null;
        var privacyMode = false;
        var locked = false;

        foreach (var folder in chain)
        {
            expiryHours ??= folder.DefaultExpiryHours;

            // Booleans have no "unset", so nearest-wins cannot be expressed by null-coalescing:
            // an ancestor that turns a protection on is honoured even when the child is silent.
            // These settings only ever tighten, which is the safe direction to be wrong in.
            passwordRequired |= folder.DefaultPasswordRequired;
            privacyMode |= folder.DefaultPrivacyMode;
            locked |= folder.DefaultsLocked;

            if (passwordHint is null && !string.IsNullOrWhiteSpace(folder.DefaultPasswordHint))
                passwordHint = folder.DefaultPasswordHint;
        }

        return new WorkspaceFolder.EffectiveDefaults(
            expiryHours, passwordRequired, passwordHint, privacyMode, locked);
    }

    /// <summary>
    /// Loads the folder and its parent (depth is capped at two, so this is at most two rows)
    /// and folds them. Returns null when the folder does not exist in that workspace — callers
    /// treat that as a refusal, never as "no defaults".
    /// </summary>
    public static async Task<WorkspaceFolder.EffectiveDefaults?> ResolveAsync(
        AppDbContext db, Guid workspaceId, Guid folderId, CancellationToken ct)
    {
        var folder = await db.WorkspaceFolders
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == folderId && f.WorkspaceId == workspaceId, ct);

        if (folder is null)
            return null;

        var chain = new List<WorkspaceFolder> { folder };

        if (folder.ParentId is { } parentId)
        {
            var parent = await db.WorkspaceFolders
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == parentId && f.WorkspaceId == workspaceId, ct);

            if (parent is not null)
                chain.Add(parent);
        }

        return Resolve(chain);
    }
}
