namespace QuickShare.Infrastructure.Constants;

public static class ErrorCodes
{
    public const string NotFound = "not_found";
    public const string BadRequest = "bad_request";
    public const string DeliveryNotFound = "delivery_not_found";
    public const string DeliveryExpired = "delivery_expired";
    public const string DeliverySuspended = "delivery_suspended";
    public const string DeliveryRevoked = "delivery_revoked";
    public const string DeliveryNoExpiry = "delivery_no_expiry";
    public const string FileNameInvalid = "file_name_invalid";
    public const string DownloadLimitReached = "download_limit_reached";
    public const string DeliveryDailyLimitReached = "delivery_daily_limit_reached";
    public const string DailyLimitInvalid = "daily_limit_invalid";
    public const string RequiresPaidPlan = "requires_paid_plan";
    public const string AliasInvalid = "alias_invalid";
    public const string AliasTaken = "alias_taken";
    public const string MonthlyQuotaExceeded = "monthly_quota_exceeded";
    public const string SchedulingRequiresPaidPlan = "scheduling_requires_paid_plan";
    public const string DeliverAtMustBeFuture = "deliver_at_must_be_future";
    public const string DeliveryNotActive = "delivery_not_active";
    public const string DeliveryLocked = "delivery_locked";
    public const string LockDurationInvalid = "lock_duration_invalid";
    public const string DeliveryNotScheduled = "delivery_not_scheduled";
    public const string MessageTooLong = "message_too_long";
    public const string DeliveryAlreadyDownloaded = "delivery_already_downloaded";
    public const string DeliveryGated = "delivery_gated";
    public const string TrialRequiresFreePlan = "trial_requires_free_plan";
    public const string TrialAlreadyUsed = "trial_already_used";
    public const string ExpiryInvalid = "expiry_invalid";
    public const string ExpiryTooLong = "expiry_too_long";
    public const string PasswordTooLong = "password_too_long";
    public const string Sha256Invalid = "sha256_invalid";
    public const string ExtendNotAllowed = "extend_not_allowed";
    public const string ExtendTooEarly = "extend_too_early";
    public const string ExtendTooLate = "extend_too_late";
    public const string TagTooLong = "tag_too_long";
    public const string AccountDeleteConfirmMismatch = "account_delete_confirm_mismatch";
    public const string NotZip = "not_a_zip";
    public const string ZipTooLarge = "zip_too_large";
    public const string ZipTooManyEntries = "zip_too_many_entries";
    public const string FileRequestNotFound = "file_request_not_found";
    public const string FileRequestExpired = "file_request_expired";
    public const string FileRequestClosed = "file_request_closed";
    public const string FileRequestLimitReached = "file_request_limit_reached";
    public const string FileRequestTitleInvalid = "file_request_title_invalid";
    public const string FileRequestExpiryInvalid = "file_request_expiry_invalid";
    public const string NotificationNotFound = "notification_not_found";
    public const string UrlInvalid = "url_invalid";
    public const string UrlFetchFailed = "url_fetch_failed";
    public const string UrlTooLarge = "url_too_large";
    public const string ExtensionRequestsDisabled = "extension_requests_disabled";
    public const string DeliveryNotExpired = "delivery_not_expired";
    public const string ExtensionRequestNotFound = "extension_request_not_found";
    public const string RecipientLimitExceeded = "recipient_limit_exceeded";
    public const string EmailOtpCooldown = "email_otp_cooldown";
    /// <summary>Caller is outside the delivery's IP allowlist or country rules (issues #142/#166).</summary>
    public const string DeliveryRestricted = "delivery_restricted";
    /// <summary>Operation refused because a legal hold freezes the delivery (issue #266).</summary>
    public const string LegalHoldActive = "legal_hold_active";
    public const string LegalHoldNotActive = "legal_hold_not_active";
    public const string LegalHoldReasonRequired = "legal_hold_reason_required";
    public const string RetentionDaysInvalid = "retention_days_invalid";
    public const string RetentionPolicyLocked = "retention_policy_locked";
    // Team workspaces (issue #106) and ownership transfer (issue #252).
    public const string WorkspaceNotFound = "workspace_not_found";
    public const string WorkspaceForbidden = "workspace_forbidden";
    public const string WorkspaceAlreadyExists = "workspace_already_exists";
    public const string WorkspaceNameInvalid = "workspace_name_invalid";
    public const string WorkspaceSeatsInvalid = "workspace_seats_invalid";
    public const string WorkspaceSeatsFull = "workspace_seats_full";
    public const string WorkspaceEmailInvalid = "workspace_email_invalid";
    public const string WorkspaceInviteInvalid = "workspace_invite_invalid";
    public const string WorkspaceInviteExpired = "workspace_invite_expired";
    public const string WorkspaceAlreadyMember = "workspace_already_member";
    public const string WorkspaceMemberNotFound = "workspace_member_not_found";
    public const string WorkspaceOwnerImmutable = "workspace_owner_immutable";
    public const string WorkspaceTransferOpen = "workspace_transfer_open";
    public const string WorkspaceTransferInvalid = "workspace_transfer_invalid";
    public const string WorkspaceTransferSelf = "workspace_transfer_self";
    /// <summary>Account deletion refused while the caller still owns a workspace (issues #59/#252).</summary>
    public const string WorkspaceTransferRequired = "workspace_transfer_required";
    // Shared folders (#151), per-folder defaults (#245), folder templates (#234).
    public const string FolderNotFound = "folder_not_found";
    public const string FolderNameInvalid = "folder_name_invalid";
    public const string FolderNameTaken = "folder_name_taken";
    public const string FolderTooDeep = "folder_too_deep";
    public const string FolderNotEmpty = "folder_not_empty";
    public const string FolderLimitReached = "folder_limit_reached";
    /// <summary>Locked folder defaults require a password and the commit did not carry one (#245).</summary>
    public const string FolderPasswordRequired = "folder_password_required";
    public const string TemplateNotFound = "template_not_found";
    public const string TemplateNameInvalid = "template_name_invalid";
    public const string TemplateNameTaken = "template_name_taken";
    public const string TemplateStructureInvalid = "template_structure_invalid";
    // Seats (#278), overage (#141), e-invoice (#112), PO + net terms (#192).
    public const string BillingProfileInvalid = "billing_profile_invalid";
    public const string TaxCodeInvalid = "tax_code_invalid";
    public const string NetTermsInvalid = "net_terms_invalid";
    public const string InvoiceNotFound = "invoice_not_found";
    public const string InvoiceNotIssued = "invoice_not_issued";
    public const string SeatsRequireWorkspace = "seats_require_workspace";
    // Tenant isolation (#190), SIEM streaming (#191), DPA (#184).
    public const string SinkEndpointInvalid = "sink_endpoint_invalid";
    public const string SinkFormatInvalid = "sink_format_invalid";
    public const string SinkNotFound = "sink_not_found";
    public const string DpaSignerRequired = "dpa_signer_required";
    public const string DpaAlreadySigned = "dpa_already_signed";
    // Scheduled workspace backup (#271).
    public const string BackupEndpointInvalid = "backup_endpoint_invalid";
    public const string BackupBucketRequired = "backup_bucket_required";
    public const string BackupFrequencyInvalid = "backup_frequency_invalid";
    public const string BackupRetentionInvalid = "backup_retention_invalid";
    public const string BackupPolicyNotFound = "backup_policy_not_found";
    // Workspace branding (#285) and custom fonts (#286).
    public const string BrandNotFound = "brand_not_found";
    public const string BrandColorInvalid = "brand_color_invalid";
    public const string BrandContrastTooLow = "brand_contrast_too_low";
    public const string BrandFontNameInvalid = "brand_font_name_invalid";
    public const string BrandAssetKindInvalid = "brand_asset_kind_invalid";
    public const string BrandAssetMissing = "brand_asset_missing";
    public const string BrandAssetTooLarge = "brand_asset_too_large";
    public const string BrandAssetTypeInvalid = "brand_asset_type_invalid";
    /// <summary>Requested storage region is not configured on this deployment (issue #119).</summary>
    public const string StorageRegionInvalid = "storage_region_invalid";
    // Approval workflow (#162) and recurring delivery (#176).
    public const string ApprovalNotPending = "approval_not_pending";
    public const string ApprovalSelfNotAllowed = "approval_self_not_allowed";
    public const string DenyReasonRequired = "deny_reason_required";
    public const string RecurrenceInvalid = "recurrence_invalid";
    public const string RecurrenceNotFound = "recurrence_not_found";
    public const string RecurrenceRecipientsRequired = "recurrence_recipients_required";
    // Delivery comments (#289) and channel posting (#180).
    public const string CommentsDisabled = "comments_disabled";
    public const string CommentBodyRequired = "comment_body_required";
    public const string CommentAuthorRequired = "comment_author_required";
    public const string CommentNotFound = "comment_not_found";
    public const string CommentTooDeep = "comment_too_deep";
    public const string CommentRateLimited = "comment_rate_limited";
    public const string ChannelUrlInvalid = "channel_url_invalid";
    public const string ChannelKindInvalid = "channel_kind_invalid";
    public const string ChannelNotFound = "channel_not_found";
    // Authenticator (#128), enterprise SSO (#181), SCIM (#146), MFA policy (#219).
    public const string TotpAlreadyEnrolled = "totp_already_enrolled";
    public const string TotpNotEnrolled = "totp_not_enrolled";
    public const string TotpCodeInvalid = "totp_code_invalid";
    /// <summary>A workspace policy requires the authenticator that is being removed (issue #219).</summary>
    public const string MfaPolicyLocked = "mfa_policy_locked";
    /// <summary>The caller must answer the second-factor challenge before this operation.</summary>
    public const string MfaRequired = "mfa_required";
    public const string MfaGraceInvalid = "mfa_grace_invalid";
    public const string SsoProviderInvalid = "sso_provider_invalid";
    public const string SsoDirectoryRequired = "sso_directory_required";
    public const string SsoNotConfigured = "sso_not_configured";
    /// <summary>Another workspace already federates to this directory (issue #181).</summary>
    public const string SsoDirectoryTaken = "sso_directory_taken";
    /// <summary>Switching on SSO-only would lock the owner out of their own workspace (issue #181).</summary>
    public const string SsoWouldLockOutOwner = "sso_would_lock_out_owner";
    public const string ScimTokenNotFound = "scim_token_not_found";
    public const string ScimUnauthorized = "scim_unauthorized";
}
