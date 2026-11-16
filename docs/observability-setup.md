# Observability Setup Guide — Grafana (Loki + Prometheus + Tempo)

> TeleCloud metrics endpoint: `http://<server>:9001/metrics`
> Health check endpoint: `http://<server>:9001/health`
> Logs: Serilog → Loki · Traces: OpenTelemetry → Tempo · Pipeline: Grafana Alloy

---

## 1. Quick Answer: Connection Strings?

**Không cần connection string nào giữa Prometheus và QuickShare.** Prometheus scrape metrics qua HTTP — giống như curl vào `/metrics`. Không cần database, không cần auth (trừ phi bạn thêm reverse proxy auth).

Grafana cũng không cần connection string — nó query Prometheus qua HTTP API.

Loki và Tempo cũng vậy — Serilog đẩy log qua HTTP POST tới `/loki/api/v1/push`, OTel đẩy trace qua OTLP (gRPC/HTTP) tới Alloy. Không connection string, không database chung.

```
QuickShare ── Serilog (direct push) ───────────────────▶ Loki :3100
QuickShare ── OpenTelemetry (OTLP) ──▶ Alloy :4318 ───▶ Tempo :3200
QuickShare ── Prometheus scrape /metrics ──────────────▶ Prometheus :9090
                                                              │
                                          Grafana :3000 ◀────┘ (queries all three)
```

---

## 2. Prometheus Configuration

### 2.1 Install (if not already running)

```bash
# Docker — recommended
docker run -d --name prometheus \
  -p 9090:9090 \
  -v /path/to/prometheus.yml:/etc/prometheus/prometheus.yml \
  -v prometheus_data:/prometheus \
  prom/prometheus:latest

# Or bare-metal
# Download from https://prometheus.io/download/
```

### 2.2 `prometheus.yml`

```yaml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  # ── QuickShare app metrics ──
  - job_name: 'quickshare'
    scrape_interval: 15s
    metrics_path: '/metrics'
    static_configs:
      - targets: ['localhost:9001']
        labels:
          service: 'quickshare'
          env: 'production'

  # ── Prometheus self-monitoring ──
  - job_name: 'prometheus'
    static_configs:
      - targets: ['localhost:9090']
```

**Không cần authentication** — `/metrics` endpoint của QuickShare là public (nội bộ). Nếu muốn bảo vệ, thêm reverse proxy (nginx/Caddy) phía trước với basic auth hoặc IP whitelist.

### 2.3 Verify Scrape Working

```bash
# Check Prometheus targets page
curl http://localhost:9090/targets

# Query directly
curl http://localhost:9090/api/v1/query?query=up{job="quickshare"}

# Should return: up{job="quickshare"} => 1
```

---

## 3. Grafana Configuration

### 3.1 Install (if not already running)

```bash
docker run -d --name grafana \
  -p 3000:3000 \
  -v grafana_data:/var/lib/grafana \
  grafana/grafana:latest

# Default login: admin / admin
# Change password on first login
```

### 3.2 Add Prometheus Data Source

1. Login Grafana → **Connections → Data Sources** (hoặc `/datasources`)
2. Click **+ Add new data source** → chọn **Prometheus**
3. Điền:

| Field | Value |
|---|---|
| Name | `Prometheus` |
| URL | `http://localhost:9090` |

4. Scroll xuống → **Save & test**. Phải thấy "Successfully queried the Prometheus API."

**Lưu ý:** Nếu Prometheus và Grafana chạy cùng Docker, dùng `http://prometheus:9090` (container name) hoặc `http://host.docker.internal:9090`.

---

## 4. Grafana Dashboard

### 4.1 Import Dashboard JSON

Vào **Dashboards → Import** → paste JSON bên dưới.

Dashboard này cover RED method (Rate, Errors, Duration) + health + system:

<details>
<summary>Click để mở dashboard JSON (copy toàn bộ)</summary>

