# Architecture — TeleCloud

## Tech Stack

| Layer | Technology | Version |
|---|---|---|
| Runtime | .NET 10 (ASP.NET Core) | 10.x |
| HTTP Framework | FastEndpoints + FastEndpoints.Swagger | 8.2.0 |
| Command Bus | WolverineFx (inbox/outbox via PostgreSQL) | 5.x |
| ORM / Transactions | EF Core 10 + WolverineFx.EntityFrameworkCore | 10.x |
| Database | PostgreSQL (Neon.tech) via Npgsql | 10.0.0 |
| Message Persistence | WolverineFx.Postgresql (schema: `telecloud`) | 5.x |
| Telegram Client | WTelegramClient | 4.x |
| Result Pattern | LightResults | 10.0.4 |
| Object Mapping | Riok.Mapperly (all entity→DTO) | 4.3.1 |
| API Docs | Swagger UI (Swashbuckle + FastEndpoints.Swagger) | — |
| Source Gen | TeleCloud.Querying.Generators (Roslyn incremental) | internal |

## Data Flow

```
HTTP Request
  │
  ▼
FastEndpoints Endpoint (thin delegate)
  │  Resolve<IMessageBus>().InvokeAsync<Result<T>>(command/query)
  ▼
Wolverine IMessageBus (mediator)
  │  AutoApplyTransactions → EF Core transaction
  │  PersistMessagesWithPostgresql → inbox/outbox
  ▼
Handler (primary constructor DI)
  │  TelegramService ──► WTelegram.Client ──► Telegram MTProto API
  │  TeleCloudDbContext ──► PostgreSQL
  ▼
LightResults.Result<T>
  │
  ▼
Endpoint maps Result → HTTP response
  Success → Ok/NoContent
  Failure → ThrowError (mapped by error type to status code)
```

## Audit Fields

**`CreatedAt` and `UpdatedAt` are managed exclusively by `AuditableEntityInterceptor`** — never set manually in code.

- **Interceptor:** `QuickShare.Server.Data.AuditableEntityInterceptor` (EF Core `SaveChangesInterceptor`)
- **Registration:** `AddDbContext<AppDbContext>(...) → AddInterceptors(new AuditableEntityInterceptor())`
- **Behavior:**
  - `EntityState.Added` → sets `CreatedAt = DateTime.UtcNow` and `UpdatedAt = DateTime.UtcNow`
  - `EntityState.Modified` → sets `UpdatedAt = DateTime.UtcNow`
- **Compatibility:** Works with any entity that has a `CreatedAt` or `UpdatedAt` property (via `entry.Metadata.FindProperty`). No base-class requirement — handles `BaseAuditEntity` descendants and standalone entities like `Manifest` equally.
- **Base:** `BaseAuditEntity` (`QuickShare.Shared.Models`) provides `Id`, `CreatedAt` (with `= DateTime.UtcNow` default), and `UpdatedAt?`.

## Object Mapping (Mapperly)

**All entity→DTO mappings use Riok.Mapperly.** Mappers live in `Features/<Feature>/Shared/<Entity>Mapper.cs`.

```csharp
[Mapper]
public static partial class UserMapper
{
    [MapperIgnoreSource(nameof(User.UpdatedAt))]
    public static partial ProfileResponse ToUserProfileResponse(this User source);
}
```

- **Pattern:** `static partial class` with `[Mapper]` attribute, `partial` method returning the response DTO.
- **Exceptions:**
  - **LINQ `.Select()` projections** with server-side subqueries (e.g., `db.Files.Count(...)`) or computed expression fields — construct DTO inline. EF Core cannot translate Mapperly-generated methods to SQL.
  - **Simple value-object responses** (`new SomeResponse(sessionId, chunkSize)`) that don't map from a single entity.

## Service Layer

### TelegramService (Singleton)

Wraps `WTelegram.Client`. Lazily initializes on first request. Thread-safe with `SemaphoreSlim` double-check locking.

- **Login:** `LoginUserIfNeeded()` — uses config lambda for auth parameters. If `WTelegram.session` file exists, skips login entirely. If not, prompts console for verification code (one-time setup).
- **Channel resolution:** `TelegramInitializer.ResolveChannelAsync()` — tries numeric channel ID first (`Messages_GetAllChats`), falls back to username resolution (`Contacts_ResolveUsername`).
- Exposes `Client` property and `GetChannelAsync()` → `StorageChannel`.

