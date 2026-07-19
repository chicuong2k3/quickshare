// Copies dependency-free vendor libraries from node_modules into wwwroot/lib/
// so they ship as local, self-contained assets (constraint: NO external/CDN call at runtime).
//
//  1) qrcode-generator  -> wwwroot/lib/qrcode/qrcode.min.js     (client-side QR rendering)
//  2) @alpinejs/csp     -> wwwroot/lib/alpine/alpine.min.js      (Alpine.js, CSP-compliant)
//                          RizzyUI's README injects Alpine via RzHeadOutlet/UseRizzy, but
//                          those do not exist in 0.9.2-alpha, so we vendor Alpine locally.
const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..");
const nm = path.join(root, "node_modules");
const www = path.join(root, "wwwroot", "lib");

function copyFirst(candidates, dest) {
    for (const c of candidates) {
        if (fs.existsSync(c)) {
            fs.copyFileSync(c, dest);
            console.log("[vendor] " + path.relative(root, c) + " -> " + path.relative(root, dest));
            return true;
        }
    }
    return false;
}

// 1) QR
fs.mkdirSync(path.join(www, "qrcode"), { recursive: true });
if (!copyFirst(
    [
        path.join(nm, "qrcode-generator", "qrcode.min.js"),
        path.join(nm, "qrcode-generator", "qrcode.js"),
    ],
    path.join(www, "qrcode", "qrcode.min.js")
)) {
    console.error("[vendor] qrcode-generator not found. Run `npm install`.");
    process.exit(1);
}

// 2) Alpine.js (CSP build — auto-bootstraps, no external call)
fs.mkdirSync(path.join(www, "alpine"), { recursive: true });
if (!copyFirst(
    [
        path.join(nm, "@alpinejs", "csp", "dist", "cdn.min.js"),
        path.join(nm, "alpinejs", "dist", "cdn.min.js"),
    ],
    path.join(www, "alpine", "alpine.min.js")
)) {
    console.error("[vendor] @alpinejs/csp (or alpinejs) not found. Run `npm install`.");
    process.exit(1);
}

console.log("[vendor] done.");
