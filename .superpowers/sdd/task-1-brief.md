### Task 1: Data model — `UploadSession.SenderEmail` + `Delivery` sender-email fields + migration

**Files:**
- Modify: `src/QuickShare/Data/Entities/UploadSession.cs`
- Modify: `src/QuickShare/Data/Entities/Delivery.cs`
- Create: `src/QuickShare/Data/Migrations/20260815XXXXXX_SenderEmailCapture.cs` (+ `.Designer.cs`, `AppDbContextModelSnapshot.cs` changes — generated)

**Interfaces:**
- Consumes: nothing (entity-only task).
- Produces: `UploadSession.SenderEmail` (string?, nullable), `Delivery.SenderEmail` (string?, nullable), `Delivery.SenderEmailConfirmed` (bool), `Delivery.SenderEmailConsent` (bool). Used by Tasks 2, 4, 5, 6.

- [ ] **Step 1: Add `SenderEmail` to `UploadSession`**

```csharp
namespace QuickShare.Data.Entities;

[DynamicSort]
public record UploadSession : BaseAuditEntity
{
    public Guid? SenderId { get; init; }
    public string? SenderEmail { get; init; }
    public string FileName { get; init; } = string.Empty;
    // ...rest unchanged
}
```

- [ ] **Step 2: Add sender-email fields to `Delivery`**

```csharp
[DynamicSort]
public record Delivery : BaseAuditEntity
{
    public Guid? SenderId { get; init; }
    public string? SenderEmail { get; set; }
    public bool SenderEmailConfirmed { get; set; }
    public bool SenderEmailConsent { get; set; }
    // ...rest unchanged
}
```

(`set`, not `init`, because Tasks 5/6 mutate `SenderEmailConfirmed`/`SenderEmailConsent` after creation.)

- [ ] **Step 3: Generate the migration**

Run from repo root:

```powershell
rtk dotnet ef migrations add SenderEmailCapture --project src/QuickShare
```

Expected: new files under `src/QuickShare/Data/Migrations/` with `AddColumn("SenderEmail", "text", nullable: true)` on `deliveries` and `upload_sessions`, and `AddColumn<bool>("SenderEmailConfirmed", ...)` + `AddColumn<bool>("SenderEmailConsent", ...)` on `deliveries`.

- [ ] **Step 4: Build to verify**

```powershell
rtk dotnet build src/QuickShare/QuickShare.csproj --no-restore
```

Expected: build succeeds, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/QuickShare/Data/Entities/UploadSession.cs src/QuickShare/Data/Entities/Delivery.cs src/QuickShare/Data/Migrations/
git commit -m "feat(data): sender email fields on UploadSession and Delivery (issue #76)"
```

---

