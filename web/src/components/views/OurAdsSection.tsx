import React, { useEffect, useMemo, useState } from 'react';
import { api } from '../../api/client';
import { fetchOwnAdComparison, type OwnAdComparisonRow, type OwnAdKind } from '../../api/ownAds';
import { fetchOurAds, type AdVariant } from '../../api/adsApi';

type Filter = 'all' | 'both' | 'opportunity' | 'higher';

const FILTERS: { key: Filter; label: string }[] = [
  { key: 'all', label: 'All' },
  { key: 'both', label: 'Both advertise' },
  { key: 'opportunity', label: 'Competitor advertises, we do not' },
  { key: 'higher', label: 'Our ad is more expensive' },
];

const KIND_LABELS: Record<OwnAdKind, { text: string; tone: string }> = {
  Both: { text: 'Both advertise', tone: 'neutral' },
  CompetitorOnly: { text: 'Opportunity', tone: 'good' },
  OursOnly: { text: 'Only we advertise', tone: 'neutral' },
};

function formatPrice(value: number | null): string {
  return value == null ? '—' : `${value.toFixed(2)} EGP`;
}

// Our price against the competitor's, as a percentage: + means we are above them.
function formatGap(gap: number | null): string {
  if (gap == null) return '—';
  if (gap === 0) return 'same price';
  return `${gap > 0 ? '+' : ''}${gap.toFixed(1)}%`;
}

function formatPlatform(platform: string | null): string {
  if (platform === 'Google') return 'Google';
  if (platform === 'Meta') return 'Meta';
  return 'Catalog';
}

