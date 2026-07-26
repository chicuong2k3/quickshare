# Task 1: P2PConnectionManager — P2PReceiverConnection + Receivers dictionary

**Files:**
- Modify: `src/QuickShare.Server/Middleware/P2PConnectionManager.cs`

**Interfaces:**
- Consumes: `P2PRoom` (existing class)
- Produces: `P2PReceiverConnection` record, updated `P2PRoom.Receivers`, updated `ReceiverConnected`/`IsComplete` properties

### Step 1: Add P2PReceiverConnection record before P2PRoom

```csharp
public sealed record P2PReceiverConnection
{
    public string Id { get; init; } = string.Empty;
    public WebSocket Socket { get; init; } = null!;
    public DateTime JoinedAt { get; init; } = DateTime.UtcNow;
}
```

### Step 2: Replace `Receiver` property with `Receivers` dictionary

```csharp
// Before
public WebSocket? Receiver { get; set; }

// After
public ConcurrentDictionary<string, P2PReceiverConnection> Receivers { get; init; } = new(StringComparer.Ordinal);
```

### Step 3: Update `ReceiverConnected` and `IsComplete`

```csharp
// Before
public bool ReceiverConnected => Receiver?.State == WebSocketState.Open;
public bool IsComplete => SenderConnected && ReceiverConnected;

// After
public bool ReceiverConnected => Receivers.Values.Any(r => r.Socket.State == WebSocketState.Open);
public bool IsComplete => SenderConnected && Receivers.Count > 0;
```

### Step 4: Add `using System.Linq;` to imports

Add at top of file.

### Step 5: Build check

Run: `dotnet build src/QuickShare.Server/QuickShare.Server.csproj --no-restore`
Expected: 0 errors

### Step 6: Commit

```bash
git add src/QuickShare.Server/Middleware/P2PConnectionManager.cs
git commit -m "feat(p2p): change P2PRoom.Receiver to Receivers dictionary"
```