```json
{
  "title": "TeleCloud QuickShare",
  "tags": ["quickshare", "dotnet"],
  "refresh": "10s",
  "panels": [
    {
      "id": 1,
      "title": "Request Rate",
      "type": "graph",
      "gridPos": { "h": 8, "w": 12, "x": 0, "y": 0 },
      "targets": [
        {
          "expr": "rate(http_requests_received_total{job=\"quickshare\"}[1m])",
          "legendFormat": "{{method}} {{code}}"
        }
      ],
      "yaxes": [{ "format": "reqps" }]
    },
    {
      "id": 2,
      "title": "Error Rate (5xx)",
      "type": "graph",
      "gridPos": { "h": 8, "w": 12, "x": 12, "y": 0 },
      "targets": [
        {
          "expr": "rate(http_requests_received_total{job=\"quickshare\",code=~\"5..\"}[1m])",
          "legendFormat": "{{method}} {{code}}"
        }
      ],
      "thresholds": [{ "value": 1, "color": "red" }]
    },
    {
      "id": 3,
      "title": "Request Duration (p50, p95, p99)",
      "type": "graph",
      "gridPos": { "h": 8, "w": 24, "x": 0, "y": 8 },
      "targets": [
        {
          "expr": "histogram_quantile(0.50, rate(http_request_duration_seconds_bucket{job=\"quickshare\"}[1m]))",
          "legendFormat": "p50"
        },
        {
          "expr": "histogram_quantile(0.95, rate(http_request_duration_seconds_bucket{job=\"quickshare\"}[1m]))",
          "legendFormat": "p95"
        },
        {
          "expr": "histogram_quantile(0.99, rate(http_request_duration_seconds_bucket{job=\"quickshare\"}[1m]))",
          "legendFormat": "p99"
        }
      ],
      "yaxes": [{ "format": "s" }]
    },
    {
      "id": 4,
      "title": "Health Status",
      "type": "stat",
      "gridPos": { "h": 4, "w": 6, "x": 0, "y": 16 },
      "targets": [
        {
          "expr": "up{job=\"quickshare\"}",
          "legendFormat": "QuickShare"
        }
      ],
      "mappings": [
        { "type": "value", "value": "1", "text": "UP" },
        { "type": "value", "value": "0", "text": "DOWN" }
      ],
      "thresholds": [
        { "value": 0, "color": "red" },
        { "value": 1, "color": "green" }
      ]
    },
    {
      "id": 5,
      "title": "Active HTTP Requests",
      "type": "stat",
      "gridPos": { "h": 4, "w": 6, "x": 6, "y": 16 },
      "targets": [
        {
          "expr": "sum(http_requests_in_progress{job=\"quickshare\"})"
        }
      ]
    },
    {
      "id": 6,
      "title": "Total Requests (24h)",
      "type": "stat",
      "gridPos": { "h": 4, "w": 6, "x": 12, "y": 16 },
      "targets": [
        {
          "expr": "sum(increase(http_requests_received_total{job=\"quickshare\"}[24h]))"
        }
      ]
    },
    {
      "id": 7,
      "title": "Avg Duration (1h)",
      "type": "stat",
      "gridPos": { "h": 4, "w": 6, "x": 18, "y": 16 },
      "targets": [
        {
          "expr": "rate(http_request_duration_seconds_sum{job=\"quickshare\"}[1h]) / rate(http_request_duration_seconds_count{job=\"quickshare\"}[1h])",
          "legendFormat": "avg"
        }
      ],
      "unit": "s"
    }
  ]
}
```

</details>

### 4.2 Alert Rules (Prometheus)

Thêm vào `prometheus.yml` hoặc file rules riêng:

```yaml
rule_files:
  - 'alerts.yml'
```

`alerts.yml`:

```yaml
groups:
  - name: quickshare
    rules:
      # App down > 1 phút
      - alert: QuickShareDown
        expr: up{job="quickshare"} == 0
        for: 1m
        labels:
          severity: critical
        annotations:
          summary: "QuickShare is down"
          description: "QuickShare has been unreachable for > 1 minute."

      # Error rate > 5% trong 5 phút
      - alert: HighErrorRate
        expr: |
          sum(rate(http_requests_received_total{job="quickshare",code=~"5.."}[5m]))
          /
          sum(rate(http_requests_received_total{job="quickshare"}[5m]))
          > 0.05
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High 5xx error rate ({{ $value | humanizePercentage }})"

      # p95 latency > 2s trong 5 phút (tính trên endpoint level)
      - alert: HighLatency
        expr: |
          histogram_quantile(0.95,
            rate(http_request_duration_seconds_bucket{job="quickshare"}[5m])
          ) > 2
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "p95 latency > 2s on {{ $labels.le }}"
```

---

## 5. Loki Configuration (Logs)

### 5.1 Serilog Sink

```bash
dotnet add package Serilog.Sinks.Grafana.Loki
```

Bỏ `Serilog.Sinks.Seq` khỏi `.csproj`. Trong `appsettings.json`, thay block `WriteTo`:

```json
"WriteTo": [
  { "Name": "Console" },
  {
    "Name": "GrafanaLoki",
    "Args": {
      "uri": "http://localhost:3100",
      "labels": [
        { "key": "service", "value": "quickshare" },
        { "key": "env", "value": "production" }
      ]
    }
  }
]
```

