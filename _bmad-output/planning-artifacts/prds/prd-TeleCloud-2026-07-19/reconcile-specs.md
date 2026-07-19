# Codebase Specs Reconciliation

## Sources
- `_bmad-output/specs/spec-telecloud/SPEC.md`
- `_bmad-output/specs/spec-telecloud/architecture.md`
- `_bmad-output/architecture/architecture-TeleCloud-2026-07-18/ARCHITECTURE-SPINE.md`

## Extracted Items

### A. Technology Stack & Core Architecture

1. **Runtime & Frameworks**: .NET 10 (ASP.NET Core), FastEndpoints 8.x, WolverineFx 5.x, EF Core 10.x
2. **Database**: PostgreSQL (Neon.tech) via Npgsql 10.0.0 with Wolverine inbox/outbox
3. **Result Pattern**: LightResults 10.0.4 for functional error handling
4. **Object Mapping**: Riok.Mapperly 4.3.1 for Folder, manual mapping for File
5. **API Documentation**: Swagger UI via Swashbuckle + FastEndpoints.Swagger
6. **Source Generation**: Custom Roslyn incremental generator for sort extensions

### B. Data Flow & Service Layer

7. **Request Flow**: FastEndpoints → IMessageBus.InvokeAsync → Handlers → Telegram/PostgreSQL → LightResults.Result → HTTP response
8. **Wolverine Configuration**: PersistMessagesWithPostgresql + AutoApplyTransactions + UseEntityFrameworkCoreTransactions
9. **TelegramService**: Singleton wrapping WTelegram.Client with lazy initialization and thread-safe double-check locking
10. **StorageChannel Abstraction**: Encapsulates Telegram channel peer, channel ID, access hash
11. **GlobalExceptionHandler**: Catches unhandled exceptions → RFC 7807 ProblemDetails 500

### C. Legacy Storage Architecture (Telegram-based)

12. **Telegram Storage Backend**: WTelegramClient 4.x with user-account authentication (not bot API)
13. **Single Account Architecture**: One Telegram account serves all users; isolation at PostgreSQL layer via OwnerId
14. **Telegram Session Persistence**: WTelegram.session file for authentication persistence
15. **Channel Resolution**: Numeric channel ID first, fallback to username resolution
16. **Telegram Constraints**: 2 GB per-file limit, channel message rate limits, no encryption beyond Telegram's default

### D. API & Endpoint Structure

17. **Endpoint Pattern**: Thin delegates that extract transport data and dispatch via IMessageBus
18. **Endpoint Count**: 10 endpoints (5 Files + 5 Folders)
19. **Auth Enforcement**: All endpoints must call AllowAnonymous() or RequireAuthorization(); default is RequireAuthorization()
20. **Sort Validation**: Source-generated TryValidateSorts validates sort properties before use (invalid → 400)
21. **List Query Params**: sort[], page, pageSize, folderId, search with multi-column sort support

### E. Data Model & Schema

22. **Files Table**: Id (UUID PK), Filename, ContentType, Size, ChannelId, MessageId, AccessHash, FileReference (byte[]), DocumentId, FolderId (FK), Tags (text[]), CreatedAt
23. **Folders Table**: Id (UUID PK), Path (UNIQUE), CreatedAt
24. **Indexes**: IX_files_FolderId, IX_files_Filename, IX_files_CreatedAt DESC, IX_folders_Path UNIQUE
25. **Foreign Keys**: ON DELETE RESTRICT - folders with files cannot be deleted
26. **Migration History**: InitialCreate → AddFoldersTable (string folder → FK migration)

### F. Authentication & Authorization Architecture

27. **Auth Paradigm**: Blazor Static SSR + FastEndpoints API with Cookie Auth, mediated by Google OAuth
28. **Cookie Configuration**: .AspNetCore.TeleCloud cookie with shared Data Protection keyring between Web/Server processes
29. **Auth Middleware**: Web SSR middleware redirects unauthenticated users to /login with public allowlist
30. **CurrentUserService**: Resolves current user from HttpContext.User for handlers
31. **TempShare Ownership**: OwnerId (Guid?) attribute added to enforce per-user quotas
32. **Plan Tier System**: User.Plan field (free/starter/ultimate) with static PlanTierLimits lookup
33. **UserUsage Entity**: Tracks per-user monthly usage (PeriodStart, TotalBytesUploaded, TransferCount)
34. **Rate Limiting**: Pre-flight quota checks before temp share creation; 403 on exceeded limits

