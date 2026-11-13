// Evaluate a JS expression in a local page and print the result (JSON).
// Usage: node eval.mjs <url> "<expression>"
import { spawn } from "node:child_process";
import os from "node:os";

const [url, expr, w = "1440", h = "900"] = process.argv.slice(2);
const port = 9700 + Math.floor(Math.random() * 200);
const chrome = `${os.homedir()}/AppData/Local/ms-playwright/chromium-1234/chrome-win64/chrome.exe`;

const proc = spawn(chrome, [
  "--headless=new", "--disable-gpu", "--incognito", "--hide-scrollbars",
  "--remote-debugging-port=" + port,
  "--user-data-dir=" + os.tmpdir() + "/qs-eval-" + port,
  "about:blank",
], { stdio: "ignore" });

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
async function wsUrl() {
  for (let i = 0; i < 60; i++) {
    try {
      const list = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
      const p = list.find((t) => t.type === "page");
      if (p?.webSocketDebuggerUrl) return p.webSocketDebuggerUrl;
    } catch {}
    await sleep(150);
  }
  throw new Error("no devtools");
}
const ws = new WebSocket(await wsUrl());
await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
let id = 0;
const pending = new Map();
ws.onmessage = (ev) => {
  const m = JSON.parse(ev.data);
  if (m.id && pending.has(m.id)) {
    const { resolve, reject } = pending.get(m.id);
    pending.delete(m.id);
    m.error ? reject(new Error(JSON.stringify(m.error))) : resolve(m.result);
  }
};
const send = (method, params = {}) => new Promise((resolve, reject) => {
  const n = ++id; pending.set(n, { resolve, reject });
  ws.send(JSON.stringify({ id: n, method, params }));
});

try {
  await send("Page.enable");
  await send("Emulation.setDeviceMetricsOverride", { width: Number(w), height: Number(h), deviceScaleFactor: 1, mobile: false });
  await send("Page.navigate", { url });
  await sleep(3200);
  const r = await send("Runtime.evaluate", { expression: expr, returnByValue: true, awaitPromise: true });
  console.log(JSON.stringify(r.result?.value ?? r, null, 2));
} finally {
  ws.close();
  proc.kill();
}