**Label discipline (quan trọng nhất):** chỉ `service`/`env`/`level` làm label. `deliveryId`, `userId`, `recipientEmail` để trong log line (structured fields) — query được qua logfmt / `|=`, không bị index. High-cardinality label là #1 nguyên nhân Loki chết chậm.

**Redaction:** strip `recipientEmail`, `accessKeySecret`, presigned URL ở sink (Serilog `IDestructuringPolicy`) trước khi vào Loki.

### 5.2 Loki Container

```bash
docker run -d --name loki \
  -p 3100:3100 \
  -v /path/to/loki-config.yml:/etc/loki/local-config.yaml \
  -v loki_data:/loki \
  grafana/loki:latest
```

Set retention tường minh trong `loki-config.yml` — đừng lấy default:

```yaml
limits_config:
  retention_period: 168h        # 7 days — tự chọn
  reject_old_samples: true
  reject_old_samples_max_age: 168h
```

### 5.3 Add Loki Data Source (Grafana)

1. Grafana → Connections → Data Sources → **Loki**
2. URL: `http://localhost:3100`
3. **Save & test**

---

## 6. OpenTelemetry + Tempo + Alloy (Traces)

Lý do bật tracing: **background job** (scheduled send — issue #69) chạy không có HTTP request nên không có traceId tự nhiên. Span trên job + `deliveryId` làm baggage cho phép truy vết job chạy lúc 3h sáng.

### 6.1 .NET — AddOpenTelemetry

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Instrumentation.Http
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

Trong `Program.cs`:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("QuickShare"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options => options.Endpoint = new Uri("http://localhost:4318")));
```

Trên background job, thêm root span (xem `src/QuickShare/Infrastructure/Telemetry/ActivitySources.cs` — shared source, và `DeliveryExpiryJob` là ví dụ sống):

```csharp
using var activity = ActivitySources.Source.StartActivity(ActivitySources.DeliveryExpiryActivity);
activity?.SetTag(ActivitySources.DeliveryCountTag, expired.Count);
```

Cross-request flow (upload → activate) liên kết span qua baggage:

```csharp
activity?.SetBaggage("deliveryId", delivery.Id.ToString());
```

### 6.2 Grafana Alloy (collector)

Alloy nhận OTLP rồi fan-out: traces → Tempo. Logs do Serilog đẩy thẳng Loki, metrics do Prometheus scrape trực tiếp — không qua Alloy.

`config.alloy`:

```alloy
otelcol.receiver.otlp "default" {
  grpc { endpoint = "0.0.0.0:4317" }
  http { endpoint = "0.0.0.0:4318" }

  output {
    traces = [otelcol.exporter.otlp.tempo.input]
  }
}

otelcol.exporter.otlp "tempo" {
  client { endpoint = "tempo:4317" }
}
```

### 6.3 Tempo Container

```bash
docker run -d --name tempo \
  -p 3200:3200 -p 4317:4317 \
  -v /path/to/tempo-config.yml:/etc/tempo.yaml \
  grafana/tempo:latest
```

`tempo-config.yml` tối thiểu (local storage):

```yaml
server:
  http_listen_port: 3200
distributor:
  receivers:
    otlp:
      protocols:
        grpc:
storage:
  trace:
    backend: local
    local:
      path: /tmp/tempo/traces
```

### 6.4 Add Tempo Data Source (Grafana)

1. Grafana → Data Sources → **Tempo**
2. URL: `http://localhost:3200`
3. **Save & test** — rồi trong Loki data source bật **Derived fields** trỏ `traceId` sang Tempo để click từ log → trace.

---

## 7. Docker Compose (All-in-One)

Chạy cả stack 1 lệnh:

