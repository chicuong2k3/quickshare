# C# Conventions — TeleCloud

> Living style guide. Append new rules as patterns emerge during development.

## Architecture

1. **Options pattern for config** — Use `IOptions<T>` + `BindConfiguration()` + `ValidateDataAnnotations()` + `ValidateOnStart()`. Never scatter `IConfiguration["key"]` across the codebase. Config classes carry `[Required]` annotations; app fails at startup if config is missing.

2. **Endpoints are thin delegates** — Endpoints extract transport data (form fields, headers, query params) and dispatch commands/queries via `IMessageBus`. No business logic, no range parsing, no null checking, no `IResult` wrapping — those live in handlers.

3. **Use command/query DTOs directly as endpoint parameters** — Instead of extracting individual fields (`string folder, string search, int limit, int offset`), accept `ListFilesQuery query` and let Wolverine auto-bind from route/query string.

4. **Handler pattern with Wolverine** — One handler per operation. Handlers inject dependencies directly (`Client`, `StorageChannel`); no intermediate service layer.

5. **Wolverine inbox/outbox** — `PersistMessagesWithPostgresql()` + `AutoApplyTransactions()` ensures messages are durably stored before execution with automatic retry on transient failures.

6. **Result pattern with LightResults** — Handlers return `Result` or `Result<T>` instead of null/bool/exception. Endpoints map `result.IsSuccess(out var value)` to HTTP responses (Ok/NotFound). Custom errors extend `Error`.

## Project Structure

7. **Vertical slice feature folders** — Group each feature (Upload, List, GetInfo, Download, Delete) into its own folder under `Features/`. Inline command/query/response DTOs into the handler file. Keep endpoint separate in the same folder. Open the folder → see the entire flow.

8. **RESTful routes** — `GET /resource/{id}` returns metadata, `GET /resource/{id}/download` streams the file. No `/info` suffix.

## C# Language

9. **Use primary constructors** — `UploadFileHandler(Client client, StorageChannel channel)` over explicit constructor + `private readonly` fields.

10. **Check GlobalUsings.cs before adding using directives** — Do not add redundant usings already covered by global imports (`TeleCloud.Server.Data`, `TeleCloud.Server.Services`, `Wolverine`, `Wolverine.Http`, `Microsoft.EntityFrameworkCore`).
