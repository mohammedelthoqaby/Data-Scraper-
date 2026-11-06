import React, { useEffect, useMemo, useState } from 'react';
import { fetchOwnAdComparison, type OwnAdComparisonRow, type OwnAdKind } from '../../api/ownAds';

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

// Our ads (Lazy Bean) compared with competitor ads for the same matched product. Our ads never count toward the
// competitor totals above.
export const OurAdsSection: React.FC = () => {
  const [rows, setRows] = useState<OwnAdComparisonRow[]>([]);
  const [filter, setFilter] = useState<Filter>('all');
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetchOwnAdComparison()
      .then((next) => {
        if (!cancelled) setRows(next);
      })
      .catch((err: Error) => {
        if (!cancelled) setError(err.message || 'Could not load our ads.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
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
        Our ads compared with competitor ads for the same matched product. Our ads are not counted as competitor ads.
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
        <table style={{ width: '100%', fontSize: '12px', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ textAlign: 'left', color: 'var(--muted)' }}>
              <th style={{ padding: '4px 8px' }}>Our product</th>
              <th style={{ padding: '4px 8px' }}>Status</th>
              <th style={{ padding: '4px 8px' }}>Our price</th>
              <th style={{ padding: '4px 8px' }}>Competitor</th>
              <th style={{ padding: '4px 8px' }}>Competitor price</th>
              <th style={{ padding: '4px 8px' }}>Ads</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((row) => {
              const label = KIND_LABELS[row.kind];
              return (
                <tr key={`${row.ourProductId}-${row.competitorProductId ?? 'none'}`} style={{ borderTop: '1px solid #f0f0f0' }}>
                  <td style={{ padding: '6px 8px', fontWeight: 600 }}>{row.ourProductTitle}</td>
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
                  <td style={{ padding: '6px 8px' }}>{formatPrice(row.ourPrice)}</td>
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
      )}
    </section>
  );
};
