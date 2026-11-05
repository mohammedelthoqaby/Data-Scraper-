import puppeteer from 'puppeteer-core';

const EDGE_PATH = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';

async function main() {
  const browser = await puppeteer.launch({
    executablePath: EDGE_PATH,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox']
  });
  const page = await browser.newPage();
  await page.setViewport({ width: 1400, height: 1100 });

  await page.goto('http://localhost:3000/#/overview', { waitUntil: 'networkidle0' });
  await page.waitForSelector('#unlock-password', { timeout: 5000 });
  await page.type('#unlock-password', 'Dev-Only-P@ssw0rd!');
  const submitBtn = await page.$('button[type="submit"]');
  await submitBtn.click();
  await page.waitForSelector('.overview-view', { timeout: 10000 });
  
  // Wait until attention items finish loading
  await page.waitForFunction(() => !document.body.innerText.includes('Analyzing market facts'), { timeout: 15000 });
  await new Promise(r => setTimeout(r, 1000));

  await page.screenshot({ path: 'C:/Users/Mohamed Atef/.gemini/antigravity/brain/0e37937a-0e5e-4091-8bd9-9de0c43cd1be/screenshots/06-overview-attention-loaded-en.png' });
  console.log('✓ Captured 06-overview-attention-loaded-en.png');

  // Click Arabic toggle
  const buttons = await page.$$('.panel-header button');
  for (const b of buttons) {
    const text = await page.evaluate(el => el.textContent, b);
    if (text.includes('عربي')) {
      await b.click();
      break;
    }
  }
  await new Promise(r => setTimeout(r, 800));

  await page.screenshot({ path: 'C:/Users/Mohamed Atef/.gemini/antigravity/brain/0e37937a-0e5e-4091-8bd9-9de0c43cd1be/screenshots/07-overview-attention-loaded-ar.png' });
  console.log('✓ Captured 07-overview-attention-loaded-ar.png');

  await browser.close();
}
main();
