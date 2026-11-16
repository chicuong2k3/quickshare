namespace QuickShare.Infrastructure.Constants;

public static class ApiTags
{
    public const string Deliveries = "Deliveries";
    public const string Downloads = "Downloads";
    public const string Payments = "Payments";
    public const string P2p = "P2P";
    public const string Uploads = "Uploads";
    public const string Ads = "Ads";
    public const string ClientErrors = "Client Errors";
    public const string Notifications = "Notifications";
    public const string Admin = "Admin";
    public const string Analytics = "Analytics";
    public const string Users = "Users";
    public const string Reports = "Reports";
    public const string FileRequests = "File Requests";
    public const string Workspaces = "Workspaces";
    /// <summary>SCIM 2.0 provisioning (issue #146). Not a normal API surface — IdPs drive it.</summary>
    public const string Scim = "SCIM";
    /// <summary>API keys and outbound webhooks (issues #115, #39).</summary>
    public const string Developer = "Developer";
}
