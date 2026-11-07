import puppeteer from 'puppeteer-core';
import assert from 'assert';

const EDGE_PATH = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const APP_URL = 'http://localhost:5188/';

async function testSingleOrigin() {
  console.log('Testing Single-Origin API + UI on http://localhost:5188...\n');

  const browser = await puppeteer.launch({
    executablePath: EDGE_PATH,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-gpu'],
  });

  const page = await browser.newPage();
  await page.setViewport({ width: 1280, height: 850 });

  const failedRequests = [];
  page.on('requestfailed', req => {
    failedRequests.push(`${req.method()} ${req.url()} (${req.failure()?.errorText})`);
  });

  try {
    // 1. Visit root - should serve SPA
    console.log('1. Navigating to http://localhost:5188/ ...');
    await page.goto(APP_URL, { waitUntil: 'networkidle0' });

    // Verify unlock screen is visible
    await page.waitForSelector('[data-testid="unlock-screen"]', { timeout: 5000 });
    console.log('✓ Unlock screen displayed correctly.');

    // 2. Log in
    console.log('2. Entering admin credentials...');
    await page.type('#unlock-password', process.env.LAZYBEAN_ADMIN_PASSWORD);
    const submitBtn = await page.$('button[type="submit"]');
    assert.ok(submitBtn, 'Submit button not found');
    await submitBtn.click();

    // Wait for Overview to mount
    console.log('3. Waiting for OverviewView to mount...');
    await page.waitForSelector('.overview-view', { timeout: 10000 });
    console.log('✓ Overview view mounted.');

    // 4. Navigate to Needs Attention
    console.log('4. Navigating to Action Center (#/attention)...');
    await page.goto('http://localhost:5188/#/attention', { waitUntil: 'networkidle0' });
    await page.waitForSelector('.action-center-view', { timeout: 10000 });
    console.log('✓ Action Center view mounted.');

    // 5. Navigate to Price Matrix
    console.log('5. Navigating to Price Comparisons (#/price-comparisons)...');
    await page.goto('http://localhost:5188/#/price-comparisons', { waitUntil: 'networkidle0' });
    await page.waitForSelector('.price-matrix-view', { timeout: 10000 });
    console.log('✓ Price Matrix view mounted.');

    if (failedRequests.length > 0) {
      console.warn('Warning: Some requests failed:', failedRequests);
    } else {
      console.log('✓ All network requests succeeded with 0 failures.');
    }

    console.log('\n✅ ALL SINGLE-ORIGIN INTEGRATION CHECKS PASSED!\n');
  } finally {
    await browser.close();
  }
}

testSingleOrigin().catch(err => {
  console.error('Test failed:', err);
  process.exit(1);
});
