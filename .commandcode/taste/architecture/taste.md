# architecture
- Endpoints should be thin delegates that only dispatch commands/queries via IMessageBus — no business logic in endpoint methods. Confidence: 0.85
- Use Wolverine command/query DTOs directly as endpoint parameters instead of extracting individual fields (e.g., `ListFilesQuery query` instead of `string folder, string search, int limit, int offset`). Confidence: 0.75
- Avoid record inheritance for Wolverine query/command DTOs — Wolverine only picks properties directly on the concrete type, not inherited ones. Declare all properties flat on the concrete record. Confidence: 0.70
- List/query endpoints must use HTTP GET, not POST — WolverineGet for reading data. Confidence: 0.85
- Use FastEndpoint instead of Wolverine.Http for HTTP endpoint definitions. Confidence: 0.70
- The server must have a global exception handler middleware, especially when individual handlers avoid try-catch for expected error paths — unexpected exceptions still need a safety net. Confidence: 0.50
- Wolverine cannot inject HttpContext directly into message handlers. Use IHttpContextAccessor instead and register AddHttpContextAccessor() in DI. Confidence: 0.70
