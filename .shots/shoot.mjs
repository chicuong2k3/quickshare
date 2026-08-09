import { chromium } from 'playwright';
import fs from 'node:fs';

const BASE = 'http://localhost:9000';
const OUT = new URL('.', import.meta.url).pathname.replace(/^\//, '');

const routes = [
    ['home', '/'],
    ['login', '/login'],
    ['pricing', '/pricing'],
    ['upgrade', '/upgrade'],
    ['send', '/send'],
    ['p2p-send', '/p2p/send'],
    ['p2p-download', '/p2p/d/deadbeefdeadbeef'],
    ['download', '/d/deadbeefdeadbeef'],
    ['files', '/files'],
    ['account', '/account'],
    ['account-profile', '/account/profile'],
    ['account-billing', '/account/billing'],
];

const viewports = [
    ['desktop', { width: 1440, height: 900 }],
    ['mobile', { width: 390, height: 844 }],
];

const browser = await chromium.launch();
const report = [];

for (const [vpName, viewport] of viewports) {
    const context = await browser.newContext({ viewport, deviceScaleFactor: 1 });
    const page = await context.newPage();
    const consoleErrors = [];
    page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });

    for (const [name, path] of routes) {
        consoleErrors.length = 0;
        let status = 0;
        try {
            const res = await page.goto(BASE + path, { waitUntil: 'networkidle', timeout: 20000 });
            status = res ? res.status() : 0;
        } catch (e) {
            report.push({ vp: vpName, name, path, error: String(e).slice(0, 200) });
            continue;
        }
        await page.waitForTimeout(1200);
        const file = `${OUT}/${vpName}-${name}.png`;
        await page.screenshot({ path: file, fullPage: true });
        report.push({
            vp: vpName,
            name,
            path,
            status,
            url: page.url(),
            title: await page.title(),
            consoleErrors: consoleErrors.slice(0, 5),
        });
    }
    await context.close();
}

await browser.close();
fs.writeFileSync(`${OUT}/report.json`, JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
