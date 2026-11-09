import puppeteer from 'puppeteer-core';
import http from 'http';

const EDGE_PATH = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';

async function fetchPriceMatrixRows() {
  // First login to get cookie
  const loginRes = await fetch('http://localhost:5188/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: 'admin', password: process.env.LAZYBEAN_ADMIN_PASSWORD })
  });
  const cookie = loginRes.headers.get('set-cookie');

  const res = await fetch('http://localhost:5188/api/price-matrix/latest', {
    headers: { 'Cookie': cookie || '' }
  });
  const parsed = await res.json();
  return parsed.rows || [];
}

async function checkApiProduct(id) {
  try {
    const res = await fetch(`https://api.lazzybean.com/api/products/${id}?lang=en-US`, {
      headers: { 'Accept': 'application/json' },
      signal: AbortSignal.timeout(8000)
    });
    const status = res.status;
    let json = null;
    try {
      json = await res.json();
    } catch {}
    return { status, data: json?.data, message: json?.message };
  } catch (e) {
    return { status: 0, error: e.message };
  }
}

async function main() {
  console.log('Fetching products from local Price Matrix...');
  let rows = [];
  try {
    rows = await fetchPriceMatrixRows();
  } catch (err) {
    console.error('Failed to fetch price matrix:', err);
    return;
  }

  console.log(`Loaded ${rows.length} rows from price matrix.`);

  // Pick 30 deterministic or distributed sample products
  const sample = [];
  const step = Math.max(1, Math.floor(rows.length / 30));
  for (let i = 0; i < rows.length && sample.length < 30; i += step) {
    sample.push(rows[i]);
  }
  console.log(`Selected ${sample.length} sample products to test.\n`);

  const browser = await puppeteer.launch({
    executablePath: EDGE_PATH,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox'],
  });

  const page = await browser.newPage();
  await page.setViewport({ width: 1280, height: 900 });

  let validCount = 0;
  let invalidCount = 0;
  const invalidDetails = [];

  for (let i = 0; i < sample.length; i++) {
    const row = sample[i];
    const id = row.lazyBeanExternalId;
    const title = row.lazyBeanTitle;
    const url = `https://lazybean.co/single-product/${id}`;

    process.stdout.write(`[${i + 1}/30] Testing ${id.slice(0, 8)}: "${title.slice(0, 35)}" ... `);

    // 1. Check live API
    const apiResult = await checkApiProduct(id);

    // 2. Check live browser SPA
    let isLost = false;
    let hasProduct = false;
    let finalTitle = '';

    try {
      await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 25000 });
      await new Promise(r => setTimeout(r, 3000));
      const pageText = await page.evaluate(() => document.body.innerText);
      finalTitle = await page.title();
      isLost = pageText.includes('Are you lost') || pageText.includes('Are you lost ?!');
      
      // Match key words of the title (first 3 words)
      const firstWords = title.split(/\s+/).slice(0, 3).join(' ');
      hasProduct = pageText.toLowerCase().includes(firstWords.toLowerCase());
    } catch (e) {
      isLost = true;
    }

    if (!isLost && hasProduct) {
      validCount++;
      console.log(`✓ VALID (API: ${apiResult.status}, InStock: ${row.lazyBeanInStock})`);
    } else {
      invalidCount++;
      console.log(`❌ INVALID ("Are you lost": ${isLost}, HasTitle: ${hasProduct}, API: ${apiResult.status})`);
      invalidDetails.push({
        id,
        title,
        url,
        inStockInDb: row.lazyBeanInStock,
        priceInDb: row.lazyBeanPrice,
        apiStatus: apiResult.status,
        apiDataStock: apiResult.data?.stock,
        apiMessage: apiResult.message || apiResult.error,
        pageTitle: finalTitle,
        isLost
      });
    }
  }

  await browser.close();

  console.log('\n=============================================');
  console.log(`LINK CHECK RESULTS (30 Sample Products):`);
  console.log(`  Valid Links:   ${validCount} / 30 (${Math.round(validCount / 30 * 100)}%)`);
  console.log(`  Invalid Links: ${invalidCount} / 30 (${Math.round(invalidCount / 30 * 100)}%)`);
  console.log('=============================================\n');

  if (invalidDetails.length > 0) {
    console.log('Detailed Analysis of Invalid Products:');
    console.log(JSON.stringify(invalidDetails, null, 2));
  }
}

main();
