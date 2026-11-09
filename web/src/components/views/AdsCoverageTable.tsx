import React, { useEffect, useMemo, useState } from 'react';
import { api, type AdvertiserIdentity, type Competitor } from '../../api/client';
import { fetchAdsSummary, type AdsSummary, type AdsSummaryRow } from '../../api/adsApi';

// Meta page IDs of the seeded stores, used when no approved identity carries one.
const SEED_META_PAGE_IDS: Record<string, string> = {
  'cafelax': '100456846237420',
  'coffee-and-cream': '109786085108938',
  'coffee-zone': '110140451538461',
  'coffee-zone-misr': '110140451538461',
  'fengany': '833564813655781',
  'strong-coffee': '102631558265863',
};

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
}

// What the coverage cell says for one store and platform: when it last synced and how many ads the page shows, or the error.
function coverageLabel(row: AdsSummaryRow | undefined) {
  if (!row) return { text: 'No approved advertiser', tone: 'neutral' as const, detail: null as string | null };
  if (row.lastError) return { text: `Error: ${row.lastError}`, tone: 'bad' as const, detail: null };
  if (row.identities === 0 && row.stored === 0) {
    return { text: 'No approved advertiser', tone: 'neutral' as const, detail: null };
  }
  if (row.lastSyncedAt) {
    return {
      text: `Synced ${formatDate(row.lastSyncedAt)}, ${row.displayed} ad${row.displayed === 1 ? '' : 's'}`,
      tone: 'good' as const,
      detail: `ingested ${row.ingested} · stored ${row.stored} · shown ${row.displayed}`,
    };
  }
  return { text: 'Not synced yet', tone: 'neutral' as const, detail: null };
}

const toneClass = (tone: 'good' | 'bad' | 'neutral') => (tone === 'bad' ? 'bad' : tone === 'good' ? 'good' : 'neutral');

