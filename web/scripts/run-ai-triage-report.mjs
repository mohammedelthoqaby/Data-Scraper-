import fs from 'fs';
import path from 'path';

const API_BASE = 'http://localhost:5188';
const KEY_PATH = 'backend/src/LazyBean.Api/Google-AI-Studio-API.txt';

async function main() {
  console.log('--- Starting AI Settings Configuration and 100-Pair Review Triage ---');

  const apiKey = fs.readFileSync(KEY_PATH, 'utf8').trim();
  console.log(`Read API key: ending with ...${apiKey.slice(-4)}`);

  // 1. Log in as admin
  console.log('1. Logging in as admin...');
  const loginRes = await fetch(`${API_BASE}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: 'admin', password: process.env.LAZYBEAN_ADMIN_PASSWORD }),
  });

  if (!loginRes.ok) {
    throw new Error(`Login failed with status ${loginRes.status}: ${await loginRes.text()}`);
  }

  const rawCookie = loginRes.headers.get('set-cookie');
  if (!rawCookie) {
    throw new Error('No cookie received from login');
  }
  const cookie = rawCookie.split(';')[0];
  console.log('✓ Successfully authenticated as admin');

  // 2. Configure AI settings
  console.log('2. Configuring AI settings via PUT /api/settings/ai...');
  const settingsRes = await fetch(`${API_BASE}/api/settings/ai`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
      'Cookie': cookie,
    },
    body: JSON.stringify({
      isEnabled: true,
      apiKey: apiKey,
      modelName: 'gemini-3.5-flash',
      dailyRequestCap: 500,
    }),
  });

  if (!settingsRes.ok) {
    throw new Error(`Failed to update AI settings: ${await settingsRes.text()}`);
  }
  const settings = await settingsRes.json();
  console.log('✓ AI Settings updated:', {
    isEnabled: settings.isEnabled,
    modelName: settings.modelName,
    maskedApiKey: settings.maskedApiKey,
    dailyRequestCap: settings.dailyRequestCap,
  });

  // 3. Test Connection
  console.log('3. Testing Gemini connection via POST /api/settings/ai/test...');
  const testRes = await fetch(`${API_BASE}/api/settings/ai/test`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Cookie': cookie,
    },
    body: JSON.stringify({}),
  });

  if (!testRes.ok) {
    throw new Error(`Test connection request failed: ${await testRes.text()}`);
  }
  const testData = await testRes.json();
  console.log('✓ Test Connection Result:', testData);
  if (!testData.success) {
    throw new Error(`Gemini connection test failed: ${testData.message}`);
  }

  // 4. Run Triage on 100 pending pairs
  console.log('4. Running AI review triage on 100 pending pairs via POST /api/settings/ai/triage?limit=100...');
  const triageRes = await fetch(`${API_BASE}/api/settings/ai/triage?limit=100`, {
    method: 'POST',
    headers: {
      'Cookie': cookie,
    },
  });

  if (!triageRes.ok) {
    throw new Error(`AI triage request failed: ${await triageRes.text()}`);
  }
  const triageStats = await triageRes.json();
  console.log('✓ AI Triage Execution Stats:', triageStats);

  // 5. Fetch reviews to get the 100 evaluated pairs with details
  console.log('5. Fetching reviews to extract 20 detailed sample examples...');
  const reviewsRes = await fetch(`${API_BASE}/api/reviews?status=0&pageSize=100`, {
    headers: { 'Cookie': cookie },
  });
  if (!reviewsRes.ok) {
    throw new Error(`Failed to fetch reviews: ${await reviewsRes.text()}`);
  }
  const reviewsData = await reviewsRes.json();
  const items = reviewsData.matches || [];

  const triagedItems = items.filter(i => i.aiVerdict);
  console.log(`Found ${triagedItems.length} reviews with AI verdict.`);

  // Save full results to scratch file
  const reportData = {
    stats: triageStats,
    sampleCount: Math.min(20, triagedItems.length),
    samples: triagedItems.slice(0, 20).map(i => ({
      id: i.id,
      refTitle: i.referenceProduct?.title,
      compTitle: i.competitorProduct?.title,
      compVendor: i.competitorProduct?.vendor,
      aiVerdict: i.aiVerdict,
      aiConfidence: i.aiConfidence,
      aiReason: i.aiReason,
    })),
  };

  fs.writeFileSync('web/scripts/ai-triage-report.json', JSON.stringify(reportData, null, 2));
  console.log('✓ Saved report to web/scripts/ai-triage-report.json');
}

main().catch(err => {
  console.error('❌ Error during AI triage run:', err);
  process.exit(1);
});
