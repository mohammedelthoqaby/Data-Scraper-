import puppeteer from 'puppeteer-core';
import fs from 'fs';

const EDGE_PATH = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';

async function main() {
  fs.mkdirSync('screenshots', { recursive: true });
  console.log('Launching browser to test sticky header...');
  const browser = await puppeteer.launch({
    executablePath: EDGE_PATH,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-gpu'],
  });

  const page = await browser.newPage();
  await page.setViewport({ width: 1440, height: 900 });

  // Unlock if needed
  await page.goto('http://localhost:3000/#/overview', { waitUntil: 'networkidle0' });
  const unlockInput = await page.$('#unlock-password');
  if (unlockInput) {
    await unlockInput.type(process.env.LAZYBEAN_ADMIN_PASSWORD);
    const submitBtn = await page.$('button[type="submit"]');
    if (submitBtn) await submitBtn.click();
    await page.waitForSelector('.overview-view', { timeout: 10000 });
    await new Promise((r) => setTimeout(r, 1000));
  }

  // Go to price comparisons
  await page.goto('http://localhost:3000/#/price-comparisons', { waitUntil: 'networkidle0' });
  await page.waitForSelector('.matrix-table', { timeout: 10000 });
  await new Promise((r) => setTimeout(r, 1500));

  // Screenshot 1: At the top
  await page.screenshot({ path: 'screenshots/sticky-01-top.png', fullPage: false });
  console.log('Saved screenshots/sticky-01-top.png');

  // Scroll down inside .matrix-table-wrap by 600px
  await page.evaluate(() => {
    const wrap = document.querySelector('.matrix-table-wrap');
    if (wrap) {
      wrap.scrollTop = 600;
    }
  });
  await new Promise((r) => setTimeout(r, 500));

  // Check the position of th elements
  const thPositions = await page.evaluate(() => {
    const wrap = document.querySelector('.matrix-table-wrap');
    const wrapRect = wrap.getBoundingClientRect();
    const ths = Array.from(document.querySelectorAll('table.matrix-table th')).map((th) => {
      const rect = th.getBoundingClientRect();
      return {
        text: th.innerText.trim(),
        topRelative: rect.top - wrapRect.top,
        top: rect.top,
        zIndex: window.getComputedStyle(th).zIndex,
        position: window.getComputedStyle(th).position,
      };
    });
    return {
      scrollTop: wrap.scrollTop,
      ths,
    };
  });

  console.log('Table scrolled to:', thPositions.scrollTop);
  console.log('TH Elements:', JSON.stringify(thPositions.ths, null, 2));

  // Screenshot 2: Scrolled down 600px
  await page.screenshot({ path: 'screenshots/sticky-02-scrolled.png', fullPage: false });
  console.log('Saved screenshots/sticky-02-scrolled.png');

  // Scroll horizontally to test left sticky column
  await page.evaluate(() => {
    const wrap = document.querySelector('.matrix-table-wrap');
    if (wrap) {
      wrap.scrollLeft = 300;
    }
  });
  await new Promise((r) => setTimeout(r, 500));

  // Screenshot 3: Scrolled diagonally (down 600px, right 300px)
  await page.screenshot({ path: 'screenshots/sticky-03-scrolled-xy.png', fullPage: false });
  console.log('Saved screenshots/sticky-03-scrolled-xy.png');

  // Test window scroll: scroll window down 250px, table at top
  await page.evaluate(() => {
    window.scrollTo(0, 250);
  });
  await new Promise((r) => setTimeout(r, 500));
  await page.screenshot({ path: 'screenshots/sticky-04-window-scrolled.png', fullPage: false });
  console.log('Saved screenshots/sticky-04-window-scrolled.png');

  await browser.close();
  console.log('Done testing sticky header!');
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
