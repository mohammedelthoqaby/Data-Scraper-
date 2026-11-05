const API_BASE = 'http://localhost:5188';

async function main() {
  console.log('1. Logging in as admin...');
  const loginRes = await fetch(`${API_BASE}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: 'admin', password: 'Dev-Only-P@ssw0rd!' })
  });

  if (!loginRes.ok) {
    console.error('Failed to log in:', loginRes.status, await loginRes.text());
    process.exit(1);
  }

  const cookie = loginRes.headers.get('set-cookie');
  console.log('✓ Successfully logged in');

  console.log('2. Syncing alerts...');
  const syncRes = await fetch(`${API_BASE}/api/alerts/sync`, {
    method: 'POST',
    headers: { 'Cookie': cookie }
  });

  if (!syncRes.ok) {
    console.error('Failed to sync alerts:', syncRes.status, await syncRes.text());
    process.exit(1);
  }

  const alerts = await syncRes.json();
  console.log(`✓ Sync returned ${alerts.length} alerts!`);

  console.log('3. Fetching summary...');
  const summaryRes = await fetch(`${API_BASE}/api/alerts/summary`, {
    headers: { 'Cookie': cookie }
  });
  const summary = await summaryRes.json();
  console.log('Summary:', JSON.stringify(summary, null, 2));

  console.log('4. Fetching health...');
  const healthRes = await fetch(`${API_BASE}/api/alerts/health`, {
    headers: { 'Cookie': cookie }
  });
  const health = await healthRes.json();
  console.log('Health:', JSON.stringify(health, null, 2));

  console.log('5. Top 10 example alerts:');
  alerts.slice(0, 10).forEach((a, idx) => {
    console.log(`\n[#${idx + 1}] [${a.severity}] [${a.type}] ${a.title}`);
    console.log(`  Reason: ${a.reason}`);
    console.log(`  Action: ${a.suggestedAction}`);
    console.log(`  Impact: ${a.egpImpact} EGP | Bestseller: ${a.isBestseller}`);
  });
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