// Per store: when Google and Meta last synced and how many ads the gallery shows. This used to sit on the Ads page; it is
// operational detail, so it lives in Settings > Collector now.
export const AdsCoverageTable: React.FC = () => {
  const [summary, setSummary] = useState<AdsSummary | null>(null);
  const [sources, setSources] = useState<Competitor[]>([]);
  const [identities, setIdentities] = useState<AdvertiserIdentity[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    Promise.all([fetchAdsSummary(), api.GET('/api/sources'), api.GET('/api/identities')])
      .then(([summaryData, srcRes, idRes]) => {
        if (!alive) return;
        setSummary(summaryData);
        setSources(srcRes.data ?? []);
        setIdentities(idRes.data ?? []);
      })
      .catch((err: Error) => {
        if (alive) setError(err.message || 'Could not load ads coverage.');
      })
      .finally(() => {
        if (alive) setIsLoading(false);
      });
    return () => {
      alive = false;
    };
  }, []);

  const stores = useMemo(
    () => [...sources].sort((a, b) => Number(a.isReference) - Number(b.isReference) || (a.name || '').localeCompare(b.name || '')),
    [sources]
  );

  const coverage = useMemo(() => {
    const rows = summary?.rows ?? [];
    const rowsByStore = stores.map((store) => {
      const google = rows.find((r) => r.competitorId === store.id && r.platform === 'Google');
      const meta = rows.find((r) => r.competitorId === store.id && r.platform === 'Meta');
      const metaIdentity = identities.find(
        (i) => i.competitorId === store.id && (i.platform || '').toLowerCase() === 'meta' && (i.status || '').toLowerCase() === 'approved'
      );
      const metaPageId = metaIdentity?.externalId || SEED_META_PAGE_IDS[store.key?.toLowerCase() || ''];
      return { store, google, meta, metaPageId };
    });
    const total = rowsByStore.reduce(
      (acc, s) => ({
        meta: acc.meta + (s.meta?.displayed ?? 0),
        google: acc.google + (s.google?.displayed ?? 0),
      }),
      { meta: 0, google: 0 }
    );
    return { rowsByStore, total };
  }, [summary, stores, identities]);

  return (
    <section className="panel" style={{ marginBottom: '16px' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '12px', flexWrap: 'wrap', marginBottom: '12px' }}>
        <h3 style={{ margin: 0 }}>Ads coverage</h3>
        <a href="#/sources" style={{ fontSize: '12px', color: 'var(--action)', fontWeight: 600, textDecoration: 'none' }}>
          Manage sources &gt; Advertiser identities ↗
        </a>
      </div>

      {error ? (
        <div className="warning-banner">{error}</div>
      ) : isLoading ? (
        <div className="skeleton skeleton-row" style={{ height: '24px', width: '100%' }} />
      ) : (
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '13px' }}>
            <thead>
              <tr style={{ borderBottom: '1px solid var(--line)', textAlign: 'left', color: 'var(--muted)', fontSize: '11px', textTransform: 'uppercase' }}>
                <th style={{ padding: '8px 12px' }}>Store</th>
                <th style={{ padding: '8px 12px' }}>Google Ads Transparency</th>
                <th style={{ padding: '8px 12px' }}>Meta Ad Library</th>
                <th style={{ padding: '8px 12px', textAlign: 'right' }}>Ads shown</th>
                <th style={{ padding: '8px 12px', textAlign: 'right' }}>Official library</th>
              </tr>
            </thead>
            <tbody>
              {coverage.rowsByStore.map(({ store, google, meta, metaPageId }) => {
                const g = coverageLabel(google);
                const m = coverageLabel(meta);
                const total = (google?.displayed ?? 0) + (meta?.displayed ?? 0);
                return (
                  <tr key={store.id} style={{ borderBottom: '1px solid #f1f5f9' }}>
                    <td style={{ padding: '10px 12px', fontWeight: 600 }}>
                      {store.name}
                      {store.isReference && <span className="badge neutral" style={{ marginLeft: '6px', fontSize: '10px' }}>Our store</span>}
                    </td>
                    <td style={{ padding: '10px 12px' }}>
                      <span className={`badge ${toneClass(g.tone)}`} style={{ fontSize: '11px' }}>{g.text}</span>
                      {g.detail && <div style={{ fontSize: '10px', color: 'var(--muted)', marginTop: '2px' }}>{g.detail}</div>}
                    </td>
                    <td style={{ padding: '10px 12px' }}>
                      <span className={`badge ${toneClass(m.tone)}`} style={{ fontSize: '11px' }}>{m.text}</span>
                      {m.detail && <div style={{ fontSize: '10px', color: 'var(--muted)', marginTop: '2px' }}>{m.detail}</div>}
                    </td>
                    <td style={{ padding: '10px 12px', textAlign: 'right', fontWeight: 600 }}>{total > 0 ? total : '—'}</td>
                    <td style={{ padding: '10px 12px', textAlign: 'right' }}>
                      {metaPageId ? (
                        <a
                          href={`https://www.facebook.com/ads/library/?active_status=active&ad_type=all&country=EG&view_all_page_id=${encodeURIComponent(metaPageId)}`}
                          target="_blank"
                          rel="noopener noreferrer"
                          style={{ color: 'var(--action)', fontWeight: 600, fontSize: '12px', textDecoration: 'none' }}
                        >
                          Meta Ad Library ↗
                        </a>
                      ) : (
                        <span style={{ color: 'var(--muted)', fontSize: '11px' }}>—</span>
                      )}
                    </td>
                  </tr>
                );
              })}
              <tr style={{ borderTop: '2px solid var(--line)' }}>
                <td style={{ padding: '10px 12px', fontWeight: 700 }}>All stores</td>
                <td style={{ padding: '10px 12px', fontSize: '12px' }}>{coverage.total.google} ads shown</td>
                <td style={{ padding: '10px 12px', fontSize: '12px' }}>{coverage.total.meta} ads shown</td>
                <td style={{ padding: '10px 12px', textAlign: 'right', fontWeight: 700 }}>{coverage.total.google + coverage.total.meta}</td>
                <td />
              </tr>
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
};
