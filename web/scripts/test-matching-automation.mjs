const API_BASE = 'http://localhost:5188';

async function main() {
  console.log('1. Logging in as admin...');
  const loginRes = await fetch(`${API_BASE}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: 'admin', password: 'Dev-Only-P@ssw0rd!' })
  });

  const cookie = loginRes.headers.get('set-cookie');
  console.log('✓ Logged in');

  console.log('2. Running matching pass (with automated triage)...');
  const triageRes = await fetch(`${API_BASE}/api/settings/ai/triage`, {
    method: 'POST',
    headers: { 'Cookie': cookie }
  });

  const triageStats = await triageRes.json();
  console.log('Triage Stats:', JSON.stringify(triageStats, null, 2));

  console.log('3. Fetching review queue to see pending items remaining (should be only unsure items)...');
  const reviewsRes = await fetch(`${API_BASE}/api/reviews?status=0&pageSize=10`, {
    headers: { 'Cookie': cookie }
  });
  const reviews = await reviewsRes.json();
  console.log(`Pending Review Items count: ${reviews.total}`);

  console.log('4. Fetching weekly audit of AI-approved matches...');
  const auditRes = await fetch(`${API_BASE}/api/matches/audit`, {
    headers: { 'Cookie': cookie }
  });
  const auditItems = await auditRes.json();
  console.log(`Audit Items returned: ${auditItems.length}`);
  if (auditItems.length > 0) {
    console.log('Sample audit item:', JSON.stringify(auditItems[0], null, 2));
  }
}

main().catch(console.error);
