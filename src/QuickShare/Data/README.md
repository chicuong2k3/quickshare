# Server Data

This folder contains the DbContext (`AppDbContext`), entities, and database-related infrastructure.

Entities live in `src/QuickShare/Data/Entities/` (namespace `QuickShare.Data.Entities`): `User`, `UserLogin`, `UserUsage`, `Delivery`, `UploadSession`, `Chunk`, `Manifest`, `Folder`, `FileMetadata`, `DownloadTracking`, `TempShare`, `PaymentIntent`, `PaymentEvent`, `Subscription`, plus shared DTOs like `ProfileResponse`.

The global usings (`global using QuickShare.Data;` and `global using QuickShare.Data.Entities;`) already cover both namespaces, so no explicit import is needed in most files.
