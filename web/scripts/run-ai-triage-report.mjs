import fs from 'fs';

// This script never reads or sends an API key. The server uses the keys saved in AI settings
// (encrypted) or the environment variables Ai__Groq__ApiKey / Ai__Gemini__ApiKey.
const API_BASE = process.env.LAZYBEAN_API_BASE ?? 'http://localhost:5188';
const ADMIN_USERNAME = process.env.LAZYBEAN_ADMIN_USERNAME ?? 'admin';
const ADMIN_PASSWORD = process.env.LAZYBEAN_ADMIN_PASSWORD;
const TRIAGE_LIMIT = Number.parseInt(process.env.LAZYBEAN_TRIAGE_LIMIT ?? '200', 10);
const REPORT_PATH = 'web/scripts/ai-triage-report.json';

async function main() {
  console.log('--- AI triage report (keys come from the server, never from this script) ---');

  if (!ADMIN_PASSWORD) {
    throw new Error('Set LAZYBEAN_ADMIN_PASSWORD in the environment to log in as the admin user.');
  }

  // 1. Log in as admin
  console.log('1. Logging in as admin...');
  const loginRes = await fetch(`${API_BASE}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: ADMIN_USERNAME, password: ADMIN_PASSWORD }),
  });

  if (!loginRes.ok) {
    throw new Error(`Login failed with status ${loginRes.status}: ${await loginRes.text()}`);
  }

  const rawCookie = loginRes.headers.get('set-cookie');
  if (!rawCookie) {
    throw new Error('No cookie received from login');
  }
  const cookie = rawCookie.split(';')[0];
  console.log('✓ Authenticated as admin');

  // 2. Read AI settings (masked keys only)
  console.log('2. Reading AI settings via GET /api/settings/ai...');
  const settingsRes = await fetch(`${API_BASE}/api/settings/ai`, { headers: { Cookie: cookie } });
  if (!settingsRes.ok) {
    throw new Error(`Failed to read AI settings: ${await settingsRes.text()}`);
  }
  const settings = await settingsRes.json();
  console.log('✓ AI settings:', {
    isEnabled: settings.isEnabled,
    primaryProvider: settings.primaryProvider,
    triageRunCap: settings.triageRunCap,
    providers: settings.providers.map((p) => ({ provider: p.provider, keySource: p.keySource, maskedApiKey: p.maskedApiKey, model: p.modelName })),
  });

  // 3. Test the primary provider with its saved or environment key
  const primary = settings.primaryProvider ?? 'groq';
  console.log(`3. Testing ${primary} via POST /api/settings/ai/providers/${primary}/test...`);
  const testRes = await fetch(`${API_BASE}/api/settings/ai/providers/${primary}/test`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Cookie: cookie },
    body: JSON.stringify({}),
  });
  if (!testRes.ok) {
    throw new Error(`Key test request failed: ${await testRes.text()}`);
  }
  const testData = await testRes.json();
  console.log('✓ Key test result:', testData);
  if (testData.status !== 'ok') {
    throw new Error(`Key test did not pass (${testData.status}): ${testData.message}`);
  }

  // 4. Run triage (capped by the server's configured per-run cap)
  console.log(`4. Running AI review triage via POST /api/settings/ai/triage?limit=${TRIAGE_LIMIT}...`);
  const triageRes = await fetch(`${API_BASE}/api/settings/ai/triage?limit=${TRIAGE_LIMIT}`, {
    method: 'POST',
    headers: { Cookie: cookie },
  });
  if (!triageRes.ok) {
    throw new Error(`AI triage request failed: ${await triageRes.text()}`);
  }
  const triageStats = await triageRes.json();
  console.log('✓ AI triage stats:', triageStats);

  // 5. Fetch reviews to extract sample verdicts
  console.log('5. Fetching reviews to extract sample verdicts...');
  const reviewsRes = await fetch(`${API_BASE}/api/reviews?status=0&pageSize=100`, {
    headers: { Cookie: cookie },
  });
  if (!reviewsRes.ok) {
    throw new Error(`Failed to fetch reviews: ${await reviewsRes.text()}`);
  }
  const reviewsData = await reviewsRes.json();
  const items = reviewsData.matches || [];

  const triagedItems = items.filter((i) => i.aiVerdict);
  console.log(`Found ${triagedItems.length} reviews with an AI verdict.`);

  const reportData = {
    stats: triageStats,
    sampleCount: Math.min(20, triagedItems.length),
    samples: triagedItems.slice(0, 20).map((i) => ({
      id: i.id,
      refTitle: i.referenceProduct?.title,
      compTitle: i.competitorProduct?.title,
      compVendor: i.competitorProduct?.vendor,
      aiVerdict: i.aiVerdict,
      aiConfidence: i.aiConfidence,
      aiReason: i.aiReason,
    })),
  };

  fs.writeFileSync(REPORT_PATH, JSON.stringify(reportData, null, 2));
  console.log(`✓ Saved report to ${REPORT_PATH}`);
}

main().catch((err) => {
  console.error('❌ Error during AI triage run:', err.message ?? err);
  process.exit(1);
});
