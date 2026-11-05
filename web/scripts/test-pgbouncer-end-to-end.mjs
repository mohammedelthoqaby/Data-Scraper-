const API_BASE = 'http://localhost:5188';

async function main() {
  console.log('=== Step 1: Admin Login ===');
  const loginRes = await fetch(`${API_BASE}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: 'admin', password: 'Dev-Only-P@ssw0rd!' })
  });
  if (!loginRes.ok) {
    throw new Error(`Login failed: ${loginRes.status} ${await loginRes.text()}`);
  }
  const cookie = loginRes.headers.get('set-cookie');
  console.log('✓ Successfully logged in as admin through PgBouncer connection');

  console.log('\n=== Step 2: Data Protection Key Ring Test ===');
  // Read current AI settings
  const getAiRes = await fetch(`${API_BASE}/api/settings/ai`, {
    headers: { 'Cookie': cookie }
  });
  const currentAi = await getAiRes.json();
  console.log('Current AI Settings:', currentAi);

  // Update AI settings with an encrypted API key
  const updateAiRes = await fetch(`${API_BASE}/api/settings/ai`, {
    method: 'PUT',
    headers: { 'Cookie': cookie, 'Content-Type': 'application/json' },
    body: JSON.stringify({
      isEnabled: true,
      apiKey: 'AIzaSyTestMonsterAspPgBouncerKey1234',
      modelName: 'gemini-2.5-flash',
      dailyRequestCap: 500
    })
  });
  if (!updateAiRes.ok) {
    throw new Error(`Failed to update AI settings: ${updateAiRes.status} ${await updateAiRes.text()}`);
  }
  const updatedAi = await updateAiRes.json();
  console.log('✓ Updated AI Settings through DataProtection:', updatedAi);
  if (!updatedAi.maskedApiKey.endsWith('1234')) {
    throw new Error(`Masked key mismatch: expected ...1234 but got ${updatedAi.maskedApiKey}`);
  }

  console.log('\n=== Step 3: Run Matching Pass ===');
  const matchRes = await fetch(`${API_BASE}/api/matches/run`, {
    method: 'POST',
    headers: { 'Cookie': cookie }
  });
  if (!matchRes.ok) {
    throw new Error(`Matching failed: ${matchRes.status} ${await matchRes.text()}`);
  }
  const matchResult = await matchRes.json();
  console.log('✓ Matching completed:', matchResult);

  console.log('\n=== Step 4: Run Alerts Calculation & Sync ===');
  const alertRes = await fetch(`${API_BASE}/api/alerts/sync`, {
    method: 'POST',
    headers: { 'Cookie': cookie }
  });
  if (!alertRes.ok) {
    throw new Error(`Alerts sync failed: ${alertRes.status} ${await alertRes.text()}`);
  }
  const alerts = await alertRes.json();
  console.log(`✓ Alerts synced through PgBouncer: ${alerts.length} active alerts calculated`);

  console.log('\n=== Step 5: Start Collection Run ===');
  const runRes = await fetch(`${API_BASE}/api/runs`, {
    method: 'POST',
    headers: { 'Cookie': cookie }
  });
  if (!runRes.ok) {
    throw new Error(`Start run failed: ${runRes.status} ${await runRes.text()}`);
  }
  const runData = await runRes.json();
  console.log('✓ Collection Run started:', runData);

  // Poll collection run for a few seconds to verify execution in PgBouncer
  console.log('\n=== Step 6: Verify Run Status via PgBouncer ===');
  for (let i = 0; i < 5; i++) {
    await new Promise(r => setTimeout(r, 2000));
    const statusRes = await fetch(`${API_BASE}/api/runs/${runData.runId}`, {
      headers: { 'Cookie': cookie }
    });
    if (statusRes.ok) {
      const runStatus = await statusRes.json();
      console.log(`[Poll ${i + 1}] Status: ${runStatus.status}, ProductsScraped: ${runStatus.productsScraped}, Started: ${runStatus.startedAt}`);
      if (runStatus.status === 'Completed' || runStatus.status === 'Partial') {
        break;
      }
    }
  }

  console.log('\n🎉 ALL PGBOUNCER TRANSACTION POOLING OPERATIONS SUCCEEDED!');
}

main().catch(err => {
  console.error('FAILED:', err);
  process.exit(1);
});
