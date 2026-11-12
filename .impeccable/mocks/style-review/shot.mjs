// Screenshot a local review page at a real device viewport.
// Headless Chrome clamps its window width (~497px on Windows), so mobile shots
// must go through CDP Emulation.setDeviceMetricsOverride, not --window-size.
// Usage: node .impeccable/mocks/style-review/shot.mjs <file.html> <out.png> <width> <height> [scale] [theme]
import { spawn } from "node:child_process";
import { writeFileSync } from "node:fs";
import { pathToFileURL } from "node:url";
import { resolve } from "node:path";
import os from "node:os";

const [file, out, w = "1440", h = "900", scale = "1", theme, hide] = process.argv.slice(2);
if (!file || !out) {
  console.error("usage: node shot.mjs <file.html> <out.png> <width> <height> [scale] [theme]");
  process.exit(1);
}
const width = Number(w);
const height = Number(h);
const dpr = Number(scale);
const chrome = `${os.homedir()}/AppData/Local/ms-playwright/chromium-1234/chrome-win64/chrome.exe`;
const port = 9300 + Math.floor(Math.random() * 400);

const proc = spawn(chrome, [
  "--headless=new",
  "--disable-gpu",
  "--incognito",
  "--hide-scrollbars",
  "--remote-debugging-port=" + port,
  "--user-data-dir=" + os.tmpdir() + "/qs-shot-" + port,
  "about:blank",
], { stdio: "ignore" });

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function getWsUrl() {
  for (let i = 0; i < 60; i++) {
    try {
      const res = await fetch(`http://127.0.0.1:${port}/json/list`);
      const list = await res.json();
      const page = list.find((t) => t.type === "page");
      if (page?.webSocketDebuggerUrl) return page.webSocketDebuggerUrl;
    } catch {}
    await sleep(150);
  }
  throw new Error("devtools endpoint never came up");
}

const ws = new WebSocket(await getWsUrl());
await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });

let id = 0;
const pending = new Map();
ws.onmessage = (ev) => {
  const msg = JSON.parse(ev.data);
  if (msg.id && pending.has(msg.id)) {
    const { resolve, reject } = pending.get(msg.id);
    pending.delete(msg.id);
    msg.error ? reject(new Error(JSON.stringify(msg.error))) : resolve(msg.result);
  }
};
const send = (method, params = {}) =>
  new Promise((resolve, reject) => {
    const n = ++id;
    pending.set(n, { resolve, reject });
    ws.send(JSON.stringify({ id: n, method, params }));
  });

try {
  await send("Page.enable");
  await send("Emulation.setDeviceMetricsOverride", {
    width,
    height,
    deviceScaleFactor: dpr,
    mobile: width < 700,
  });
  const target = /^https?:\/\//.test(file) ? file : pathToFileURL(resolve(file)).href;
  await send("Page.navigate", { url: target });
  await sleep(2200);
  if (hide) {
    await send("Runtime.evaluate", {
      expression: `document.querySelectorAll(${JSON.stringify(hide)}).forEach(function(n){ n.remove(); })`,
    });
    await sleep(400);
  }
  if (theme) {
    await send("Runtime.evaluate", {
      expression: `document.documentElement.dataset.theme = ${JSON.stringify(theme)}`,
    });
    await sleep(700);
  }
  const { data } = await send("Page.captureScreenshot", {
    format: "png",
    captureBeyondViewport: false,
  });
  writeFileSync(out, Buffer.from(data, "base64"));
  console.log(`${out} — ${width}x${height} @${dpr}x${theme ? " · " + theme : ""}`);
} finally {
  ws.close();
  proc.kill();
}
