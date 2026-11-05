import puppeteer from 'puppeteer-core';

const EDGE_PATH = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';

async function main() {
  const browser = await puppeteer.launch({
    executablePath: EDGE_PATH,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox'],
  });

  const page = await browser.newPage();

  try {
    await page.goto('https://lazybean.co/', { waitUntil: 'domcontentloaded', timeout: 30000 });
    await new Promise((r) => setTimeout(r, 4000));

    const routes = await page.evaluate(() => {
      // Check Nuxt router
      if (window.$nuxt && window.$nuxt.$router) {
        return window.$nuxt.$router.options.routes.map(r => ({
          path: r.path,
          name: r.name,
          component: r.chunkName
        }));
      }
      return 'No $nuxt found';
    });

    console.log('Nuxt Routes:', JSON.stringify(routes, null, 2));

  } catch (err) {
    console.error('Error:', err);
  } finally {
    await browser.close();
  }
}

main();
