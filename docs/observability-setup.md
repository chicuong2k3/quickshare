# Observability Setup Guide — Prometheus + Grafana

> TeleCloud metrics endpoint: `http://<server>:9001/metrics`
> Health check endpoint: `http://<server>:9001/health`

---

## 1. Quick Answer: Connection Strings?

**Không cần connection string nào giữa Prometheus và QuickShare.** Prometheus scrape metrics qua HTTP — giống như curl vào `/metrics`. Không cần database, không cần auth (trừ phi bạn thêm reverse proxy auth).

Grafana cũng không cần connection string — nó query Prometheus qua HTTP API.

```
┌──────────────┐   HTTP scrape /metrics    ┌──────────────┐   HTTP query    ┌──────────────┐
│  QuickShare  │ ◄─────────────────────── │  Prometheus  │ ◄───────────── │   Grafana    │
│  :9001       │                           │  :9090       │                │  :3000       │
└──────────────┘                           └──────────────┘                └──────────────┘
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

## 5. Docker Compose (All-in-One)

Nếu chưa có Prometheus + Grafana, chạy cả stack 1 lệnh:

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

volumes:
  prometheus_data:
  grafana_data:
```

```bash
docker compose -f docker-compose.observability.yml up -d
```

---

## 6. Verification Checklist

```bash
# 1. QuickShare metrics có expose?
curl http://localhost:9001/metrics | head -5
# → phải thấy: # HELP http_requests_received_total ...

# 2. Prometheus scrape được?
curl 'http://localhost:9090/api/v1/query?query=up{job="quickshare"}'
# → phải thấy: "value":[timestamp,"1"]

# 3. Grafana query được Prometheus?
# Mở http://localhost:3000 → Explore → chọn Prometheus → query: up{job="quickshare"}
```

---

## 7. Quick Reference

| URL | Purpose |
|---|---|
| `http://<server>:9001/metrics` | Prometheus scrape target |
| `http://<server>:9001/health` | Health check (Npgsql + R2) |
| `http://<server>:9090/targets` | Prometheus targets status |
| `http://<server>:3000` | Grafana UI |
