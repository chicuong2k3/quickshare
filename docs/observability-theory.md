# TeleCloud Observability — Theory & Architecture

> Distilled from team discussion, 2026-08-03.
> Stack decision: **Prometheus + Grafana** (metrics), **Seq** (logs), **OpenTelemetry** (tracing — deferred).

---

## 1. Core Concepts

### Telemetry
Raw data collected from a running system. Three signal types:

| Signal | What it is | TeleCloud example |
|---|---|---|
| **Logs** | Timestamped text records of discrete events | `"R2 InitMultipartUpload: UploadId=abc, Key=files/..."` |
| **Metrics** | Numeric measurements over time (counters, gauges, histograms) | `http_request_duration_seconds{method="POST",handler="start-r2"}` |
| **Traces** | Span trees showing a request's path through the system | See §2 below |

Telemetry is the *raw material*, not the end goal.

### Observability
The *ability* to understand internal system state from external telemetry. The test: can you answer a question you *didn't anticipate when you wrote the code* without deploying new instrumentation?

Observability requires sufficient richness (cardinality, dimensionality) across all three signals — not just having a dashboard.

### Tracing
A specific telemetry signal: follows one request through its entire lifecycle, decomposed into spans.

**Distributed tracing** = tracing across multiple services/machines. TeleCloud is single-process; tracing here is in-process (handler → DB → R2 API).

### Correlation
The ability to group all telemetry belonging to one logical unit:
- **traceId** — groups all spans of one HTTP request chain (automatic — W3C Trace Context)
- **Baggage** — key-value context that propagates across *different* traces, e.g. `deliveryId=abc` linking `start-r2` → chunk uploads → `commit-r2` into one business flow
- **correlationId** (traditional) — a manually generated ID stamped on every log line. **Obsolete if traceId is used.** Baggage covers the cross-trace case.

### Logs vs Traces

```
LOGS (flat, per-event)
─────────────────────────
[10:23:01.234] start-r2 started
[10:23:01.456] R2 InitMultipartUpload
[10:23:01.567] UploadSession saved
[10:23:01.568] start-r2 200 OK (334ms)

→ What happened at each point in time.
→ No structure linking events together.

TRACING (tree, per-request)
─────────────────────────
traceId: 7f3a8b2c
├─ span: start-r2 handler ........ 334ms
│   ├─ span: GetUserAsync(db) ..... 12ms
│   ├─ span: R2 InitMultipart .... 201ms
│   └─ span: SaveChangesAsync ..... 45ms

→ Complete journey of ONE request.
→ See instantly which step is slow.
```

Logs and traces are complementary: logs answer "what happened?", traces answer "how did this request flow?".

---

## 2. Key Identifiers

```
┌────────────────────────────────────────────────────────┐
│ traceId: "7f3a8b2c"  ← ONE HTTP request              │
│                                                        │
│ spanId: "a1"           spanId: "b2"    spanId: "c3"   │
│ ┌────────────────┐   ┌─────────────┐  ┌────────────┐  │
│ │ start-r2       │──▶│ R2 Init     │─▶│ DB Save    │  │
│ │ (root span)    │   │ (child)     │  │ (child)    │  │
│ └────────────────┘   └─────────────┘  └────────────┘  │
│                                                        │
│ Baggage: deliveryId=abc-123  ← cross-trace business ID │
└────────────────────────────────────────────────────────┘
```

| ID | Scope | Created by | Purpose |
|---|---|---|---|
| **traceId** | One HTTP request chain | OpenTelemetry SDK (auto) | Group all spans of one request |
| **spanId** | One operation within a trace | OpenTelemetry SDK (auto) | Identify a single step; has `parentSpanId` |
| **Baggage** | Cross-trace business flow | Application code (manual) | Link multiple requests into one user action |
| correlationId | — | — | **Legacy.** Use traceId + Baggage instead |

---

## 3. TeleCloud Stack

| Concern | Tool | Why |
|---|---|---|
| **Logs** | Serilog → Seq | Already installed; rich structured log query; free tier sufficient |
| **Metrics** | prometheus-net → Prometheus → Grafana | Lightweight (< 512 MB RAM); .NET native; PromQL for ad-hoc queries |
| **Tracing** | OpenTelemetry (deferred) | Overkill for single-process app; add when complexity warrants |
| **Health** | ASP.NET Core Health Checks | Built-in; expose Npgsql + R2 connectivity as `/health` |
| **Load Test** | k6 | Simpler than NBomber for HTTP-only; mature ecosystem |

### Why NOT ELK
- Elasticsearch is resource-heavy (JVM, RAM)
- Overkill for single-server .NET app
- Seq covers logging, Prometheus covers metrics — no gap ELK fills

### Why NOT Loki (for now)
- Seq already serves as structured log store with excellent query UI
- Adding Loki duplicates log storage without benefit
- If Seq outgrown, Grafana Loki integrates with existing Grafana

---

## 4. Cross-Request Business Flows (Baggage)

The upload flow spans multiple HTTP requests:
```
POST /api/uploads/start-r2     → traceId: AAA
PUT chunks to R2 (×N)          → traceId: BBB, CCC, DDD
POST /api/uploads/commit-r2    → traceId: EEE
```

Baggage links them via `deliveryId`:
```csharp
// Server sets baggage on start-r2 response
Response.Headers["baggage"] = $"deliveryId={deliveryId}";

// Client echoes it back on subsequent requests
fetch("/api/uploads/commit-r2", {
    headers: { "baggage": "deliveryId=abc-123" }
});

// Server reads it
var deliveryId = Baggage.Current.GetBaggage("deliveryId");
```

Grep `deliveryId=abc-123` in Seq → every log line from the entire upload flow, across all 5 traces.

---

## 5. Performance Testing

| Tool | Use case | Notes |
|---|---|---|
| **k6** | HTTP load test (primary) | JavaScript; CI-ready; official GitHub Actions |
| **NBomber** | Complex scenarios (future) | C#/F#; database-level testing; deferred until needed |

### k6 Test Plan
- Simulate 10 concurrent uploads
- Steps: start-r2 → upload chunks (simulated) → commit-r2
- Measure: p50/p95/p99 latency, error rate, R2 dependency failures
- Run in CI on PR, locally on demand

---

## 6. Terminology Quick Reference

| Term | One-line definition |
|---|---|
| Telemetry | Raw data from system: logs, metrics, traces |
| Observability | Ability to answer new questions from telemetry |
| Logging | Timestamped event records |
| Tracing | Request journey as a span tree |
| Metric | Numeric measurement over time |
| traceId | ID linking all spans of one request |
| spanId | ID of one operation within a trace |
| Baggage | User-defined key-values propagating across traces |
| Health Check | Readiness/liveness probe endpoint |
| RED method | Rate, Errors, Duration — the 3 key service metrics |
