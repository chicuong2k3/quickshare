// k6 load test — TeleCloud R2 upload flow (Prometheus-compatible)
//
// Usage:
//   k6 run --vus 10 --duration 60s upload-load-test.js
//   k6 run --vus 50 --duration 5m upload-load-test.js  (stress test)
//   k6 run --out prometheus-remote=url=http://localhost:9090/api/v1/write upload-load-test.js
//
// Simulates: start-r2 → (skip chunk uploads to R2) → commit-r2
// Chunk uploads to R2 are skipped because they go directly to Cloudflare, not our server.

import { check, sleep } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';
import http from 'k6/http';

// ── Config ──────────────────────────────────────────────────────────────────
const BASE_URL = __ENV.BASE_URL || 'http://localhost:9001';

// k6 will use the cookie jar for auth — set up via `--http-debug` or env
// For authenticated tests, run with: k6 run --env AUTH_COOKIE=".AspNetCore.Cookies=..."

// ── Custom metrics ──────────────────────────────────────────────────────────
const startR2Duration = new Trend('start_r2_duration', true);
const commitR2Duration = new Trend('commit_r2_duration', true);
const uploadSuccessRate = new Rate('upload_success_rate');
const r2Errors = new Counter('r2_errors');

export const options = {
    thresholds: {
        // Fail the test if p95 start-r2 > 2s or p95 commit-r2 > 5s
        'start_r2_duration': ['p(95)<2000'],
        'commit_r2_duration': ['p(95)<5000'],
        // At least 95% uploads must succeed
        'upload_success_rate': ['rate>0.95'],
    },
};

// ── Load profile ────────────────────────────────────────────────────────────
// Controlled by CLI flags: --vus N --duration Xs
// Default stages if no CLI override:
// export const options = {
//     stages: [
//         { duration: '30s', target: 5 },   // ramp up
//         { duration: '1m',  target: 10 },  // steady
//         { duration: '30s', target: 0 },   // ramp down
//     ],
// };

// ── Test scenario ────────────────────────────────────────────────────────────
export default function () {
    const fileName = `loadtest-${__VU}-${__ITER}-${Date.now()}.bin`;
    const fileSize = 5 * 1024 * 1024; // 5 MB
    const partSize = 5 * 1024 * 1024; // 5 MB per part
    const totalParts = Math.ceil(fileSize / partSize);
    const partNumbers = Array.from({ length: totalParts }, (_, i) => i + 1);

    // ── Step 1: Start R2 upload ─────────────────────────────────────────
    const startPayload = JSON.stringify({
        fileName: fileName,
        fileSize: fileSize,
        contentType: 'application/octet-stream',
        partNumbers: partNumbers,
    });

    const startRes = http.post(`${BASE_URL}/api/uploads/start-r2`, startPayload, {
        headers: { 'Content-Type': 'application/json' },
        tags: { name: 'start-r2' },
    });

    startR2Duration.add(startRes.timings.duration);

    const startOk = check(startRes, {
        'start-r2 status 200': (r) => r.status === 200,
    });

    if (!startOk) {
        r2Errors.add(1, { stage: 'start' });
        uploadSuccessRate.add(false);
        return;
    }

    const session = JSON.parse(startRes.body);
    const deliveryId = session.deliveryId;
    const uploadId = session.uploadId;
    const objectKey = session.objectKey;

    // ── Step 2: Simulate chunk uploads to R2 (skipped — client-side) ────
    // In real usage, the browser uploads chunks directly to R2 presigned URLs.
    // We skip this because it doesn't hit our server.
    sleep(0.5);

    // ── Step 3: Commit R2 upload ────────────────────────────────────────
    const commitPayload = JSON.stringify({
        deliveryId: deliveryId,
        uploadId: uploadId,
        objectKey: objectKey,
        fileName: fileName,
        contentType: 'application/octet-stream',
        fileSize: fileSize,
        parts: partNumbers.map((n) => ({
            partNumber: n,
            eTag: `"fake-etag-${n}-${__VU}-${__ITER}"`,
        })),
        otp: null,
    });

    const commitRes = http.post(`${BASE_URL}/api/uploads/commit-r2`, commitPayload, {
        headers: { 'Content-Type': 'application/json' },
        tags: { name: 'commit-r2' },
    });

    commitR2Duration.add(commitRes.timings.duration);

    const commitOk = check(commitRes, {
        'commit-r2 status 201': (r) => r.status === 201,
    });

    if (commitOk) {
        uploadSuccessRate.add(true);
        const result = JSON.parse(commitRes.body);
        console.log(`VU ${__VU}: Upload OK — share: ${result.shareLink}`);
    } else {
        r2Errors.add(1, { stage: 'commit' });
        uploadSuccessRate.add(false);
        console.error(`VU ${__VU}: Commit FAILED (${commitRes.status}) — ${commitRes.body}`);
    }

    sleep(1);
}

// ── Teardown (optional cleanup) ─────────────────────────────────────────────
export function teardown() {
    // Metrics are auto-flushed by k6.
    console.log('Load test complete. Check Grafana dashboard for results.');
}
