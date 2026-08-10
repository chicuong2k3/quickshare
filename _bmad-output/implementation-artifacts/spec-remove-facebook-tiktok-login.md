---
title: 'Remove Facebook and TikTok login'
type: 'chore'
created: '2026-08-09'
status: 'in-progress'
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
baseline_revision: '096c548de6f4acdfb978447f42497037feced779'
---

<intent-contract>

## Intent

**Problem:** Facebook and TikTok remain exposed as login options and configuration even though neither provider is registered by the backend. Their links are dead and leave stale auth surface area.

**Approach:** Remove Facebook/TikTok login markup, styling, localization, environment wiring, and active documentation. Preserve the existing Google/GitHub UI changes and Google backend flow. Verify both provider challenge paths remain unavailable.

## Boundaries & Constraints

**Always:** Keep Google authentication behavior unchanged. Keep existing uncommitted login visual changes and GitHub markup unchanged. Remove provider-specific references from active source, test, config, and `docs/` artifacts.

**Block If:** A Facebook/TikTok backend registration or persisted production dependency is discovered that requires a migration or credential decision.

**Never:** Do not remove Google, GitHub UI, generic cookie auth, the generic challenge endpoint, unrelated Facebook/TikTok mentions in product or third-party datasets, or historical planning records.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Login page | GET `/login` | HTML contains Google/GitHub providers and no Facebook/TikTok labels, URLs, classes, or provider data attributes | Existing page response behavior unchanged |
| Removed provider challenge | GET `/login/facebook` or `/login/tiktok` | HTTP 404 because no matching authentication scheme exists | No OAuth challenge or redirect |
| Deployment config | Compose or `.env.example` loaded | No Facebook/TikTok environment variables are mapped or documented | Existing Google configuration remains |

</intent-contract>

## Code Map

- `src/QuickShare/Components/Pages/Login.razor` -- login provider markup and provider URL constants.
- `src/QuickShare/Components/Pages/Login.razor.css` -- scoped provider styling; generated output is copied to `wwwroot/css/QuickShare.styles.css`.
- `src/QuickShare/SharedLoc.en.resx` -- English provider labels.
- `src/QuickShare/SharedLoc.vi.resx` -- Vietnamese provider labels.
- `.env.example` -- local OAuth variable template.
- `docker-compose.yml` -- container OAuth environment mappings.
- `tests/QuickShare.Server.Tests/E2E/AuthFlowTests.cs` -- login page and unknown-provider route coverage.
- `docs/architecture.md` -- active auth and SSR documentation.
- `docs/login-architecture.md` -- active login architecture documentation.

## Tasks & Acceptance

**Execution:**
- [ ] Remove Facebook/TikTok constants, links, icons, and scoped CSS plus generated CSS output -- eliminate dead UI and stale styling.
- [ ] Remove Facebook/TikTok localization and environment/container configuration -- eliminate stale user-facing and deployment surfaces.
- [ ] Update auth tests and active docs; clarify Google-only backend wording where it claims all providers -- keep behavior and documentation consistent.

**Acceptance Criteria:**
- Given the login page, when it is rendered, then Facebook and TikTok do not appear anywhere in the response while Google and existing GitHub markup remain.
- Given an unauthenticated request to `/login/facebook` or `/login/tiktok`, when the challenge route resolves schemes, then it returns 404 and never redirects to an OAuth provider.
- Given `.env.example` and `docker-compose.yml`, when searched for provider names or credentials, then no Facebook/TikTok auth configuration remains.
- Given active source, tests, CSS, localization, and `docs/` auth documentation, when searched for Facebook/TikTok provider references, then none remain.

## Verification

**Commands:**
- `rtk dotnet test tests/QuickShare.Server.Tests/QuickShare.Server.Tests.csproj --filter FullyQualifiedName~AuthFlowTests` -- expected: all auth flow tests pass.
- `rtk dotnet build src/QuickShare/QuickShare.csproj --no-restore` -- expected: build succeeds and regenerated scoped CSS contains no removed provider selectors.