### G. UI & Frontend Architecture

35. **UI Framework**: RizzyUI Razor components (RzButton, RzCard, RzBadge, RzTable, RzAvatar, etc.)
36. **No Raw HTML Elements**: No raw HTML button, link, table, card, badge, or progress elements
37. **Layout Structure**: Plain `<div>`, `<span>`, `<p>`, `<header>`, `<footer>`, `<main>`, `<nav>` allowed
38. **Tailwind Classes**: Available for spacing, alignment, and custom overrides

### H. Legacy Feature Constraints

39. **Telegram Constraints**: No bot API usage, user-account authentication only
40. **No File Encryption**: No encryption at rest or in transit beyond what Telegram provides
41. **No Automatic Creation**: No automatic channel or folder creation; both must be pre-configured
42. **No Media Processing**: No thumbnail generation, preview, or media transcoding
43. **No Soft-Delete**: File deletion removes from both Telegram and PostgreSQL
44. **No Asset Preview**: No asset (image) preview or gallery generation
45. **2FA Deferred**: Not needed while Telegram is the single shared storage layer
46. **OAuth Providers**: Google, TikTok, Facebook, GitHub configurable via TeleCloudOptions

### I. Payment & Business Logic (Legacy)

47. **Stripe Integration**: Paid transfers via Stripe with webhook confirmation
48. **OAuth Configuration**: Client_id and client_secret per provider in TeleCloudOptions
49. **File Request Links**: Unique, revocable, optional expiration, max file size setting per link
50. **Custom Branding**: Per-transfer logo, background, and personalized message (not global)
51. **Password Protection**: Optional password on file transfers with recipient verification
52. **Expiration Dates**: Configurable expiration date/time per file transfer

### J. Feature Folders Structure

53. **Vertical Slice Architecture**: Features/Files/ and Features/Folders/ with Shared/, Create/, List/, GetInfo/, Download/, Delete/ subfolders
54. **Endpoint-Handler Pattern**: Each endpoint has corresponding command/query handler with Result<T> return
55. **Response DTOs**: FileInfoResponse, FolderInfoResponse with dedicated mappers
56. **Error Classes**: NotFoundError, BadRequestError, ConflictError, ValidationError

### K. Performance & Scalability Constraints

57. **Telegram Rate Limits**: Channel message rate limits apply
58. **File Size Limits**: Telegram's 2 GB per-file limit
59. **Pagination**: PagedResult<T> with page/pageSize parameters
60. **Concurrency Control**: Plan-dependent concurrency limits for chunked operations

### L. Project Structure & Organization

61. **Project Layout**: TeleCloud.Server/, TeleCloud.Querying/, TeleCloud.Querying.Generators/
62. **Feature Organization**: Features/Files/ (5 endpoints), Features/Folders/ (5 endpoints)
63. **Infrastructure**: GlobalExceptionHandler, ResultExtensions, ApiCookieAuthExtensions
64. **Services**: TelegramService, TelegramInitializer, StorageChannel, TeleCloudOptions

## Reconciliation Status

### ✅ Captured in PRD

- **A.1**: Technology stack (.NET 10, FastEndpoints, WolverineFx, EF Core, PostgreSQL)
- **A.4**: Result pattern (LightResults) maintained
- **B.8**: Wolverine transaction configuration maintained
- **D.17**: Endpoint pattern (thin delegates via IMessageBus)
- **E.22-24**: Data model structure (folders, files, indexes)
- **F.27**: Cookie auth paradigm maintained
- **F.31-33**: User ownership attribution and plan tier system
- **G.35**: RizzyUI UI framework requirement maintained
- **J.54-56**: Endpoint-handler pattern with Result<T> returns

### ✅ Captured in Addendum

- **C.12-15**: Telegram storage backend REJECTED with detailed economic analysis
- **F.34**: Rate limiting and per-user quotas maintained
- **I.47**: Stripe REJECTED (unavailable in Vietnam), replaced with VietQR
- **K.57-58**: Telegram constraints documented as rejection rationale

### 🟡 Partially Captured

- **A.2**: PostgreSQL maintained but no longer primary storage (used for metadata only)
- **B.7**: Request flow maintained but storage backend changed from Telegram to owned disk
- **D.19**: Auth enforcement maintained but requirements expanded for recipient-never-authenticates
- **I.50-52**: Custom branding, password protection, expiration maintained but now part of owned storage architecture
- **K.59-60**: Pagination maintained but concurrency now applies to chunked uploads
- **L.61-64**: Project structure maintained but needs updates for chunked storage architecture

