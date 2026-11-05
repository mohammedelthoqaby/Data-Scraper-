import puppeteer from 'puppeteer-core';

const EDGE_PATH = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';

async function main() {
  const browser = await puppeteer.launch({
    executablePath: EDGE_PATH,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox'],
  });

  const page = await browser.newPage();
  await page.setViewport({ width: 1280, height: 900 });

  for (const url of [
    'https://lazybean.co/single-product/864be144-c54f-424f-a720-2f9e4b122542',
    'https://lazzybean.com/single-product/864be144-c54f-424f-a720-2f9e4b122542'
  ]) {
    console.log('\n--- Testing URL:', url);
    await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 30000 });
    await new Promise(r => setTimeout(r, 4500));
    const title = await page.title();
    const currentUrl = page.url();
    const text = await page.evaluate(() => document.body.innerText);
    const isLost = text.includes('Are you lost');
    const hasProduct = text.includes('Jadeite Chocolate Coffee');
    console.log('Final URL:', currentUrl);
    console.log('Page Title:', title);
    console.log('Contains "Are you lost":', isLost);
    console.log('Contains "Jadeite Chocolate Coffee":', hasProduct);
    console.log('Text preview:', text.slice(0, 180).replace(/\n+/g, ' '));
  }
  await browser.close();
}

main();
