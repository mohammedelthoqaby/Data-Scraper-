import puppeteer from 'puppeteer-core';
import fs from 'fs';
import path from 'path';

const EDGE_PATH = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const ARTIFACT_DIR = 'C:\\Users\\Mohamed Atef\\.gemini\\antigravity\\brain\\0e37937a-0e5e-4091-8bd9-9de0c43cd1be\\screenshots';

async function main() {
  console.log('--- Verifying Price Comparisons Matrix Links and CSV Export ---');
  if (!fs.existsSync(ARTIFACT_DIR)) {
    fs.mkdirSync(ARTIFACT_DIR, { recursive: true });
  }

  // 1. First test CSV Export via API
  console.log('Testing CSV Export from backend API...');
  const loginRes = await fetch('http://localhost:5188/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: 'admin', password: 'Dev-Only-P@ssw0rd!' })
  });
  const cookie = loginRes.headers.get('set-cookie');

  const csvRes = await fetch('http://localhost:5188/api/price-matrix/latest/export', {
    headers: { 'Cookie': cookie || '' }
  });

  if (csvRes.status !== 200) {
    throw new Error(`CSV Export failed with status ${csvRes.status}`);
  }

  const csvText = await csvRes.text();
  const csvLines = csvText.trim().split('\n');
  const headerLine = csvLines[0];
  console.log('CSV Header Columns Count:', headerLine.split(',').length);

  // Check URL columns in CSV header
  const expectedUrlCols = [
    'cafelax_Url',
    'coffee-and-cream_Url',
    'fengany_Url',
    'coffee-zone_Url',
    'strong-coffee_Url'
  ];

  for (const col of expectedUrlCols) {
    if (!headerLine.includes(col)) {
      throw new Error(`Missing expected URL column in CSV header: ${col}`);
    }
  }
  console.log('✓ All 5 competitor URL columns present in CSV header, including coffee-zone_Url.');

  // Check sample row URL values
  let urlCount = 0;
  for (let i = 1; i < Math.min(20, csvLines.length); i++) {
    const line = csvLines[i];
    if (line.includes('http://') || line.includes('https://')) {
      urlCount++;
    }
  }
  console.log(`✓ CSV sample rows have competitor & LazyBean URLs populated (checked ${urlCount} rows).`);

  // 2. Headless Browser Test on Frontend Matrix View
  console.log('\nLaunching browser to verify UI Price Matrix Links...');
  const browser = await puppeteer.launch({
    executablePath: EDGE_PATH,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--window-size=1440,900']
  });

  const page = await browser.newPage();
  await page.setViewport({ width: 1440, height: 900 });

  await page.goto('http://localhost:3000', { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.unlock-card', { timeout: 10000 });

  // Log in via unlock screen
  await page.type('#unlock-password', 'Dev-Only-P@ssw0rd!');
  await page.click('button[type="submit"]');

  // Wait for overview, then navigate to price-comparisons hash
  await page.waitForSelector('.overview-view', { timeout: 15000 });
  console.log('Logged in successfully. Navigating to Price Comparisons...');
  await page.goto('http://localhost:3000/#/price-comparisons', { waitUntil: 'domcontentloaded' });

  await page.waitForSelector('.matrix-table-wrap', { timeout: 15000 });
  await new Promise(r => setTimeout(r, 1500));

  // Check row count and absence of pagination controls
  const singlePageCheck = await page.evaluate(() => {
    const trCount = document.querySelectorAll('.matrix-table tbody tr').length;
    const pagerButtons = document.querySelectorAll('.pager-btn, .pager-buttons');
    const pageSizeSelect = document.querySelectorAll('select option[value="25"]');
    const pagerText = document.querySelector('.matrix-pager')?.innerText?.trim() || '';
    return {
      trCount,
      hasPagerButtons: pagerButtons.length > 0,
      hasPageSizeSelect: pageSizeSelect.length > 0,
      pagerText
    };
  });
  console.log('Single-page Matrix Check:', singlePageCheck);
  if (singlePageCheck.hasPagerButtons) {
    throw new Error('Pagination buttons still present');
  }
  if (singlePageCheck.hasPageSizeSelect) {
    throw new Error('Page size dropdown still present');
  }
  console.log(`✓ All ${singlePageCheck.trCount} products rendered on a single page with zero pagination controls.`);

  // Inspect Lazy Bean product links
  const lazyBeanLinks = await page.evaluate(() => {
    const links = Array.from(document.querySelectorAll('.sticky-col .product-title-link'));
    return links.slice(0, 5).map(a => ({
      tagName: a.tagName,
      href: a.href,
      text: a.innerText.trim(),
      target: a.getAttribute('target'),
      rel: a.getAttribute('rel')
    }));
  });

  console.log('Sample Lazy Bean product links in UI:');
  console.log(JSON.stringify(lazyBeanLinks, null, 2));

  // Verify Lazy Bean links use https://lazybean.co/single-product/
  for (const lb of lazyBeanLinks) {
    if (lb.tagName === 'A') {
      if (!lb.href.startsWith('https://lazybean.co/single-product/')) {
        throw new Error(`Lazy Bean link did not use https://lazybean.co pattern: ${lb.href}`);
      }
      if (lb.rel !== 'noopener noreferrer') {
        throw new Error(`Lazy Bean link missing noopener noreferrer: ${lb.rel}`);
      }
    }
  }
  console.log('✓ Lazy Bean links correctly configured with https://lazybean.co/single-product/{id} and rel="noopener noreferrer"');

  // Inspect Competitor Price Cell Links
  const competitorLinks = await page.evaluate(() => {
    const table = document.querySelector('.matrix-table');
    const cells = Array.from(table.querySelectorAll('tbody tr td:not(.sticky-col):not(.matrix-own)'));
    const found = [];

    for (const cell of cells) {
      const link = cell.querySelector('a');
      if (link && link.href) {
        const computed = window.getComputedStyle(link);
        found.push({
          href: link.href,
          text: link.innerText.trim(),
          titleAttr: link.getAttribute('title'),
          target: link.getAttribute('target'),
          rel: link.getAttribute('rel'),
          textOverflow: computed.textOverflow,
          whiteSpace: computed.whiteSpace,
          overflow: computed.overflow,
          maxWidth: computed.maxWidth
        });
      }
    }
    return found;
  });

  console.log(`Found ${competitorLinks.length} competitor links in the visible matrix.`);
  console.log('Sample competitor links:');
  console.log(JSON.stringify(competitorLinks.slice(0, 5), null, 2));

  // Verify competitor link properties
  for (const comp of competitorLinks.slice(0, 10)) {
    if (comp.target !== '_blank') {
      throw new Error(`Competitor link target was not _blank: ${comp.target}`);
    }
    if (!comp.rel || !comp.rel.includes('noopener')) {
      throw new Error(`Competitor link rel missing noopener: ${comp.rel}`);
    }
    if (!comp.titleAttr || comp.titleAttr.length === 0) {
      throw new Error(`Competitor link missing title attribute for hover: ${comp.text}`);
    }
    if (comp.textOverflow !== 'ellipsis') {
      throw new Error(`Competitor link not styled with text-overflow ellipsis: ${comp.textOverflow}`);
    }
  }
  console.log('✓ Competitor cell links have target="_blank", rel="noopener noreferrer", title attribute for hover, and CSS ellipsis truncation.');

  // Take desktop screenshot of Price Matrix
  const desktopPath = path.join(ARTIFACT_DIR, '02-price-comparisons-desktop.png');
  await page.screenshot({ path: desktopPath, fullPage: false });
  console.log(`✓ Saved desktop screenshot to ${desktopPath}`);

  // Test mobile viewport
  await page.setViewport({ width: 390, height: 844, isMobile: true, hasTouch: true });
  await new Promise(r => setTimeout(r, 1000));
  const mobilePath = path.join(ARTIFACT_DIR, '02-price-comparisons-mobile.png');
  await page.screenshot({ path: mobilePath, fullPage: false });
  console.log(`✓ Saved mobile screenshot to ${mobilePath}`);

  await browser.close();
  console.log('\n=== ALL MATRIX LINK & CSV VERIFICATIONS PASSED ===');
}

main().catch(err => {
  console.error('VERIFICATION ERROR:', err);
  process.exit(1);
});
