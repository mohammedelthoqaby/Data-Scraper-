import puppeteer from 'puppeteer-core';

const EDGE_PATH = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';

async function main() {
  const browser = await puppeteer.launch({
    executablePath: EDGE_PATH,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox']
  });
  const page = await browser.newPage();
  await page.setViewport({ width: 1400, height: 900 });

  // 1. Login
  await page.goto('http://localhost:3000/#/overview', { waitUntil: 'networkidle0' });
  await page.waitForSelector('#unlock-password', { timeout: 5000 });
  await page.type('#unlock-password', 'Dev-Only-P@ssw0rd!');
  const submitBtn = await page.$('button[type="submit"]');
  await submitBtn.click();
  await page.waitForSelector('.overview-view', { timeout: 10000 });
  await new Promise(r => setTimeout(r, 1500));

  // Screenshot 1: Overview
  await page.screenshot({ path: 'C:/Users/Mohamed Atef/.gemini/antigravity/brain/0e37937a-0e5e-4091-8bd9-9de0c43cd1be/screenshots/01-overview-ai-attention.png' });
  console.log('✓ Captured 01-overview-ai-attention.png');

  // Screenshot 2: Review Queue with AI Badges
  await page.goto('http://localhost:3000/#/review-queue', { waitUntil: 'networkidle0' });
  await page.waitForSelector('.review-card', { timeout: 10000 });
  await new Promise(r => setTimeout(r, 1500));
  await page.screenshot({ path: 'C:/Users/Mohamed Atef/.gemini/antigravity/brain/0e37937a-0e5e-4091-8bd9-9de0c43cd1be/screenshots/02-review-queue-ai-triage.png' });
  console.log('✓ Captured 02-review-queue-ai-triage.png');

  // Screenshot 2b: Filter by AI: Different
  await page.select('.matrix-filters-bar select', 'different');
  await new Promise(r => setTimeout(r, 1000));
  await page.screenshot({ path: 'C:/Users/Mohamed Atef/.gemini/antigravity/brain/0e37937a-0e5e-4091-8bd9-9de0c43cd1be/screenshots/04-review-queue-ai-different.png' });
  console.log('✓ Captured 04-review-queue-ai-different.png');

  // Screenshot 3: Settings AI View
  await page.goto('http://localhost:3000/#/settings-ai', { waitUntil: 'networkidle0' });
  await page.waitForSelector('.settings-ai-view', { timeout: 10000 });
  await new Promise(r => setTimeout(r, 1500));
  await page.screenshot({ path: 'C:/Users/Mohamed Atef/.gemini/antigravity/brain/0e37937a-0e5e-4091-8bd9-9de0c43cd1be/screenshots/03-settings-ai-config.png' });
  console.log('✓ Captured 03-settings-ai-config.png');

  await browser.close();
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
