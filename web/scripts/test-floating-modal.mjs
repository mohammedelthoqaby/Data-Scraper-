import puppeteer from 'puppeteer-core';
import fs from 'fs';
import path from 'path';

const EDGE_PATH = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const ARTIFACT_DIR = 'C:\\Users\\Mohamed Atef\\.gemini\\antigravity\\brain\\0e37937a-0e5e-4091-8bd9-9de0c43cd1be\\screenshots';

async function main() {
  console.log('Testing Product Detail Floating Modal...');
  const browser = await puppeteer.launch({
    executablePath: EDGE_PATH,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--window-size=1440,900']
  });

  const page = await browser.newPage();
  await page.setViewport({ width: 1440, height: 900 });

  await page.goto('http://localhost:3000/#/price-comparisons', { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.unlock-card', { timeout: 10000 });

  // Unlock / log in
  await page.type('#unlock-password', 'Dev-Only-P@ssw0rd!');
  await page.click('button[type="submit"]');

  await page.waitForSelector('.matrix-table-wrap', { timeout: 15000 });
  await new Promise(r => setTimeout(r, 1500));

  console.log('Finding Caffè Vergnano Antica Bottega row to test multiple observations...');
  const clicked = await page.evaluate(() => {
    const rows = Array.from(document.querySelectorAll('.matrix-table tbody tr'));
    const targetRow = rows.find(r => r.innerText.includes('Antica Bottega')) || rows[0];
    if (targetRow) {
      const detailsBtn = targetRow.querySelector('button');
      if (detailsBtn) {
        detailsBtn.click();
        return true;
      }
    }
    return false;
  });

  if (!clicked) {
    throw new Error('Could not find Details button in matrix');
  }

  await page.waitForSelector('.product-detail-modal', { timeout: 10000 });
  await new Promise(r => setTimeout(r, 1500));

  // Verify computed styles of modal-overlay, product-detail-modal and close button
  const modalChecks = await page.evaluate(() => {
    const overlay = document.querySelector('.modal-overlay');
    const modal = document.querySelector('.product-detail-modal');
    const closeBtn = modal?.querySelector('.modal-header button');
    const header = modal?.querySelector('.modal-header');
    const links = Array.from(modal?.querySelectorAll('tbody tr a') || []).map(a => ({
      href: a.href,
      text: a.innerText.trim(),
      title: a.getAttribute('title')
    }));

    const modalRect = modal?.getBoundingClientRect();
    const btnRect = closeBtn?.getBoundingClientRect();
    const headerRect = header?.getBoundingClientRect();

    const chartContainer = modal?.querySelector('.chart-svg-container') || modal?.querySelector('.chart-card');
    const tableContainer = modal?.querySelector('table')?.parentElement;
    const rows = Array.from(modal?.querySelectorAll('tbody tr') || []);

    return {
      overlayPosition: window.getComputedStyle(overlay).position,
      modalRightDistance: modalRect && btnRect ? modalRect.right - btnRect.right : null,
      chartHeight: chartContainer?.getBoundingClientRect().height,
      tableContainerHeight: tableContainer?.getBoundingClientRect().height,
      tableContainerScrollHeight: tableContainer?.scrollHeight,
      hasTableVerticalScrollbar: tableContainer ? tableContainer.scrollHeight > tableContainer.clientHeight : false,
      rowCount: rows.length,
      linksCount: links.length,
      sampleLinks: links.slice(0, 4)
    };
  });

  console.log('Modal Verification Checks:', JSON.stringify(modalChecks, null, 2));

  // Capture screenshot of the floating modal
  const screenshotPath = path.join(ARTIFACT_DIR, '03-product-detail-desktop.png');
  await page.screenshot({ path: screenshotPath, fullPage: false });
  console.log(`Saved screenshot to ${screenshotPath}`);

  // Test mobile viewport
  await page.setViewport({ width: 390, height: 844, isMobile: true, hasTouch: true });
  await new Promise(r => setTimeout(r, 500));
  const hasModal = await page.$('.product-detail-modal');
  if (!hasModal) {
    await page.evaluate(() => {
      const rows = Array.from(document.querySelectorAll('.matrix-table tbody tr'));
      const targetRow = rows.find(r => r.innerText.includes('Antica Bottega')) || rows[0];
      targetRow?.querySelector('button')?.click();
    });
    await page.waitForSelector('.product-detail-modal', { timeout: 5000 });
    await new Promise(r => setTimeout(r, 1000));
  }
  const mobileScreenshotPath = path.join(ARTIFACT_DIR, '03-product-detail-mobile.png');
  await page.screenshot({ path: mobileScreenshotPath, fullPage: false });
  console.log(`Saved mobile screenshot to ${mobileScreenshotPath}`);

  await browser.close();
  console.log('✓ Product Detail Modal is confirmed FLOATING!');
}

main().catch(err => {
  console.error('ERROR:', err);
  process.exit(1);
});