### 🔴 Missing from PRD/Addendum

#### A. Critical Technical Gaps

1. **TelegramService & StorageChannel Abstraction**: PRD removes Telegram but doesn't specify replacement service architecture for owned disk storage
2. **Chunked Storage Mechanism**: PRD mentions chunking but doesn't specify handler patterns, transaction boundaries, or commit protocols
3. **Wolverine Transaction Boundaries**: How chunk uploads, manifest commits, and quota checks map to Wolverine AutoApplyTransactions
4. **Source Generator Architecture**: Sort extension generator design and DynamicSort attribute pattern not captured
5. **Migration Strategy**: How to migrate from existing Telegram-based system to owned storage
6. **Endpoint Configuration**: TeleCloudOptions structure for owned storage (replaces Telegram credentials)
7. **File Request Links**: Legacy CAP-11 feature not mentioned in PRD but may still be relevant
8. **OAuth Provider Scope**: Only Google mentioned in PRD; TikTok/Facebook/GitHub status unclear
9. **Download Tracking Schema**: How download count, timestamp, and per-recipient identity are tracked
10. **Ad Network Integration**: How Adsterra/PopAds networks integrate with recipient pages

#### B. Architectural Decision Gaps

11. **Storage Service Interface**: No replacement abstraction specified for TelegramService/StorageChannel
12. **Chunk Storage Abstraction**: How chunks are stored, referenced, and retrieved from owned disk
13. **Manifest Storage Design**: How encrypted manifests are stored and retrieved by Share ID
14. **Overflow Architecture**: How cold overflow to Backblaze B2 integrates with primary storage
15. **Concurrency Control**: How Plan-dependent concurrency limits are enforced for chunked operations
16. **Rate Limiting Architecture**: How rate limits are enforced per Plan and per operation
17. **Cookie Sharing Configuration**: Data Protection keyring sharing between Web/Server for cookie auth
18. **Public Endpoint Security**: How public endpoints allowlist works without breaking recipient-never-authenticates
19. **Middleware Pipeline**: How auth middleware, rate limiting, and abuse detection are ordered
20. **Background Job Architecture**: How expiry cleanup, storage reclamation, and usage rollover work

#### C. Configuration & Implementation Gaps

21. **TeleCloudOptions Schema**: New configuration structure for owned storage, overflow, ad networks, payment rails
22. **Plan Limits Configuration**: How PlanTierLimits is structured and made configurable
23. **Chunk Size & Concurrency Defaults**: How chunk target size and concurrency limits are configured
24. **Ad Network Configuration**: How ad network credentials, categories, and allowlists are configured
25. **VietQR Integration**: How VietQR payment flow integrates with delivery unlock mechanism
26. **Error Mapping**: How new error types (quota exceeded, payment required, abuse locked) map to HTTP status codes
27. **Logging & Monitoring**: What is logged (metadata-only) vs not logged (content) for abuse detection
28. **Migration Commands**: How EF migrations handle schema changes from Telegram to owned storage
29. **Health Check Architecture**: How storage health, disk space, and overflow status are monitored
30. **Deployment Architecture**: How dedicated server, cold overflow, and Web/Server processes are deployed

#### D. Data Model Gaps

31. **Delivery/Manifest Schema**: How Delivery/Manifest entities relate to legacy File/TempShare entities
32. **Chunk Table Design**: How chunks are stored, indexed, and garbage-collected
33. **Download Tracking Schema**: How download count, last download, and per-recipient identity are stored
34. **UserUsage Period Management**: How monthly periods are calculated and rolled over
35. **Ad Impression Tracking**: How ad impressions, revenue, and adblock detection are stored
36. **Abuse Reporting Schema**: How reports, takedowns, and abuse metadata are stored
37. **Payment Schema**: How VietQR payments, webhooks, and payment status are stored
38. **Share ID Generation**: How Share IDs are generated and ensured to be unique
39. **Fragment Key Handling**: How fragment keys are validated and extracted from URLs
40. **Storage Reference Schema**: How storage references (disk path vs Backblaze B2 key) are structured

#### E. Feature Implementation Gaps