### StorageChannel

```csharp
public sealed class StorageChannel
{
    public InputPeer Peer { get; init; }
    public long ChannelId { get; init; }
    public long ChannelAccessHash { get; init; }
    public InputChannel ToInputChannel();
}
```

### Error Handling

- **Expected errors:** `LightResults.Result` with typed errors (`NotFoundError`, `BadRequestError`, `ConflictError`, `ValidationError`).
- **Unhandled exceptions:** `GlobalExceptionHandler` (IExceptionHandler) → RFC 7807 ProblemDetails 500 with traceId.

### Configuration (`TeleCloudOptions`)

```csharp
public sealed class TeleCloudOptions
{
    string ApiId, ApiHash, PhoneNumber, Password;
    string SessionPathname = "WTelegram.session";
    string StorageChannel = "";
}
```

Bound from `TeleCloud` config section via `IOptions<T>` pattern.

## Feature Folders (Vertical Slice)

```
Features/
├── Files/
│   ├── Shared/
│   │   ├── FileMapper.cs             # Mapperly source-gen mapper
│   │   └── Errors.cs                 # NotFoundError, BadRequestError, etc.
│   ├── Upload/
│   │   ├── UploadEndpoint.cs         # POST /api/files (multipart)
│   │   ├── UploadFileRequest.cs      # form DTO (IFormFile + FolderId)
│   │   └── UploadFileHandler.cs      # UploadFileCommand + handler
│   ├── List/
│   │   ├── ListEndpoint.cs           # GET /api/files
│   │   └── ListFilesHandler.cs       # ListFilesQuery + handler → Result<PagedResult>
│   ├── GetInfo/
│   │   ├── GetInfoEndpoint.cs        # GET /api/files/{id}
│   │   └── GetFileInfoHandler.cs     # GetFileInfoQuery + handler → Result<FileItemResponse>
│   ├── Download/
│   │   ├── DownloadEndpoint.cs       # GET /api/files/{id}/download
│   │   └── DownloadFileHandler.cs    # DownloadFileQuery + handler (streams via IHttpContextAccessor)
│   └── Delete/
│       ├── DeleteEndpoint.cs         # DELETE /api/files/{id}
│       └── DeleteFileHandler.cs      # DeleteFileCommand + handler → Result
└── Folders/
    ├── Shared/
    │   └── FolderMapper.cs           # Riok.Mapperly source-gen mapper
    ├── Create/
    │   ├── CreateFolderEndpoint.cs   # POST /api/folders
    │   └── CreateFolderHandler.cs    # CreateFolderCommand + handler
    ├── List/
    │   ├── ListFoldersEndpoint.cs    # GET /api/folders
    │   └── ListFoldersHandler.cs     # ListFoldersQuery + handler → Result<PagedResult>
    ├── GetInfo/
    │   ├── GetFolderInfoEndpoint.cs  # GET /api/folders/{id}
    │   └── GetFolderInfoHandler.cs   # GetFolderInfoQuery + handler → Result<FolderItemResponse>
    ├── Update/
    │   ├── UpdateFolderEndpoint.cs   # PUT /api/folders/{id}
    │   └── UpdateFolderHandler.cs    # UpdateFolderCommand + handler (ExecuteUpdateAsync)
    └── Delete/
        ├── DeleteFolderEndpoint.cs   # DELETE /api/folders/{id}
        └── DeleteFolderHandler.cs    # DeleteFolderCommand + handler (must be empty)
```

## Endpoint Map (10 endpoints)

| Method | Route | Handler | Input | Returns |
|---|---|---|---|---|
| GET | `/api/files` | `ListFilesHandler` | `ListFilesQuery` | `Result<PagedResult<FileItemResponse>>` |
| GET | `/api/files/{id}` | `GetFileInfoHandler` | `GetFileInfoQuery` | `Result<FileItemResponse>` |
| GET | `/api/files/{id}/download` | `DownloadFileHandler` | `DownloadFileQuery` | Stream (200/206) |
| POST | `/api/files` | `UploadFileHandler` | `UploadFileCommand` | `Result<FileItemResponse>` |
| DELETE | `/api/files/{id}` | `DeleteFileHandler` | `DeleteFileCommand` | `Result` (204/404) |
| GET | `/api/folders` | `ListFoldersHandler` | `ListFoldersQuery` | `Result<PagedResult<FolderItemResponse>>` |
| GET | `/api/folders/{id}` | `GetFolderInfoHandler` | `GetFolderInfoQuery` | `Result<FolderItemResponse>` |
| POST | `/api/folders` | `CreateFolderHandler` | `CreateFolderCommand` | `Result<FolderItemResponse>` |
| PUT | `/api/folders/{id}` | `UpdateFolderHandler` | `UpdateFolderCommand` | `Result<FolderItemResponse>` |
| DELETE | `/api/folders/{id}` | `DeleteFolderHandler` | `DeleteFolderCommand` | `Result` (204/404/409) |

