import puppeteer from 'puppeteer-core';

const EDGE_PATH = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';

async function main() {
  console.log('Launching headless browser to click 3 products on lazybean.co...');
  const browser = await puppeteer.launch({
    executablePath: EDGE_PATH,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox'],
  });

  const page = await browser.newPage();
  await page.setViewport({ width: 1280, height: 900 });

  try {
    // 1. Navigate to the storefront
    console.log('Navigating to https://lazybean.co ...');
    await page.goto('https://lazybean.co', {
      waitUntil: 'domcontentloaded',
      timeout: 30000,
    });
    await new Promise((r) => setTimeout(r, 4000));

    // Get product links from homepage / category
    let productHrefs = await page.evaluate(() => {
      return Array.from(document.querySelectorAll('a[href*="/single-product/"]'))
        .map(a => a.href)
        .filter((v, i, self) => self.indexOf(v) === i);
    });

    if (productHrefs.length < 3) {
      console.log('Found fewer than 3 links on home, checking a category page...');
      await page.goto('https://lazybean.co/products?category=b0508000-1c24-447a-82a5-5332dee71c96', {
        waitUntil: 'domcontentloaded',
        timeout: 30000,
      });
      await new Promise((r) => setTimeout(r, 4000));
      const catLinks = await page.evaluate(() => {
        return Array.from(document.querySelectorAll('a[href*="/single-product/"]'))
          .map(a => a.href)
          .filter((v, i, self) => self.indexOf(v) === i);
      });
      productHrefs = Array.from(new Set([...productHrefs, ...catLinks]));
    }

    console.log(`Discovered ${productHrefs.length} live product links on the site.`);
    const sampleHrefs = productHrefs.slice(0, 3);

    const results = [];
    for (let i = 0; i < sampleHrefs.length; i++) {
      const url = sampleHrefs[i];
      console.log(`\nNavigating to Product ${i + 1}: ${url} ...`);
      await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 30000 });
      await new Promise((r) => setTimeout(r, 4000));

      const info = await page.evaluate(() => {
        const title = document.title;
        const text = document.body.innerText;
        const hasAreYouLost = text.includes('Are you lost');
        const h1 = document.querySelector('h1')?.innerText?.trim() || '';
        const h2 = document.querySelector('h2')?.innerText?.trim() || '';
        return {
          title,
          h1,
          h2,
          hasAreYouLost,
          textSample: text.slice(0, 200).replace(/\s+/g, ' ')
        };
      });

      console.log(`Product ${i + 1} Result:`, {
        url,
        documentTitle: info.title,
        heading: info.h1 || info.h2,
        hasAreYouLost: info.hasAreYouLost,
        valid: !info.hasAreYouLost && (info.title.length > 0 || info.h1.length > 0)
      });

      results.push({
        index: i + 1,
        url,
        title: info.title,
        heading: info.h1 || info.h2,
        hasAreYouLost: info.hasAreYouLost,
        valid: !info.hasAreYouLost
      });
    }

    console.log('\n=== 3 PRODUCTS NAVIGATION SUMMARY ===');
    console.log(JSON.stringify(results, null, 2));

  } catch (err) {
    console.error('Error during 3 products test:', err);
  } finally {
    await browser.close();
  }
}

main();
