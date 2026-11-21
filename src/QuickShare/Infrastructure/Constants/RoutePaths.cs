namespace QuickShare.Infrastructure.Constants;

public static class RoutePaths
{
    public const string Home = "/";
    public const string Send = "/send";
    public const string Pricing = "/pricing";
    public const string Upgrade = "/upgrade";
    public const string Account = "/account";
    public const string AccountProfile = "/account/profile";
    public const string AccountBilling = "/account/billing";
    public const string AccountDeliveries = "/account/deliveries";
    public const string AccountDeliveryLabels = "/account/deliveries/labels";
    public const string AccountUsage = "/account/usage";
    public const string AccountDevices = "/account/devices";
    public const string AccountDeviceRemove = "/account/devices/remove";
    public const string ServiceWorker = "/sw.js";
    public const string ScopedStyles = "/QuickShare.styles.css";
    public const string ScopedStylesPrefix = "/QuickShare.";
    public const string ScopedStylesSuffix = ".styles.css";
    public const string AccountSecurityActivity = "/account/security/activity";
    public const string AccountNotifications = "/account/notifications";
    public const string AccountNotificationCenter = "/account/notifications/inbox";
    public const string AccountNotificationReadAll = "/account/notifications/inbox/read-all";
    public const string AccountDownloads = "/account/downloads";
    public const string AccountFileRequests = "/account/requests";
    public const string FileRequestPage = "/r/";
    public const string DeliveryPage = "/d/";
    public const string Login = "/login";
    public const string Logout = "/logout";
    public const string P2p = "/p2p";
    public const string P2pSend = "/p2p/send";
    public const string Api = "/api";
    public const string UpgradePro = "/upgrade?plan=pro";
    public const string UpgradeStudio = "/upgrade?plan=studio";
    public const string CookieConsent = "/api/cookie-consent";
    public const string PayOSReturn = "/payments/payos/return";
    public const string Unsubscribe = "/unsubscribe";
    public const string ConfirmSenderEmail = "/confirm-sender-email";
    public const string UnsubscribeSender = "/unsubscribe-sender";
    public const string Terms = "/terms";
    public const string Privacy = "/privacy";
    public const string ToolsOnlineClipboard = "/tools/online-clipboard";
    public const string ToolsSendImagesUncompressed = "/tools/gui-anh-khong-nen";
    public const string ToolsShareText = "/tools/share-text";
    public const string AdminFlags = "/admin/flags";
    public const string AdminAnalytics = "/admin/analytics";
    public const string AdminReports = "/admin/reports";
    public const string Abuse = "/abuse";
    public const string NotFound = "/not-found";
    public const string SeoSendLargeFilesFree = "/how-to/send-large-files-free";
    public const string SeoWeTransferAlternatives = "/wetransfer-alternatives";
    public const string SeoP2pFileTransfer = "/p2p-file-transfer";
    public const string SeoViGuiFileLonMienPhi = "/vi/gui-file-lon-mien-phi";
    public const string Changelog = "/changelog";
    public const string Roadmap = "/roadmap";
    public const string Security = "/security";
    public const string Compare = "/compare";
    public const string WhyPrivate = "/why-private";
    public const string Sitemap = "/sitemap.xml";
    public const string Robots = "/robots.txt";
    public const string Status = "/status";
    public const string Help = "/help";
    public const string AccountReferral = "/account/referral";
    public const string AccountWorkspace = "/account/workspace";
    public const string AccountWorkspaceAudit = "/account/workspace/audit";
    public const string AccountWorkspaceReport = "/account/workspace/report";
    public const string AccountWorkspaceFolders = "/account/workspace/folders";
    public const string AccountWorkspaceSeats = "/account/workspace/seats";
    public const string AccountInvoices = "/account/invoices";
    public const string AccountWorkspaceBrand = "/account/workspace/brand";
    public const string AccountWorkspaceChannel = "/account/workspace/channel";
    public const string AccountWorkspaceDomain = "/account/workspace/domain";
    public const string AccountSecurity = "/account/security";
    public const string AccountDeveloper = "/account/developer";
    /// <summary>Invite and transfer accept landing page — the token rides in the query string.</summary>
    public const string WorkspaceJoin = "/workspace/join";
    /// <summary>Upload presets management page (issue #100).</summary>
    public const string AccountPresets = "/account/presets";
    public const string ChangelogRss = "/changelog.xml";
    public const string ApiDocs = "/api-docs";
    public const string WidgetPage = "/widget/";
    public const string DeliveryCardRoute = "/api/downloads/{shareCode}/card.svg";
    public const string BrevoEmailWebhook = "/api/webhooks/email/brevo";
    public const string AdminEmailSuppressions = "/admin/email-suppressions";
    public const string AdminEmailSuppressionsApi = "/api/admin/email-suppressions";
    public const string DeliverySubtitlesRoute = "/api/downloads/{ShareCode}/subtitles/{FileId}";
    public const string P2pPairRoute = "/api/shares/p2p/pair";
    public const string DeliveryWalletPassRoute = "/api/downloads/{ShareCode}/wallet-pass";
    public const string AdminCampaignsApi = "/api/admin/campaigns";
    public const string ActiveCampaignsApi = "/api/campaigns/active";
    public const string AdminAnalyticsCsvRoute = "/api/admin/analytics/export.csv";
    public const string SearchApi = "/api/search";
    public const string SmsOptOutApi = "/api/webhooks/sms/opt-out";
    public const string DeliveryRestoreApi = "/api/downloads/{ShareCode}/restore";
    public const string AccountColdStorageApi = "/api/account/cold-storage";
    public const string AccountSendingDomainApi = "/api/account/sending-domain";
    public const string AccountSendingDomainVerifyApi = "/api/account/sending-domain/verify";
    public const string SupportChatApi = "/api/support/chat";
    public const string AccountSupport = "/account/support";
    public const string CheckoutSessionApi = "/api/payments/checkout-session";
    public const string SecurityPentestApi = "/api/security/pentest";
    public const string AdminSecurityPentestApi = "/api/admin/security/pentest";
    public const string SeoZhSendLargeFilesFree = "/zh/send-large-files-free";
    public const string SeoJaSendLargeFilesFree = "/ja/send-large-files-free";
    public const string SeoEsSendLargeFilesFree = "/es/enviar-archivos-grandes-gratis";
    public const string SeoArSendLargeFilesFree = "/ar/send-large-files-free";
    public const string WorkspaceByokApi = "/api/workspace/byok";
    public const string WorkspaceByokRotateApi = "/api/workspace/byok/rotate";
    public const string WorkspaceByokRevokeApi = "/api/workspace/byok/revoke";
    public const string DeliveryOfflineBundleApi = "/api/downloads/{ShareCode}/offline-bundle";
    public const string OfflineUploadQueueApi = "/api/uploads/offline-queue";
    public const string PdfToolsApi = "/api/tools/pdf";
    public const string DeliveryHlsMasterApi = "/api/downloads/{ShareCode}/hls/master.m3u8";
    public const string DeliveryHlsVariantApi = "/api/downloads/{ShareCode}/hls/{Quality}/playlist.m3u8";
    public const string LiveStreamProgressApi = "/api/uploads/live-progress";
    public const string SdkManifestApi = "/api/sdk/manifest";
    public const string SelfHostManifestApi = "/api/self-host/manifest";
    public const string TelegramBotWebhookApi = "/api/webhooks/telegram/bot";
    public const string DiscordBotWebhookApi = "/api/webhooks/discord/bot";
    public const string DiscordCommunityApi = "/api/community/discord";
    public const string DesktopAppManifestApi = "/api/apps/desktop/manifest";
    public const string DesktopQrAuthStartApi = "/api/apps/desktop/qr-auth";
    public const string DesktopQrAuthApproveApi = "/api/apps/desktop/qr-auth/approve";
    public const string BrowserExtensionManifestApi = "/api/apps/browser-extension/manifest.json";
    public const string BrowserExtensionShareApi = "/api/apps/browser-extension/share";
    public const string ZaloMiniAppManifestApi = "/api/zalo/mini-app/manifest";
    public const string ZaloMiniAppShareApi = "/api/zalo/mini-app/share";
    public const string IosShareShortcutApi = "/api/apps/ios/shortcut-manifest";
    public const string IosShareIngestApi = "/api/apps/ios/share";
    public const string OpenSourceAuditApi = "/api/security/open-source-audit";
    public const string LaunchKitApi = "/api/growth/launch-kit";

    public static bool IsScopedStyles(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (string.Equals(path, ScopedStyles, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.StartsWith(ScopedStylesPrefix, StringComparison.OrdinalIgnoreCase)
            && path.EndsWith(ScopedStylesSuffix, StringComparison.OrdinalIgnoreCase);
    }
}
