import puppeteer from 'puppeteer-core';
import assert from 'assert';

const EDGE_PATH = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const APP_URL = 'http://localhost:3000/#/overview';

async function runTests() {
  console.log('--- Starting Auth & Overview Verification Tests ---\n');

  const browser = await puppeteer.launch({
    executablePath: EDGE_PATH,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-gpu'],
  });

  const page = await browser.newPage();
  await page.setViewport({ width: 1280, height: 850 });

  try {
    // -------------------------------------------------------------------------
    // TEST 1: Astro Dev Toolbar is permanently disabled
    // -------------------------------------------------------------------------
    console.log('Test 1: Verifying Astro Dev Toolbar is disabled...');
    await page.goto(APP_URL, { waitUntil: 'networkidle0' });

    const devToolbar = await page.$('astro-dev-toolbar');
    assert.strictEqual(devToolbar, null, 'FAIL: astro-dev-toolbar element was found on page!');
    console.log('✓ PASS: Astro dev toolbar is completely absent from DOM.\n');

    // -------------------------------------------------------------------------
    // TEST 2: Unauthenticated state renders Unlock screen, NEVER zeros or empty states
    // -------------------------------------------------------------------------
    console.log('Test 2: Verifying unauthenticated state displays Unlock screen (no zeros/empty states)...');
    
    // Clear cookies to guarantee unauthenticated state
    const client = await page.target().createCDPSession();
    await client.send('Network.clearBrowserCookies');

    await page.goto(APP_URL, { waitUntil: 'networkidle0' });
    await new Promise((r) => setTimeout(r, 1000));

    // Verify Unlock screen is rendered
    const unlockScreen = await page.$('[data-testid="unlock-screen"]');
    assert.notStrictEqual(unlockScreen, null, 'FAIL: Unlock screen was not rendered when unauthenticated!');

    const unlockHeading = await page.$eval('[data-testid="unlock-screen"] .brand-text', (el) => el.textContent);
    assert.ok(unlockHeading.includes('Unlock Operations'), 'FAIL: Unlock Operations brand heading missing');

    // Verify Overview and its zero stats are NOT rendered in the DOM
    const overviewView = await page.$('.overview-view');
    assert.strictEqual(overviewView, null, 'FAIL: Overview view was rendered while unauthenticated (possible zeros leak)!');

    const statNumbers = await page.$$('.stat-number');
    assert.strictEqual(statNumbers.length, 0, `FAIL: Found ${statNumbers.length} stat-number elements while unauthenticated!`);

    const noChangesText = await page.evaluate(() => document.body.innerText.includes('No supported competitor changes recorded'));
    assert.strictEqual(noChangesText, false, 'FAIL: Empty state "No supported competitor changes recorded" was visible while unauthenticated!');

    console.log('✓ PASS: Unlock screen rendered; zero stat cards and empty states are strictly prevented.\n');

    // -------------------------------------------------------------------------
    // TEST 3: Login via Unlock Screen and verify real numbers on Overview
    // -------------------------------------------------------------------------
    console.log('Test 3: Logging in via Unlock Screen and verifying Overview shows real numbers...');

    await page.waitForSelector('#unlock-password', { timeout: 5000 });
    await page.type('#unlock-password', 'Dev-Only-P@ssw0rd!');
    
    const submitBtn = await page.$('button[type="submit"]');
    assert.ok(submitBtn, 'Submit button on unlock screen not found');
    await submitBtn.click();

    // Wait for Overview to mount and data to load
    await page.waitForSelector('.overview-view', { timeout: 10000 });
    await page.waitForSelector('.stat-card', { timeout: 10000 });
    await new Promise((r) => setTimeout(r, 2000));

    // Extract KPI numbers from Overview
    const kpiValues = await page.$$eval('.stat-card .stat-number', (elements) =>
      elements.map((el) => parseInt(el.textContent.trim(), 10))
    );

    console.log(`  KPI Cards Found: ${kpiValues.length}`);
    console.log(`  Suggested actions count: ${kpiValues[0]}`);
    console.log(`  Supported price comparisons count: ${kpiValues[1]}`);
    console.log(`  Product matches to review count: ${kpiValues[2]}`);

    assert.ok(kpiValues[0] > 0, `FAIL: Suggested actions is ${kpiValues[0]}, expected > 0`);
    assert.ok(kpiValues[1] > 0, `FAIL: Supported price comparisons is ${kpiValues[1]}, expected > 0`);
    assert.ok(kpiValues[2] > 0, `FAIL: Product matches to review is ${kpiValues[2]}, expected > 0`);

    // Verify recent competitor changes render real items
    const recentDecisionRows = await page.$$('.decision-row');
    assert.ok(recentDecisionRows.length > 0, `FAIL: Expected decision rows on Overview, found ${recentDecisionRows.length}`);
    console.log(`  Decision/Change rows rendered on Overview: ${recentDecisionRows.length}`);

    // Verify that empty state placeholder is NOT present
    const emptyNotice = await page.evaluate(() =>
      document.body.innerText.includes('No supported competitor changes recorded')
    );
    assert.strictEqual(emptyNotice, false, 'FAIL: Empty state notice still displayed despite having real offers data!');

    console.log('✓ PASS: Overview displays real numbers across review count, price comparisons, and competitor changes.\n');

    console.log('=============================================');
    console.log('ALL VERIFICATION TESTS PASSED SUCCESSFULLY! ✓');
    console.log('=============================================');
  } catch (err) {
    console.error('\n❌ TEST RUN FAILED:', err);
    process.exitCode = 1;
  } finally {
    await browser.close();
  }
}

runTests();
