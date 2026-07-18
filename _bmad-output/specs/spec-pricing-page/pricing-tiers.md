# Pricing Tiers

> Single source of truth for TeleCloud subscription tiers. Referenced by the pricing page at `/pricing`.
> 
> **Visibility:** Only Free, Starter, and Ultimate are shown on the pricing page. Teams and Enterprise tiers are defined here for future use but hidden until auth/team infrastructure is built.

## Billing

- **Monthly** — billed every month, higher per-month price.
- **Yearly** — billed annually, 20% discount vs monthly cumulative.
- All prices in USD.

## Tier Overview

| Tier | Monthly Price | Yearly Price | Audience | File Size Limit | Monthly Volume | Transfers/Month | Transfer Expiry | Members |
|------|--------------|-------------|----------|----------------|----------------|-----------------|-----------------|---------|
| **Free** | $0 | $0 | Occasional users, first-time explorers | 200 MB | 3 GB | 10 | Up to 3 days | 1 |
| **Starter** | $8 | $77 ($6.42/mo) | Frequent individual users | 750 MB | 300 GB | 10 | Up to 3 days | 1 |
| **Ultimate** | $23 | $221 ($18.42/mo) | Power users sending big files daily | 1.5 GB | Unlimited | Unlimited | Unlimited | 1 |
| **Teams** | $19/user/mo (min 2) | $182/user/yr (min 2) | Small teams needing shared file solution | 1.5 GB | Unlimited | Unlimited | Unlimited | 2–50 |
| **Enterprise** | Custom | Custom | 50+ member orgs, custom requirements | 1.5 GB | Unlimited | Unlimited | Unlimited | 51+ unlimited |

## Feature Matrix

### Send and Receive

| Feature | Free | Starter | Ultimate | Teams | Enterprise |
|---------|------|---------|----------|-------|------------|
| GB / Month | 3 | 300 | Unlimited | Unlimited | Unlimited |
| Transfers / Month | 10 | 10 | Unlimited | Unlimited | Unlimited |
| Request and receive files | ✗ | ✗ | ✓ | ✓ | ✓ |

### Security

| Feature | Free | Starter | Ultimate | Teams | Enterprise |
|---------|------|---------|----------|-------|------------|
| Transfer expiry | Up to 3 days | Up to 3 days | Unlimited | Unlimited | Unlimited |
| Recover expired transfers | ✗ | ✗ | ✓ | ✓ | ✓ |
| Password-protected transfers | ✗ | ✓ | ✓ | ✓ | ✓ |
| Data encryption | ✓ | ✓ | ✓ | ✓ | ✓ |
| Two-factor authentication | ✗ | ✗ | ✗ | ✓ | ✓ |
| Automatic malware scanning | ✗ | ✗ | ✓ | ✓ | ✓ |

### Branding

| Feature | Free | Starter | Ultimate | Teams | Enterprise |
|---------|------|---------|----------|-------|------------|
| Custom transfer page | ✗ | ✗ | ✓ | ✓ | ✓ |
| Personalized transfer emails | ✗ | ✗ | ✓ | ✓ | ✓ |
| Custom transfer wallpapers | ✗ | ✗ | ✓ | ✓ | ✓ |
| Social links on download page | ✗ | ✗ | ✓ | ✓ | ✓ |
| Wallpaper backgrounds | Advertising | Upload your own | Upload your own | Upload your own | Upload your own |
| Custom branding per transfer | ✗ | ✗ | ✓ | ✓ | ✓ |

### Monetization

| Feature | Free | Starter | Ultimate | Teams | Enterprise |
|---------|------|---------|----------|-------|------------|
| Sell files and receive payment | ✗ | ✗ | ✓ | ✓ | ✓ |
| Platform fees | N/A | N/A | None | None | None |

### Team Features

| Feature | Free | Starter | Ultimate | Teams | Enterprise |
|---------|------|---------|----------|-------|------------|
| Shared branding | Requires Teams | Requires Teams | Requires Teams | ✓ | ✓ |
| Invite team members | Requires Teams | Requires Teams | Requires Teams | ✓ | ✓ |
| Shared storage | Requires Teams | Requires Teams | Requires Teams | ✓ | ✓ |
| Centralized billing | ✗ | ✗ | ✗ | ✓ | ✓ |
| Custom transfer rules | ✗ | ✗ | ✗ | ✗ | ✓ |
| Premium support | ✗ | ✗ | ✗ | ✗ | ✓ |
| Single sign-on (SSO) | ✗ | ✗ | ✗ | ✗ | ✓ |
| Advanced access management | ✗ | ✗ | ✗ | ✗ | ✓ |
| Usage and security logs | ✗ | ✗ | ✗ | ✗ | ✓ |

## Tier Details

### Free

- $0 — no payment required.
- For individual, non-commercial use.
- Entry point: instantly start using TeleCloud without an account.
- Advertising wallpapers on download pages.
- CTA: "Get Started" → `/upload`.

### Starter

- $8/month billed monthly, or $77/year billed annually.
- For individual use only.
- 300 GB monthly volume with 10 transfers/month.
- Adds password-protected transfers and custom wallpaper uploads.
- CTA: "Subscribe" → checkout link.

### Ultimate _(Most Popular)_

- $23/month billed monthly, or $221/year billed annually.
- For individual use only.
- Unlimited monthly volume and transfers.
- Adds custom branding, transfer recovery, malware scanning, file sales.
- CTA: "Subscribe" → checkout link.
- Visual highlight: "Most Popular" badge on the tier card.

### Teams

- $19/user/month billed monthly (minimum 2 users), or $182/user/year billed annually.
- For small teams up to 50 members.
- Everything in Ultimate, plus team creation, shared branding, shared storage, centralized billing, 2FA.
- CTA: "Start Team Trial" or "Subscribe" → checkout link.

### Enterprise

- Custom pricing tailored to business needs.
- For 50+ member organizations.
- Everything in Teams, plus unlimited members, custom transfer rules, premium support, SSO, advanced access management, usage/security logs.
- CTA: "Contact Sales" → contact form or sales email.

## FAQ

Frequently asked questions to display on the pricing page.

**What happens after I sign up and pay?**
Access to your plan's features is immediate. You can start using the paid-tier limits right away.

**Do my recipients need to sign up?**
No. Recipients download files without creating an account, regardless of your plan tier.

**Can I receive bigger transfers from anyone?**
Senders are limited by their own plan's limits. Your plan does not affect what others can send to you.

**How do I add and remove team members?**
Team admins can manage members through the team dashboard (Teams and Enterprise tiers only).

**What payment methods can I use?**
Credit/debit cards and PayPal are accepted. Enterprise customers can use invoicing.

**What happens if I go over my storage limit?**
On Free and Starter plans, transfers will be blocked once the monthly limit is reached until the next billing cycle. Ultimate and above have no volume limits.

**What can I do with a Free plan?**
Share and receive up to 3 GB/month across 10 transfers. Files expire after up to 3 days. Includes data encryption and basic sharing.

**What file size limits apply?**
Free: 200 MB per file. Starter: 750 MB per file. Ultimate: 1.5 GB per file.

**How do I cancel?**
Cancel anytime from your account settings. Your paid features remain active until the end of the current billing period. No refunds for partial months.

**Can I trust TeleCloud? How safe is it?**
Files are transferred over encrypted connections. Password protection and two-factor authentication are available on paid plans.