// Our ads (Lazy Bean): one row per our product, competitor product and platform, with our catalog price even when
// we do not advertise it. Below it, our own Google and Meta ads. Our ads never count toward the competitor totals.
export const OurAdsSection: React.FC = () => {
  const [rows, setRows] = useState<OwnAdComparisonRow[]>([]);
  const [ourAds, setOurAds] = useState<AdVariant[]>([]);
  const [filter, setFilter] = useState<Filter>('all');
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      try {
        const [comparison, sources] = await Promise.all([fetchOwnAdComparison(), api.GET('/api/sources')]);
        const reference = (sources.data ?? []).find((s) => s.isReference);
        const own = reference?.id ? await fetchOurAds(reference.id) : null;
        if (!cancelled) {
          setRows(comparison);
          setOurAds(own?.ads ?? []);
        }
      } catch (err) {
        if (!cancelled) setError((err as Error).message || 'Could not load our ads.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    };
    load();
    return () => {
      cancelled = true;
    };
  }, []);

  const visible = useMemo(
    () =>
      rows.filter((row) => {
        if (filter === 'both') return row.kind === 'Both';
        if (filter === 'opportunity') return row.kind === 'CompetitorOnly';
        if (filter === 'higher') return row.ourAdIsMoreExpensive;
        return true;
      }),
    [rows, filter]
  );

  return (
    <section className="panel" style={{ marginBottom: '16px', padding: '14px 16px' }}>
      <div className="eyebrow">Lazy Bean</div>
      <h3 style={{ marginTop: 0 }}>Our ads (Lazy Bean)</h3>
      <p className="subtitle" style={{ marginTop: 0 }}>
        Our catalog price against each competitor for the same matched product, one row per platform. Our ads are not
        counted as competitor ads.
      </p>

      <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap', marginBottom: '10px' }}>
        {FILTERS.map((f) => (
          <button
            key={f.key}
            type="button"
            className={filter === f.key ? 'btn btn-primary' : 'btn btn-secondary'}
            onClick={() => setFilter(f.key)}
            style={{ fontSize: '12px', padding: '5px 10px' }}
          >
            {f.label}
          </button>
        ))}
      </div>

      {loading ? (
        <p className="subtitle">Loading our ads…</p>
      ) : error ? (
        <div className="warning-banner">{error}</div>
      ) : visible.length === 0 ? (
        <p className="subtitle">No comparisons for this filter.</p>
      ) : (
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', fontSize: '12px', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ textAlign: 'left', color: 'var(--muted)' }}>
                <th style={{ padding: '4px 8px' }}>Our product</th>
                <th style={{ padding: '4px 8px' }}>Platform</th>
                <th style={{ padding: '4px 8px' }}>Status</th>
                <th style={{ padding: '4px 8px' }}>Our price (each)</th>
                <th style={{ padding: '4px 8px' }}>Gap</th>
                <th style={{ padding: '4px 8px' }}>Competitor</th>
                <th style={{ padding: '4px 8px' }}>Competitor price</th>
                <th style={{ padding: '4px 8px' }}>Ads</th>
              </tr>
            </thead>
            <tbody>
              {visible.map((row) => {
                const label = KIND_LABELS[row.kind];
                return (
                  <tr
                    key={`${row.ourProductId}-${row.competitorProductId ?? 'none'}-${row.platform ?? 'catalog'}`}
                    style={{ borderTop: '1px solid #f0f0f0' }}
                  >
                    <td style={{ padding: '6px 8px', fontWeight: 600 }}>{row.ourProductTitle}</td>
                    <td style={{ padding: '6px 8px' }}>{formatPlatform(row.platform)}</td>
                    <td style={{ padding: '6px 8px' }}>
                      <span className={`badge ${label.tone}`} style={{ fontSize: '10px' }}>
                        {label.text}
                      </span>
                      {row.ourAdIsMoreExpensive && (
                        <span className="badge bad" style={{ fontSize: '10px', marginLeft: '6px' }}>
                          Our price is higher
                        </span>
                      )}
                    </td>
                    <td style={{ padding: '6px 8px' }}>
                      {formatPrice(row.ourPrice)}
                      {row.ourAdCount === 0 && (
                        <span style={{ color: 'var(--muted)', marginLeft: '6px' }}>(catalog, not advertised)</span>
                      )}
                    </td>
                    <td style={{ padding: '6px 8px' }}>{formatGap(row.gapPercent)}</td>
                    <td style={{ padding: '6px 8px' }}>
                      {row.competitorName ? `${row.competitorName}: ${row.competitorProductTitle ?? ''}` : '—'}
                    </td>
                    <td style={{ padding: '6px 8px' }}>{formatPrice(row.competitorPrice)}</td>
                    <td style={{ padding: '6px 8px' }}>
                      {row.ourAdUrl && (
                        <a href={row.ourAdUrl} target="_blank" rel="noreferrer" style={{ marginRight: '8px' }}>
                          Our ad
                        </a>
                      )}
                      {row.competitorAdUrl && (
                        <a href={row.competitorAdUrl} target="_blank" rel="noreferrer">
                          Competitor ad
                        </a>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      <h4 style={{ margin: '18px 0 8px 0' }}>Lazy Bean ads</h4>
      {loading ? null : ourAds.length === 0 ? (
        <p className="subtitle">No Lazy Bean ads collected yet.</p>
      ) : (
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', fontSize: '12px', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ textAlign: 'left', color: 'var(--muted)' }}>
                <th style={{ padding: '4px 8px' }}>Platform</th>
                <th style={{ padding: '4px 8px' }}>Format</th>
                <th style={{ padding: '4px 8px' }}>Creative</th>
                <th style={{ padding: '4px 8px' }}>Advertised</th>
                <th style={{ padding: '4px 8px' }}>Shown</th>
                <th style={{ padding: '4px 8px' }}>Status</th>
                <th style={{ padding: '4px 8px' }}>Link</th>
              </tr>
            </thead>
            <tbody>
              {ourAds.map((ad) => (
                <tr key={ad.id} style={{ borderTop: '1px solid #f0f0f0' }}>
                  <td style={{ padding: '6px 8px' }}>{ad.platform}</td>
                  <td style={{ padding: '6px 8px' }}>{ad.creativeFormat ?? '—'}</td>
                  <td style={{ padding: '6px 8px' }}>{ad.title || ad.externalAdId}</td>
                  <td style={{ padding: '6px 8px' }}>
                    {ad.advertisedPrice != null
                      ? ad.advertisedQuantity && ad.advertisedQuantity > 1
                        ? `${ad.advertisedQuantity} for ${formatPrice(ad.advertisedPrice)} (${formatPrice(ad.unitPrice)} each)`
                        : formatPrice(ad.advertisedPrice)
                      : '—'}
                  </td>
                  <td style={{ padding: '6px 8px' }}>{new Date(ad.lastSeenAt).toLocaleDateString('en-GB')}</td>
                  <td style={{ padding: '6px 8px' }}>{ad.status}</td>
                  <td style={{ padding: '6px 8px' }}>
                    {ad.snapshotUrl && (
                      <a href={ad.snapshotUrl} target="_blank" rel="noreferrer">
                        View ad
                      </a>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
};
