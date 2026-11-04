import puppeteer from 'puppeteer-core';
import path from 'path';

const EDGE_PATH = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';

async function main() {
  console.log('Launching Edge via puppeteer-core...');
  const browser = await puppeteer.launch({
    executablePath: EDGE_PATH,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-gpu'],
  });

  const page = await browser.newPage();

  // 1. First, navigate to overview and take screenshot of login modal
  await page.setViewport({ width: 1440, height: 900 });
  await page.goto('http://localhost:3000/#/overview', { waitUntil: 'networkidle0' });
  const unlockBtn = await page.$('.lock-btn');
  if (unlockBtn) {
    await unlockBtn.click();
    await page.waitForSelector('.modal-dialog', { timeout: 5000 });
    await new Promise((r) => setTimeout(r, 500));
    await page.screenshot({ path: 'screenshots/12-login-dialog-desktop.png', fullPage: false });
    console.log('Saved screenshots/12-login-dialog-desktop.png');

    // Fill in credentials and unlock
    await page.type('#login-password', 'Dev-Only-P@ssw0rd!');
    const submitBtn = await page.$('button[type="submit"]');
    if (submitBtn) await submitBtn.click();
    await new Promise((r) => setTimeout(r, 2500)); // wait for authenticated reloads
  }

  // 2. Desktop - Authenticated Overview (1440x900)
  await page.goto('http://localhost:3000/#/overview', { waitUntil: 'networkidle0' });
  await page.waitForSelector('.overview-view', { timeout: 10000 });
  await new Promise((r) => setTimeout(r, 2000));
  await page.screenshot({ path: 'screenshots/01-overview-desktop.png', fullPage: false });
  console.log('Saved screenshots/01-overview-desktop.png');

  // 3. Desktop - Price Comparisons (1440x900)
  await page.goto('http://localhost:3000/#/price-comparisons', { waitUntil: 'networkidle0' });
  await page.waitForSelector('.price-matrix-view', { timeout: 10000 });
  await new Promise((r) => setTimeout(r, 2500));
  await page.screenshot({ path: 'screenshots/02-price-comparisons-desktop.png', fullPage: false });
  console.log('Saved screenshots/02-price-comparisons-desktop.png');

  // 4. Mobile - Overview (390x844)
  await page.setViewport({ width: 390, height: 844, isMobile: true, hasTouch: true });
  await page.goto('http://localhost:3000/#/overview', { waitUntil: 'networkidle0' });
  await page.waitForSelector('.overview-view', { timeout: 10000 });
  await new Promise((r) => setTimeout(r, 2000));
  await page.screenshot({ path: 'screenshots/01-overview-mobile.png', fullPage: false });
  console.log('Saved screenshots/01-overview-mobile.png');

  // 5. Mobile - Price Comparisons (390x844)
  await page.goto('http://localhost:3000/#/price-comparisons', { waitUntil: 'networkidle0' });
  await page.waitForSelector('.price-matrix-view', { timeout: 10000 });
  await new Promise((r) => setTimeout(r, 2500));
  await page.screenshot({ path: 'screenshots/02-price-comparisons-mobile.png', fullPage: false });
  console.log('Saved screenshots/02-price-comparisons-mobile.png');

  await browser.close();
  console.log('All screenshots captured successfully!');
}

main().catch((err) => {
  console.error('Error taking screenshots:', err);
  process.exit(1);
});
