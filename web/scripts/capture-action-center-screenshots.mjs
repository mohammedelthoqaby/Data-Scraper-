import puppeteer from 'puppeteer-core';
import path from 'path';

const EDGE_PATH = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const ARTIFACT_DIR = 'C:/Users/Mohamed Atef/.gemini/antigravity/brain/0e37937a-0e5e-4091-8bd9-9de0c43cd1be';

async function main() {
  const browser = await puppeteer.launch({
    executablePath: EDGE_PATH,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox']
  });

  const page = await browser.newPage();
  await page.setViewport({ width: 1440, height: 900 });

  // 1. Authenticate
  console.log('Logging in...');
  await page.goto('http://localhost:3000/#/overview', { waitUntil: 'networkidle0' });
  const pwInput = await page.$('#unlock-password');
  if (pwInput) {
    await page.type('#unlock-password', process.env.LAZYBEAN_ADMIN_PASSWORD);
    const submitBtn = await page.$('button[type="submit"]');
    await submitBtn.click();
    await page.waitForSelector('.overview-view', { timeout: 10000 });
  }
  await new Promise(r => setTimeout(r, 1000));

  // Screenshot 1: Overview with Top 5 Urgent Alerts
  console.log('Capturing Overview Top Urgent Alerts...');
  await page.goto('http://localhost:3000/#/overview', { waitUntil: 'networkidle0' });
  await page.waitForSelector('.overview-view', { timeout: 10000 });
  await new Promise(r => setTimeout(r, 1500));
  await page.screenshot({ path: path.join(ARTIFACT_DIR, '01-overview-urgent-alerts-desktop.png') });
  console.log('✓ Captured 01-overview-urgent-alerts-desktop.png');

  // Screenshot 2: Action Center Desktop (#/attention)
  console.log('Capturing Action Center Desktop...');
  await page.goto('http://localhost:3000/#/attention', { waitUntil: 'networkidle0' });
  await page.waitForSelector('.action-center-view', { timeout: 10000 });
  await new Promise(r => setTimeout(r, 1500));
  await page.screenshot({ path: path.join(ARTIFACT_DIR, '02-action-center-desktop.png') });
  console.log('✓ Captured 02-action-center-desktop.png');

  // Screenshot 3: Action Center Mobile (#/attention)
  console.log('Capturing Action Center Mobile...');
  await page.setViewport({ width: 390, height: 844, isMobile: true, hasTouch: true });
  await page.goto('http://localhost:3000/#/attention', { waitUntil: 'networkidle0' });
  await page.waitForSelector('.action-center-view', { timeout: 10000 });
  await new Promise(r => setTimeout(r, 1500));
  await page.screenshot({ path: path.join(ARTIFACT_DIR, '03-action-center-mobile.png') });
  console.log('✓ Captured 03-action-center-mobile.png');

  // Reset to Desktop
  await page.setViewport({ width: 1440, height: 900 });

  // Screenshot 4: Data Quality & Match Review / Weekly AI Audit (#/data-quality)
  console.log('Capturing Data Quality & Weekly AI Audit...');
  await page.goto('http://localhost:3000/#/data-quality', { waitUntil: 'networkidle0' });
  await page.waitForSelector('.data-quality-view', { timeout: 10000 });
  // Click on "Weekly AI Audit" tab
  const auditTab = await page.$('button[data-tab="audit"], .dq-tab:nth-child(2)');
  if (auditTab) {
    await auditTab.click();
    await new Promise(r => setTimeout(r, 1000));
  }
  await page.screenshot({ path: path.join(ARTIFACT_DIR, '04-data-quality-audit-desktop.png') });
  console.log('✓ Captured 04-data-quality-audit-desktop.png');

  // Screenshot 5: Alert Thresholds Settings (#/settings-alerts)
  console.log('Capturing Alert Thresholds Settings...');
  await page.goto('http://localhost:3000/#/settings-alerts', { waitUntil: 'networkidle0' });
  await page.waitForSelector('.settings-alerts-view', { timeout: 10000 });
  await new Promise(r => setTimeout(r, 1000));
  await page.screenshot({ path: path.join(ARTIFACT_DIR, '05-settings-alerts-desktop.png') });
  console.log('✓ Captured 05-settings-alerts-desktop.png');

  // Screenshot 6: Price Matrix showing [AI] Match Badges with Undo Match
  console.log('Capturing Price Matrix with [AI] Match Badge...');
  await page.goto('http://localhost:3000/#/price-comparisons', { waitUntil: 'networkidle0' });
  await page.waitForSelector('.price-matrix-view', { timeout: 10000 });
  await new Promise(r => setTimeout(r, 1500));
  await page.screenshot({ path: path.join(ARTIFACT_DIR, '06-price-matrix-ai-badges.png') });
  console.log('✓ Captured 06-price-matrix-ai-badges.png');

  await browser.close();
  console.log('All screenshots captured successfully!');
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