### List query params (shared across list endpoints)

| Param | Type | Description |
|---|---|---|
| `sort` | `string[]` (repeated) | Sort field with direction: `"field asc"` or `"field desc"`. Default: `"createdAt"` ascending. Multi-column: `?sort=filename&sort=createdAt desc` |
| `page` | `int` | Page number, 1-based. Default: 1 |
| `pageSize` | `int` | Items per page. Default: 50 |
| `folderId` | `Guid?` | Filter by folder (files list only) |
| `search` | `string?` | Search filename + folder path (files list only) |

## Data Model

```
folders                         files
┌──────────────────┐           ┌────────────────────────┐
│ Id (UUID PK)     │◄──────────│ Id (UUID PK)           │
│ Path (UNIQUE)    │  FK       │ Filename               │
│ CreatedAt        │           │ ContentType            │
└──────────────────┘           │ Size                   │
                               │ ChannelId              │
                               │ MessageId              │
                               │ AccessHash             │
                               │ FileReference (byte[]) │
                               │ DocumentId             │
                               │ FolderId (FK → folders)│
                               │ Tags (text[])          │
                               │ CreatedAt              │
                               └────────────────────────┘
```

Indexes: `IX_files_FolderId`, `IX_files_Filename`, `IX_files_CreatedAt DESC`, `IX_folders_Path UNIQUE`.

FK: `ON DELETE RESTRICT` — folders with files cannot be deleted.

## Sort System (`TeleCloud.Querying` + Generator)

1. Entities marked `[DynamicSort]` (FileMetadata, Folder) trigger the `SortExtensionsGenerator` Roslyn source generator.
2. Generator produces `{Type}SortExtensions` with:
   - `HashSet<string> SortableProperties` — all public properties.
   - `TryValidateSorts(sorts, out error)` — pre-flight validation.
   - `OrderBy(IQueryable, SortDescriptor)` / `ThenBy` — single-property sort.
   - `OrderBy(IQueryable, IEnumerable<SortDescriptor>)` — multi-column sort.
3. `SortDescriptor.Parse(string[]?, defaultProperty)` parses repeated `?sort=field dir` query params.
4. `QueryableExtensions.ToPagedResultAsync(source, page, pageSize, selector)` handles pagination with projection.

## Migration History

| Migration | Description |
|---|---|
| `InitialCreate` | `files` table with string `Folder` column |
| `AddFoldersTable` | Creates `folders` table, adds `folder_id` FK column, migrates string folder data to FK via SQL, drops old `Folder` column |

## Wolverine Configuration

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(connectionString, "telecloud");
    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.AutoApplyTransactions();
});
```

## Project Layout

```
src/
├── TeleCloud.Server/
│   ├── Program.cs                        # DI, middleware, migration
│   ├── GlobalUsings.cs
│   ├── appsettings.json
│   ├── Data/
│   │   ├── TeleCloudDbContext.cs
│   │   ├── FileMetadata.cs
│   │   └── Folder.cs
│   ├── Features/Files/...                # 5 file endpoints
│   ├── Features/Folders/...              # 5 folder endpoints
│   ├── Infrastructure/
│   │   ├── GlobalExceptionHandler.cs
│   │   └── ResultExtensions.cs
│   ├── Migrations/...
│   └── Services/
│       ├── TeleCloudOptions.cs
│       ├── TelegramService.cs
│       ├── TelegramInitializer.cs
│       └── StorageChannel.cs
├── TeleCloud.Querying/
│   ├── PagedResult.cs
│   ├── QueryableExtensions.cs
│   ├── SortDescriptor.cs
│   └── Attributes/DynamicSortAttribute.cs
└── TeleCloud.Querying.Generators/
    └── SortExtensionsGenerator.cs        # Roslyn incremental source gen
```
