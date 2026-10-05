// Opens the running app in a real browser and uses it the way a person would.
// The other tests never run the page's JavaScript or its Content-Security-Policy, so only this
// one notices a page that loads but does not respond.
//
//   APP_URL=http://localhost:5150/ node wizard.mjs
import { chromium } from 'playwright-core';

const url = process.env.APP_URL ?? 'http://localhost:5150/';
const problems = [];
const check = (ok, message) => { if (!ok) problems.push(message); };

const browser = await chromium.launch({ channel: 'chrome' });
const page = await browser.newPage();
page.on('console', message => { if (message.type() === 'error') problems.push('Console: ' + message.text()); });
page.on('pageerror', error => problems.push('Script error: ' + error));
await page.addInitScript(() => document.addEventListener('securitypolicyviolation', event =>
  console.error(`Blocked by the Content-Security-Policy (${event.violatedDirective}): ${event.blockedURI || 'inline'}`)));

const response = await page.goto(url);
const headers = response.headers();
check(response.status() === 200, `The page answered ${response.status()}.`);
check((headers['content-security-policy'] ?? '').includes("script-src 'self'"), 'The page has no Content-Security-Policy.');
check(headers['x-frame-options'] === 'DENY', 'The page may be shown inside another site.');

// The page is drawn before it is connected. A click only counts once Blazor has started.
const linux = page.locator('button[data-template="linux-service"]');
let connected = false;
for (let attempt = 0; attempt < 30 && !connected; attempt++) {
  await linux.click();
  connected = await page.locator('button[data-template="linux-service"].applied').waitFor({ timeout: 500 }).then(() => true, () => false);
}
check(connected, 'Clicking a template did nothing: the page is not interactive.');

if (connected) {
  await page.click('button[data-step="Result"]');
  const yaml = await page.locator('.yaml-preview').textContent({ timeout: 10000 });
  check(yaml.includes('systemctl start'), 'The pipeline for a Linux service is not shown on the Result step.');

  const [download] = await Promise.all([page.waitForEvent('download', { timeout: 10000 }), page.click('#download-yaml')]);
  check(download.suggestedFilename() === 'azure-pipelines.yml', `Download gave ${download.suggestedFilename()}.`);

  // Light and dark: the button switches, and the choice is still there after a reload.
  const theme = () => page.evaluate(() => document.documentElement.dataset.theme ?? '');
  const before = await theme();
  await page.click('.theme-toggle');
  const chosen = await theme();
  check(['light', 'dark'].includes(chosen) && chosen !== before, `The theme button did not switch (was "${before}", is "${chosen}").`);
  await page.reload();
  check(await theme() === chosen, 'The chosen theme was forgotten after a reload.');
}

await browser.close();
if (problems.length > 0) {
  for (const problem of problems) console.log(`::error title=Browser test::${problem}`);
  process.exit(1);
}
console.log('The wizard works in a browser: templates, the Result step, download and the theme button.');