41. **Chunk Upload Flow**: How chunk uploads are ordered, deduplicated, and committed
42. **Resume Mechanism**: How chunk-level resume works after connection loss
43. **Parallel Download Reassembly**: How parallel chunk downloads are reassembled and verified
44. **Integrity Verification**: How per-chunk and whole-file SHA-256 verification is implemented
45. **Client-Side Crypto Integration**: How client-side encryption keys are generated and managed
46. **Fragment Key Extraction**: How fragment keys are extracted from URL fragments and used for decryption
47. **Password-Protected Access**: How password protection interacts with fragment key decryption
48. **Payment Gating**: How payment verification gates access to encrypted manifests
49. **Ad Injection**: How ads are injected into recipient pages based on sender Plan
50. **Adblock Detection**: How adblock detection works without blocking file access

#### F. Testing & Validation Gaps

51. **Chunk Integrity Testing**: How chunk upload/download and integrity verification are tested
52. **Crypto Correctness Testing**: How client-side encryption is tested and verified
53. **Rate Limit Testing**: How rate limits and quota enforcement are tested
54. **Payment Flow Testing**: How VietQR payment flows are tested end-to-end
55. **Abuse Detection Testing**: How abuse detection and takedown flows are tested
56. **Ad Network Testing**: How ad networks and adblock recovery are tested
57. **Migration Testing**: How migration from Telegram to owned storage is tested
58. **Performance Testing**: How chunked upload/download performance is tested on VN networks
59. **Load Testing**: How system handles 100 users × 500 GB/month load scenario
60. **Failover Testing**: How overflow to Backblaze B2 is tested

## Key Gaps

### 1. Storage Service Architecture Gap (Critical)
**Impact**: HIGH - Entire storage backend abstraction needs replacement  
**Missing**: No replacement for TelegramService/StorageChannel abstraction for owned disk storage  
**Required**: New storage service interface, chunk storage abstraction, manifest storage design, overflow architecture  
**Current Spec**: TelegramService wraps WTelegram.Client with channel resolution  
**PRD Gap**: PRD removes Telegram but doesn't specify replacement architecture

### 2. Chunked Storage Implementation Gap (Critical)
**Impact**: HIGH - Core delivery mechanism lacks implementation specification  
**Missing**: Chunk upload flow, commit protocols, transaction boundaries, resume mechanism  
**Required**: Handler patterns for chunk upload/download, Wolverine transaction mapping, integrity verification  
**Current Spec**: Endpoints dispatch via IMessageBus → Handlers → Telegram  
**PRD Gap**: PRD mentions chunking but doesn't specify implementation architecture

### 3. Data Model Migration Gap (High)
**Impact**: HIGH - Database schema needs complete overhaul for chunked storage  
**Missing**: Delivery/Manifest entities, Chunk table, download tracking, payment schema, abuse reporting  
**Required**: New EF entities, migration strategy, indexes, foreign keys, cleanup jobs  
**Current Spec**: Files/Folders tables with Telegram metadata  
**PRD Gap**: PRD introduces Deliveries, Manifests, Chunks but no database schema

### 4. Payment Integration Architecture Gap (High)
**Impact**: MEDIUM - Payment flow needs architectural specification  
**Missing**: VietQR integration, payment webhooks, payment gating vs fragment key interaction  
**Required**: Payment service interface, webhook handling, unlock mechanism, schema  
**Current Spec**: Stripe integration with webhook confirmation  
**PRD Gap**: PRD replaces Stripe with VietQR but no integration architecture

### 5. Ad Network Integration Gap (Medium)
**Impact**: MEDIUM - Free-tier monetization lacks implementation details  
**Missing**: Ad network integration, impression tracking, adblock detection, recovery mechanism  
**Required**: Ad injection architecture, network API integration, impression tracking  
**Current Spec**: No ad system  
**PRD Gap**: PRD introduces ad-supported free tier but no technical implementation

### 6. Configuration & Operations Gap (Medium)
**Impact**: MEDIUM - Deployment and configuration unclear  
**Missing**: TeleCloudOptions redesign, Plan limits configuration, chunk size defaults, monitoring  
**Required**: Configuration schema, health checks, deployment architecture, operational procedures  
**Current Spec**: Telegram credentials, session path, channel ID  
**PRD Gap**: No specification for owned storage configuration or operations

### 7. Feature Scope Clarification Gap (Medium)
**Impact**: MEDIUM - Unclear which legacy features are kept or removed  
**Missing**: File request links, OAuth providers beyond Google, folders vs deliveries  
**Required**: Explicit feature inclusion/exclusion list, migration path for kept features  
**Current Spec**: 10 endpoints (Files + Folders), 4 OAuth providers  
**PRD Gap**: PRD doesn't mention file requests, folders, or non-Google OAuth status
