# Unpoly and Shadcn migration

QuickShare uses `Unpoly.Blazor` with opt-in link, form and polling enhancement on Blazor static SSR. Direct GET requests render authorized page data and native forms provide the no-JavaScript path. Personal responses and pages showing the antiforgery-protected consent form are `no-store`; cacheable anonymous home and pricing responses vary by cookie, language and Unpoly headers.

`App.razor` loads Unpoly, the vendored `Unpoly.Blazor.Shadcn` assets, generated Tailwind CSS and the SDK's scoped CSS bundle. The client build runs before static assets are collected so fresh builds and publishes include generated JavaScript and CSS. The vendored library's upstream revision and license are recorded in `vendor/Unpoly.Blazor.Shadcn/UPSTREAM.md`.

The runtime package, middleware, headers and Razor integration from the previous interaction library have been removed. Existing DAUB pages remain while the visual migration proceeds; new and migrated components use Shadcn components and tokens.

Razor-handled POST forms have unique form names, run mutations in their submit handlers and validate antiforgery tokens. Forms sent to dedicated endpoints also validate antiforgery. Profile, notification preferences, retention policy, billing, MFA, workspace invitation and ownership transfer, device removal, onboarding dismissal, admin flags and reports remain usable without JavaScript. Upgrade GET renders options without creating a payment intent; checkout POST creates one, and Unpoly polls the owner-scoped payment status. The status page also provides a manual refresh link. The MFA attempt limiter and its verification lock are process-local; deployments with multiple app instances need shared rate limiting and database-level replay protection.

Regression coverage includes `AccountUnpolyMigrationTests`, `RetentionNativeFormTests`, `BillingNativeFormTests`, `AccountProfileTests`, `AdminFlagFormTests`, `AdminReportFormTests`, `UpgradeNativeFlowTests`, `ClientAssetTests` and `ResponseCacheTests`. Browser coverage exercises fragment reloads, delivery filters and payment polling with a no-JavaScript fallback.