```yaml
# docker-compose.observability.yml
version: '3.8'

services:
  prometheus:
    image: prom/prometheus:latest
    container_name: prometheus
    ports:
      - '9090:9090'
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
      - ./alerts.yml:/etc/prometheus/alerts.yml
      - prometheus_data:/prometheus
    command:
      - '--config.file=/etc/prometheus/prometheus.yml'
      - '--storage.tsdb.path=/prometheus'
      - '--storage.tsdb.retention.time=15d'

  loki:
    image: grafana/loki:latest
    container_name: loki
    ports:
      - '3100:3100'
    volumes:
      - ./loki-config.yml:/etc/loki/local-config.yaml
      - loki_data:/loki

  tempo:
    image: grafana/tempo:latest
    container_name: tempo
    ports:
      - '3200:3200'
      - '4317:4317'
    volumes:
      - ./tempo-config.yml:/etc/tempo.yaml
      - tempo_data:/tmp/tempo

  alloy:
    image: grafana/alloy:latest
    container_name: alloy
    ports:
      - '4318:4318'
    volumes:
      - ./config.alloy:/etc/alloy/config.alloy
    depends_on:
      - tempo

  grafana:
    image: grafana/grafana:latest
    container_name: grafana
    ports:
      - '3000:3000'
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=changeme
      - GF_USERS_ALLOW_SIGN_UP=false
    volumes:
      - grafana_data:/var/lib/grafana
    depends_on:
      - prometheus
      - loki
      - tempo

volumes:
  prometheus_data:
  grafana_data:
  loki_data:
  tempo_data:
```

```bash
docker compose -f docker-compose.observability.yml up -d
```

---

## 8. Verification Checklist

```bash
# 1. QuickShare metrics có expose?
curl http://localhost:9001/metrics | head -5
# → phải thấy: # HELP http_requests_received_total ...

# 2. Prometheus scrape được?
curl 'http://localhost:9090/api/v1/query?query=up{job="quickshare"}'
# → phải thấy: "value":[timestamp,"1"]

# 3. Loki nhận log?
curl -G http://localhost:3100/loki/api/v1/query --data-urlencode 'query={service="quickshare"}'
# → phải thấy dòng log gần nhất

# 4. Tempo nhận trace?
curl http://localhost:3200/ready
# → "ready"

# 5. Grafana query được cả 3?
# Mở http://localhost:3000 → Explore → chọn Prometheus/Loki/Tempo → query thử
```

---

## 9. Quick Reference

| URL | Purpose |
|---|---|
| `http://<server>:9001/metrics` | Prometheus scrape target |
| `http://<server>:9001/health` | Health check (Npgsql + R2) |
| `http://<server>:3100` | Loki (logs) |
| `http://<server>:3200` | Tempo (traces) |
| `http://<server>:4318` | Alloy (OTLP receiver) |
| `http://<server>:9090/targets` | Prometheus targets status |
| `http://<server>:3000` | Grafana UI |

---

## 10. Alerting (issue #89)

Crash-rate alerting via **Grafana Alerting** on Loki → **Telegram** contact point. No Sentry — fully self-hosted.

### 10.1 Health checks (updated)

`/health` now reports three checks:

| Name | Tags | Checks |
|---|---|---|
| `postgresql` | `db` | Npgsql `SELECT 1` |
| `r2` | `storage` | authenticated `HeadBucket` (only when R2 configured) |
| `email` | `notification` | SMTP host + from-address configured |

### 10.2 Telegram contact point

Grafana has a native Telegram contact point — needs a bot token + chat ID (no need to build the full #71 bot):

1. Grafana → Alerting → Contact points → New → **Telegram**.
2. Bot API token (from @BotFather) + chat ID.
3. Save.

Provisioning equivalent (`alerting/contactpoints.yaml`):

```yaml
apiVersion: 1
contactPoints:
  - name: telegram-owner
    type: telegram
    settings:
      bottoken: ${TELEGRAM_BOT_TOKEN}
      chatid: ${TELEGRAM_CHAT_ID}
```

### 10.3 Error-rate alert rule

Loki alert: more than N error-level logs in 5 minutes → page the owner.

LogQL:

```logql
sum(count_over_time({service="quickshare"} |~ "(?i)error|exception|fatal|unhandled" [5m]))
```

Rule: condition `> 10`, evaluation every `1m`, for `1m`, notification → `telegram-owner`.

Provisioning (`alerting/rules.yaml`):

```yaml
apiVersion: 1
groups:
  - name: quickshare-crash
    rules:
      - alert: HighErrorRate
        expr: sum(count_over_time({service="quickshare"} |~ "(?i)error|exception|fatal|unhandled" [5m])) > 10
        for: 1m
        labels:
          severity: critical
        annotations:
          summary: "High error rate on QuickShare"
```

### 10.4 Release tracking + PII scrub

- **Release**: every log line and trace carries the git SHA — logs via the `release` property (from `GIT_SHA` env, default `dev`), traces via OTel resource `service.version`. Set `GIT_SHA` in CI so a crash maps to a release.
- **PII scrub**: `PiiRedactingEnricher` redacts `Email`/`SenderEmail`/`FileName`/`IpAddress`/`RemoteIp`/access keys from every log event before it reaches Loki.
