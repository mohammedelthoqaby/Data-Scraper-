import puppeteer from 'puppeteer-core';

const EDGE_PATH = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';

async function main() {
  console.log('Inspecting category page https://lazybean.co/products?category=b0508000-1c24-447a-82a5-5332dee71c96 ...');
  const browser = await puppeteer.launch({
    executablePath: EDGE_PATH,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox'],
  });

  const page = await browser.newPage();
  await page.setViewport({ width: 1280, height: 900 });

  // Listen to router navigations and network requests
  page.on('framenavigated', frame => {
    console.log('Frame navigated to:', frame.url());
  });

  try {
    await page.goto('https://lazybean.co/products?category=b0508000-1c24-447a-82a5-5332dee71c96', {
      waitUntil: 'domcontentloaded',
      timeout: 30000,
    });
    console.log('Waiting 6s for product cards to render...');
    await new Promise((r) => setTimeout(r, 6000));

    // Inspect all anchors
    const allAnchors = await page.evaluate(() => {
      return Array.from(document.querySelectorAll('a[href]'))
        .map(a => a.href)
        .filter(h => !h.includes('category=') && !h.endsWith('#') && h !== 'https://lazybean.co/');
    });
    console.log('Non-category anchors:', allAnchors.slice(0, 20));

    // Inspect elements that look like product cards
    const cards = await page.evaluate(() => {
      const items = Array.from(document.querySelectorAll('.product-card, .card, [class*="product-item"], [class*="productCard"]'));
      return items.slice(0, 3).map(el => ({
        className: el.className,
        text: el.innerText.slice(0, 60),
        html: el.outerHTML.slice(0, 200)
      }));
    });
    console.log('Sample cards:', JSON.stringify(cards, null, 2));

    // Try clicking the first product card if any
    const clicked = await page.evaluate(() => {
      const firstCard = document.querySelector('.product-card, [class*="product-card"], .card, [class*="productItem"]');
      if (firstCard) {
        firstCard.click();
        return true;
      }
      return false;
    });
    console.log('Clicked first card?', clicked);

    if (clicked) {
      await new Promise((r) => setTimeout(r, 4000));
      console.log('URL after click:', page.url());
    }

  } catch (err) {
    console.error('Error during investigation:', err);
  } finally {
    await browser.close();
  }
}

main();
