import puppeteer from 'puppeteer-core';
import fs from 'fs';
import path from 'path';

const EDGE_PATH = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const OUTPUT_DIR = path.resolve('../docs/reviews/screenshots');

if (!fs.existsSync(OUTPUT_DIR)) {
  fs.mkdirSync(OUTPUT_DIR, { recursive: true });
}

async function captureScreen(page, url, selector, filename, waitMs = 2500) {
  console.log(`Navigating to ${url}...`);
  await page.goto(url, { waitUntil: 'networkidle0' });
  if (selector) {
    try {
      await page.waitForSelector(selector, { timeout: 10000 });
    } catch (e) {
      console.warn(`Selector ${selector} not found within timeout for ${filename}`);
    }
  }
  await new Promise((r) => setTimeout(r, waitMs));
  const fullPath = path.join(OUTPUT_DIR, filename);
  await page.screenshot({ path: fullPath, fullPage: false });
  console.log(`✓ Saved ${filename}`);
}

async function main() {
  console.log('Launching Edge for Milestone 2 capture...');
  const browser = await puppeteer.launch({
    executablePath: EDGE_PATH,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-gpu'],
  });

  const page = await browser.newPage();

  // 1. Initial login on desktop viewport (1280px)
  await page.setViewport({ width: 1280, height: 850 });
  await page.goto('http://localhost:3000/#/overview', { waitUntil: 'networkidle0' });

  const unlockBtn = await page.$('.lock-btn');
  if (unlockBtn) {
    console.log('Logging in with admin credentials...');
    await unlockBtn.click();
    await page.waitForSelector('.modal-dialog', { timeout: 5000 });
    await new Promise((r) => setTimeout(r, 400));
    await page.type('#login-password', 'Dev-Only-P@ssw0rd!');
    const submitBtn = await page.$('button[type="submit"]');
    if (submitBtn) await submitBtn.click();
    await new Promise((r) => setTimeout(r, 2500));
  }

  // ==========================================
  // DESKTOP CAPTURES (1280px)
  // ==========================================
  console.log('\n--- Capturing Desktop Screens (1280px) ---');
  await page.setViewport({ width: 1280, height: 850 });

  // 1. Offers
  await captureScreen(page, 'http://localhost:3000/#/offers', '.offers-view', '01-offers-desktop.png');

  // 2. Ads & campaigns
  await captureScreen(page, 'http://localhost:3000/#/ads-campaigns', '.ads-campaigns-view', '02-ads-campaigns-desktop.png');

  // 3. Product detail with price history chart
  console.log('Opening product detail modal for price history chart...');
  await page.goto('http://localhost:3000/#/price-comparisons', { waitUntil: 'networkidle0' });
  await page.waitForSelector('.price-matrix-view', { timeout: 10000 });
  await new Promise((r) => setTimeout(r, 2000));
  
  // Click first Details button on price matrix row
  const detailsButtons = await page.$$('.product-meta-row button');
  if (detailsButtons.length > 0) {
    await detailsButtons[0].click();
    await page.waitForSelector('.product-detail-modal', { timeout: 5000 });
    await new Promise((r) => setTimeout(r, 2000)); // wait for chart SVG render
    await page.screenshot({ path: path.join(OUTPUT_DIR, '03-product-detail-desktop.png'), fullPage: false });
    console.log('✓ Saved 03-product-detail-desktop.png');
    // Close modal
    const closeBtn = await page.$('.product-detail-modal .modal-header button');
    if (closeBtn) await closeBtn.click();
    await new Promise((r) => setTimeout(r, 500));
  }

  // 4. Review queue
  await captureScreen(page, 'http://localhost:3000/#/review-queue', '.review-queue-view', '04-review-queue-desktop.png');

  // 5. Runs
  await captureScreen(page, 'http://localhost:3000/#/runs', '.runs-view', '05-runs-desktop.png');

  // 6. Sources and identities (Sources Tab)
  await captureScreen(page, 'http://localhost:3000/#/sources', '.sources-identities-view', '06-sources-desktop.png');

  // 6b. Sources and identities (Identities Tab)
  console.log('Switching to Identities tab...');
  const tabButtons = await page.$$('.tab-bar .tab-btn');
  if (tabButtons.length > 1) {
    await tabButtons[1].click();
    await new Promise((r) => setTimeout(r, 1000));
    await page.screenshot({ path: path.join(OUTPUT_DIR, '06-identities-desktop.png'), fullPage: false });
    console.log('✓ Saved 06-identities-desktop.png');
  }

  // ==========================================
  // MOBILE CAPTURES (390px)
  // ==========================================
  console.log('\n--- Capturing Mobile Screens (390px) ---');
  await page.setViewport({ width: 390, height: 844, isMobile: true, hasTouch: true });

  // 1. Offers Mobile
  await captureScreen(page, 'http://localhost:3000/#/offers', '.offers-view', '01-offers-mobile.png');

  // 2. Ads & campaigns Mobile
  await captureScreen(page, 'http://localhost:3000/#/ads-campaigns', '.ads-campaigns-view', '02-ads-campaigns-mobile.png');

  // 3. Product detail with price history chart Mobile
  console.log('Opening mobile product detail modal...');
  await page.goto('http://localhost:3000/#/price-comparisons', { waitUntil: 'networkidle0' });
  await page.waitForSelector('.price-matrix-view', { timeout: 10000 });
  await new Promise((r) => setTimeout(r, 2000));
  const mobDetailsButtons = await page.$$('.product-meta-row button');
  if (mobDetailsButtons.length > 0) {
    await mobDetailsButtons[0].click();
    await page.waitForSelector('.product-detail-modal', { timeout: 5000 });
    await new Promise((r) => setTimeout(r, 2000));
    await page.screenshot({ path: path.join(OUTPUT_DIR, '03-product-detail-mobile.png'), fullPage: false });
    console.log('✓ Saved 03-product-detail-mobile.png');
    const closeBtn = await page.$('.product-detail-modal .modal-header button');
    if (closeBtn) await closeBtn.click();
    await new Promise((r) => setTimeout(r, 500));
  }

  // 4. Review queue Mobile
  await captureScreen(page, 'http://localhost:3000/#/review-queue', '.review-queue-view', '04-review-queue-mobile.png');

  // 5. Runs Mobile
  await captureScreen(page, 'http://localhost:3000/#/runs', '.runs-view', '05-runs-mobile.png');

  // 6. Sources and identities Mobile
  await captureScreen(page, 'http://localhost:3000/#/sources', '.sources-identities-view', '06-sources-mobile.png');

  await browser.close();
  console.log('\nAll Milestone 2 screenshots captured successfully!');
}

main().catch((err) => {
  console.error('Error during screenshot capture:', err);
  process.exit(1);
});
