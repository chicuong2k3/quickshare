namespace QuickShare.Data.Entities;

/// <summary>
/// A shared folder inside a team workspace (issue #151). Content organization for #106:
/// the team drops files into "Campaign Assets" and every seat sees them, instead of each
/// member owning a private pile that happens to be in the same workspace.
///
/// Nesting stops at two levels (<see cref="MaxDepth"/>). A tree of arbitrary depth needs
/// breadcrumbs, move semantics, cycle checks and a recursive permission walk; two levels
/// covers "client / project" — the shape agencies actually asked for — with a parent chain
/// short enough to resolve in one pass.
/// </summary>
public record WorkspaceFolder : BaseAuditEntity
{
    public const int NameMaxLength = 80;
    public const int HintMaxLength = 120;
    /// <summary>Root = depth 1, one level of children = depth 2. No grandchildren.</summary>
    public const int MaxDepth = 2;
    /// <summary>Ceiling per workspace. A runaway template apply is the realistic way to hit it.</summary>
    public const int MaxPerWorkspace = 200;

    public Guid WorkspaceId { get; set; }
    /// <summary>Null = a root folder. A child may never itself have children (see <see cref="MaxDepth"/>).</summary>
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid CreatedById { get; set; }

    // ── Default settings (issue #245) ──
    // Null/false means "inherit from the parent, then from nothing". These are *defaults*,
    // not policy: the upload form prefills from them and the sender stays in control —
    // unless DefaultsLocked is set, which is the compliance escape hatch.

    /// <summary>Prefilled link lifetime in hours; null = no folder opinion.</summary>
    public double? DefaultExpiryHours { get; set; }
    /// <summary>Uploads into this folder should carry a password.</summary>
    public bool DefaultPasswordRequired { get; set; }
    /// <summary>Shown next to the password field so the team knows which password to use.</summary>
    public string? DefaultPasswordHint { get; set; }
    /// <summary>Prefill privacy mode (issue #187): no tracking, no analytics, no visible counts.</summary>
    public bool DefaultPrivacyMode { get; set; }
    /// <summary>
    /// Workspace admins turn the defaults from a prefill into a rule. Locked defaults are
    /// enforced server-side at commit; unlocked ones only reach the form.
    /// </summary>
    public bool DefaultsLocked { get; set; }

    public Workspace Workspace { get; init; } = null!;

    /// <summary>Resolved settings for uploads into this folder, after walking the parent chain.</summary>
    public sealed record EffectiveDefaults(
        double? ExpiryHours,
        bool PasswordRequired,
        string? PasswordHint,
        bool PrivacyMode,
        bool Locked);
}

/// <summary>
/// A reusable folder structure (issue #234). An agency onboarding a client creates the same
/// five folders with the same settings every time; a template turns thirty clicks into one.
///
/// The structure is stored as a JSON snapshot rather than as template-folder rows. A template
/// is a value, not a live tree: editing it must not reach into folders already created from
/// it, and deleting it must not touch them either. A JSON blob makes that impossible to get
/// wrong; foreign keys to real folders would make it easy.
/// </summary>
public record WorkspaceFolderTemplate : BaseAuditEntity
{
    public const int NameMaxLength = 80;
    public const int StructureMaxLength = 8000;
    /// <summary>Folders one apply may create. Bounded because apply is a bulk insert.</summary>
    public const int MaxFolders = 25;

    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>JSON array of <c>FolderTemplateNode</c>; see the folders feature for the shape.</summary>
    public string StructureJson { get; set; } = "[]";
    public Guid CreatedById { get; set; }

    public Workspace Workspace { get; init; } = null!;
}
