import React, { useState, useEffect, useMemo } from 'react';
import { api, type Competitor, type AdvertiserIdentity } from '../../api/client';
import {
  fetchAdsSummary,
  fetchCampaignPage,
  type AdCampaignCard,
  type AdVariant,
  type AdsSummary,
  type AdsSummaryRow,
  type CampaignPage,
} from '../../api/adsApi';
import { OurAdsSection } from './OurAdsSection';

interface AdsCampaignsViewProps {
  onOpenProductDetail?: (productId: string, title?: string) => void;
}

const PAGE_SIZE = 20;

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

function formatEgp(value: number | null | undefined): string {
  return value == null ? '—' : `${value.toFixed(2)} EGP`;
}

// Escapes text put into an attribute of the preview page below.
function escapeAttribute(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
}

// A video creative's preview is a script on Google's display-ads host. It runs in an iframe with a sandbox and no
// access to this page, and loads only after the user asks for the preview.
function videoPreviewDocument(scriptUrl: string): string {
  return `<!doctype html><html><body style="margin:0"><script src="${escapeAttribute(scriptUrl)}"></script></body></html>`;
}

export const AdsCampaignsView: React.FC<AdsCampaignsViewProps> = ({ onOpenProductDetail }) => {
  const [summary, setSummary] = useState<AdsSummary | null>(null);
  const [page, setPage] = useState<CampaignPage | null>(null);
  const [sources, setSources] = useState<Competitor[]>([]);
  const [identities, setIdentities] = useState<AdvertiserIdentity[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isPageLoading, setIsPageLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isSyncing, setIsSyncing] = useState(false);
  const [syncMessage, setSyncMessage] = useState<string | null>(null);
  const [syncErrors, setSyncErrors] = useState<string[]>([]);
  const [openPreviews, setOpenPreviews] = useState<Set<string>>(new Set());

  // Filters. The default is Active, so the page shows what is live now.
  const [selectedCompetitor, setSelectedCompetitor] = useState<string>('all');
  const [selectedPlatform, setSelectedPlatform] = useState<string>('all');
  const [selectedStatus, setSelectedStatus] = useState<string>('active');
  const [pageNumber, setPageNumber] = useState<number>(1);
  const [search, setSearch] = useState<string>('');

  const loadSummaryAndSources = async () => {
    const [summaryData, srcRes, idRes] = await Promise.all([
      fetchAdsSummary(),
      api.GET('/api/sources'),
      api.GET('/api/identities'),
    ]);
    setSummary(summaryData);
    if (srcRes.data) setSources(srcRes.data);
    if (idRes.data) setIdentities(idRes.data);
  };

  useEffect(() => {
    let isMounted = true;
    setIsLoading(true);
    setError(null);
    loadSummaryAndSources()
      .catch((err: Error) => {
        if (isMounted) setError(err.message || 'Failed to load the ads summary.');
      })
      .finally(() => {
        if (isMounted) setIsLoading(false);
      });
    return () => {
      isMounted = false;
    };
  }, []);

  // Campaigns for the current filters and page.
  useEffect(() => {
    let isMounted = true;
    setIsPageLoading(true);
    fetchCampaignPage({
      competitorId: selectedCompetitor === 'all' ? undefined : selectedCompetitor,
      platform: selectedPlatform === 'all' ? undefined : selectedPlatform === 'google' ? 'Google' : 'Meta',
      status: selectedStatus === 'all' ? undefined : selectedStatus === 'active' ? 'Active' : 'Inactive',
      page: pageNumber,
      pageSize: PAGE_SIZE,
    })
      .then((data) => {
        if (isMounted) setPage(data);
      })
      .catch((err: Error) => {
        if (isMounted) setError(err.message || 'Failed to load campaigns.');
      })
      .finally(() => {
        if (isMounted) setIsPageLoading(false);
      });
    return () => {
      isMounted = false;
    };
  }, [selectedCompetitor, selectedPlatform, selectedStatus, pageNumber]);

  const handleSyncGoogleAds = async () => {
    setIsSyncing(true);
    setSyncMessage(null);
    setSyncErrors([]);
    try {
      const res = await api.POST('/api/ads/sync', { params: { query: { platform: 'google' } } });
      const data = res.data;
      const checked = data?.identitiesProcessed ?? 0;
      const fetched = data?.totalAdsFetched ?? 0;
      const newCount = data?.newAdsCount ?? 0;
      const errs = data?.errors ?? [];

      setSyncMessage(
        `Google Ads sync complete: ${checked} advertiser identity${checked === 1 ? '' : 'ies'} checked, ${fetched} ads fetched (${newCount} new).`
      );
      if (errs.length > 0) {
        setSyncErrors(errs);
      }
      await loadSummaryAndSources();
      setPageNumber(1);
    } catch (err: any) {
      setSyncMessage(err?.message || 'Failed to sync Google Ads.');
    } finally {
      setIsSyncing(false);
    }
  };

  const togglePreview = (adId: string) => {
    setOpenPreviews((prev) => {
      const next = new Set(prev);
      if (next.has(adId)) {
        next.delete(adId);
      } else {
        next.add(adId);
      }
      return next;
    });
  };

  // Competitors for the filter: the monitored stores, then the reference store.
  const competitorOptions = useMemo(
    () => [...sources].sort((a, b) => Number(a.isReference) - Number(b.isReference) || (a.name || '').localeCompare(b.name || '')),
    [sources]
  );

  // Coverage per store and platform: when it last synced and how many ads the page shows, or the error.
  const coverage = useMemo(() => {
    const rows = summary?.rows ?? [];
    const stores = competitorOptions.map((store) => {
      const google = rows.find((r) => r.competitorId === store.id && r.platform === 'Google');
      const meta = rows.find((r) => r.competitorId === store.id && r.platform === 'Meta');
      const metaIdentity = identities.find(
        (i) => i.competitorId === store.id && (i.platform || '').toLowerCase() === 'meta' && (i.status || '').toLowerCase() === 'approved'
      );
      const metaPageId = metaIdentity?.externalId || SEED_META_PAGE_IDS[store.key?.toLowerCase() || ''];
      return { store, google, meta, metaPageId };
    });
    const total = stores.reduce(
      (acc, s) => ({
        meta: acc.meta + (s.meta?.displayed ?? 0),
        google: acc.google + (s.google?.displayed ?? 0),
      }),
      { meta: 0, google: 0 }
    );
    return { stores, total };
  }, [summary, competitorOptions, identities]);

  // KPI totals over the monitored stores (Lazy Bean is shown separately).
  const kpis = useMemo(() => {
    const monitored = (summary?.rows ?? []).filter((r) => !r.isReference);
    const sum = (pick: (r: AdsSummaryRow) => number) => monitored.reduce((acc, r) => acc + pick(r), 0);
    return {
      ingested: sum((r) => r.ingested),
      stored: sum((r) => r.stored),
      displayed: sum((r) => r.displayed),
      googleAdvertisers: identities.filter(
        (i) => (i.platform || '').toLowerCase() === 'google' && (i.status || '').toLowerCase() === 'approved'
      ).length,
    };
  }, [summary, identities]);

  const approvedIdentitiesCount = useMemo(
    () => identities.filter((i) => (i.status || '').toLowerCase() === 'approved').length,
    [identities]
  );

  // Search runs over the campaigns of the current page.
  const visibleCampaigns = useMemo(() => {
    const items = page?.items ?? [];
    if (!search.trim()) return items;
    const q = search.toLowerCase();
    return items.filter(
      (c) =>
        (c.body || '').toLowerCase().includes(q) ||
        (c.competitorName || '').toLowerCase().includes(q) ||
        (c.identityName || '').toLowerCase().includes(q) ||
        c.ads.some(
          (a) =>
            (a.title || '').toLowerCase().includes(q) ||
            (a.productTitle || '').toLowerCase().includes(q) ||
            (a.lazyBeanProductTitle || '').toLowerCase().includes(q)
        )
    );
  }, [page, search]);

  const totalPages = page ? Math.max(1, Math.ceil(page.totalCount / PAGE_SIZE)) : 1;
  const rangeStart = page && page.totalCount > 0 ? (pageNumber - 1) * PAGE_SIZE + 1 : 0;
  const rangeEnd = page ? Math.min(pageNumber * PAGE_SIZE, page.totalCount) : 0;

  const coverageLabel = (row: AdsSummaryRow | undefined) => {
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
  };

  const renderPositionBadge = (ad: AdVariant) => {
    switch (ad.pricePosition) {
      case 'cheapest':
        return <span className="badge good">Lazy Bean is {Math.abs(ad.priceDiffPercent ?? 0).toFixed(1)}% cheaper</span>;
      case 'more expensive':
        return <span className="badge bad">Competitor is {Math.abs(ad.priceDiffPercent ?? 0).toFixed(1)}% cheaper</span>;
      case 'same price':
        return <span className="badge neutral">Same price as Lazy Bean</span>;
      case 'within 5%':
        return <span className="badge neutral">Prices within 5% ({ad.priceDiffPercent?.toFixed(1)}%)</span>;
      case 'lazyBeanOutOfStock':
        return <span className="badge warn">Lazy Bean out of stock</span>;
      default:
        return <span className="badge neutral">{ad.pricePosition}</span>;
    }
  };

  const renderPrice = (ad: AdVariant) => {
    if (ad.advertisedPrice == null) {
      return <span style={{ color: 'var(--muted)' }}>No price in the ad</span>;
    }
    if (ad.advertisedQuantity && ad.advertisedQuantity > 1) {
      return (
        <strong>
          {ad.advertisedQuantity} for {formatEgp(ad.advertisedPrice)} ({formatEgp(ad.unitPrice)} each)
        </strong>
      );
    }
    return <strong>{formatEgp(ad.advertisedPrice)}</strong>;
  };

  const renderThumbnail = (ad: AdVariant) => {
    const isOpen = openPreviews.has(ad.id);
    const format = (ad.creativeFormat || '').toLowerCase();

    if (format === 'image' && ad.previewUrl) {
      // A preview that cannot be loaded (blocked, or removed by Google) is replaced by a note, not a broken image.
      return (
        <img
          src={ad.previewUrl}
          alt=""
          loading="lazy"
          referrerPolicy="no-referrer"
          onError={(e) => {
            const note = document.createElement('div');
            note.textContent = 'Preview unavailable: open the Google page for the creative.';
            note.style.cssText = 'font-size:12px;color:var(--muted);padding:12px 0';
            e.currentTarget.replaceWith(note);
          }}
          style={{ width: '100%', maxHeight: '220px', objectFit: 'contain', background: '#f8fafc', borderRadius: '4px' }}
        />
      );
    }

    if (format === 'video' && ad.previewUrl) {
      return isOpen ? (
        <iframe
          title={`Video creative ${ad.externalAdId}`}
          srcDoc={videoPreviewDocument(ad.previewUrl)}
          sandbox="allow-scripts"
          loading="lazy"
          referrerPolicy="no-referrer"
          style={{ width: '100%', height: '220px', border: 0, background: '#f8fafc', borderRadius: '4px' }}
        />
      ) : (
        <button type="button" className="btn-xs ghost" onClick={() => togglePreview(ad.id)}>
          Show video preview
        </button>
      );
    }

    return (
      <div style={{ fontSize: '12px', color: 'var(--muted)', padding: '12px 0' }}>
        {format === 'text' ? 'Text creative: open the Google page for the copy.' : 'No preview stored for this creative.'}
      </div>
    );
  };

  const renderVariant = (ad: AdVariant) => {
    const isGoogle = ad.platform === 'Google';
    const isActive = (ad.status || '').toLowerCase() === 'active';

    return (
      <div key={ad.id} className="ad-variant-card">
        <div className="ad-variant-header">
          <span className="badge neutral" style={{ fontSize: '10px' }}>
            {ad.creativeFormat ?? (isGoogle ? 'Creative' : 'Meta ad')}
          </span>
          <span className={`badge ${isActive ? 'good' : 'neutral'}`} style={{ fontSize: '10px' }}>
            {ad.status}
          </span>
        </div>

        <div style={{ margin: '8px 0' }}>{renderThumbnail(ad)}</div>

        <div style={{ fontSize: '11px', color: 'var(--muted)' }}>
          {isGoogle ? `Creative ${ad.externalAdId}` : ad.title || 'Untitled ad'}
        </div>
        <div style={{ fontSize: '11px', color: 'var(--muted)', marginTop: '2px' }}>
          First shown {formatDate(ad.firstSeenAt)} · Last shown {formatDate(ad.lastSeenAt)}
        </div>
        <div style={{ fontSize: '12px', marginTop: '6px' }}>Advertised: {renderPrice(ad)}</div>

        {ad.snapshotUrl && (
          <a
            href={ad.snapshotUrl}
            target="_blank"
            rel="noopener noreferrer"
            style={{ display: 'inline-block', marginTop: '6px', color: 'var(--action)', fontWeight: 600, fontSize: '12px' }}
          >
            {isGoogle ? 'View on Google Ads Transparency ↗' : 'View in Ad Library ↗'}
          </a>
        )}

        {/* Product link: a product, our match, or not linked. */}
        <div className="ad-product-link-box" style={{ marginTop: '8px' }}>
          {ad.productId ? (
            <>
              <div style={{ fontSize: '12px' }}>
                <span style={{ color: 'var(--muted)' }}>Store product: </span>
                <strong>{ad.productTitle}</strong>
                <span style={{ color: 'var(--muted)' }}> · {formatEgp(ad.competitorPrice)}</span>
              </div>
              {ad.lazyBeanProductId ? (
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px', alignItems: 'center', marginTop: '4px' }}>
                  <span style={{ fontSize: '12px' }}>
                    <span style={{ color: 'var(--muted)' }}>Our product: </span>
                    <strong>{ad.lazyBeanProductTitle}</strong>
                    <span style={{ color: 'var(--muted)' }}> · {formatEgp(ad.lazyBeanPrice)}</span>
                  </span>
                  {renderPositionBadge(ad)}
                  {onOpenProductDetail && (
                    <button
                      type="button"
                      className="btn-xs ghost"
                      onClick={() => onOpenProductDetail(ad.lazyBeanProductId!, ad.lazyBeanProductTitle ?? undefined)}
                    >
                      Price history
                    </button>
                  )}
                </div>
              ) : (
                <div style={{ fontSize: '11px', color: 'var(--muted)', marginTop: '4px' }}>No Lazy Bean product matched</div>
              )}
              {ad.hasPriceDiscrepancy && (
                <span className="badge warn" style={{ fontSize: '10px', marginTop: '4px' }}>
                  Ad price differs from the store price
                </span>
              )}
            </>
          ) : (
            <span className="badge neutral" style={{ fontSize: '11px' }}>
              Not linked
            </span>
          )}
        </div>
      </div>
    );
  };

  const renderCampaign = (campaign: AdCampaignCard) => {
    const isActive = (campaign.status || '').toLowerCase() === 'active';
    const isGoogle = campaign.platform === 'Google';
    const metaPageId = SEED_META_PAGE_IDS[(campaign.competitorName || '').toLowerCase().replace(/[^a-z0-9]/g, '-')];

    return (
      <div key={campaign.id} className="campaign-card">
        <div className="campaign-header">
          <div className="campaign-tags">
            <span className="badge neutral" style={{ fontWeight: 700 }}>
              {campaign.competitorName}
            </span>
            <span className="badge" style={{ background: '#eaf1ee', color: '#164f40', fontWeight: 600 }}>
              {campaign.platform} Ads
            </span>
            <span className={`badge ${isActive ? 'good' : 'warn'}`}>{campaign.status}</span>
            <span className="badge" style={{ background: '#f0f3f2' }}>
              {campaign.adCount} creative{campaign.adCount === 1 ? '' : 's'} · {campaign.activeAdCount} active
            </span>
          </div>
          <div style={{ fontSize: '12px', color: 'var(--muted)', display: 'flex', alignItems: 'center', gap: '8px' }}>
            <span>Last shown {formatDate(campaign.lastSeenAt)}</span>
            {!isGoogle && metaPageId && (
              <a
                href={`https://www.facebook.com/ads/library/?active_status=active&ad_type=all&country=EG&view_all_page_id=${encodeURIComponent(metaPageId)}`}
                target="_blank"
                rel="noopener noreferrer"
                style={{ color: 'var(--action)', fontWeight: 600, textDecoration: 'none' }}
              >
                View on Meta Ad Library ↗
              </a>
            )}
          </div>
        </div>

        <div className="campaign-body">
          {isGoogle ? (
            <>
              <strong>{campaign.identityName || campaign.competitorName}</strong>
              <span style={{ color: 'var(--muted)' }}> · Google advertiser</span>
            </>
          ) : campaign.body ? (
            `"${campaign.body}"`
          ) : (
            <span style={{ color: 'var(--muted)' }}>(Visual creative campaign without headline text)</span>
          )}
        </div>

        <div className="campaign-ads-container">
          <div className="campaign-ads-title">Creatives ({campaign.ads.length})</div>
          <div className="ad-variants-grid">{campaign.ads.map(renderVariant)}</div>
        </div>
      </div>
    );
  };

  return (
    <div className="ads-campaigns-view">
      <div className="page-heading" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <div>
          <div className="eyebrow">MARKET INTELLIGENCE</div>
          <h1 className="view-title">Ads & Campaigns</h1>
          <p className="subtitle">
            Competitor ads by advertiser (Google) and creative (Meta), with the price each advertises and its linked product.
          </p>
        </div>
        <button
          className="btn btn-secondary"
          disabled={isSyncing}
          onClick={handleSyncGoogleAds}
          style={{ whiteSpace: 'nowrap', marginTop: '4px' }}
        >
          {isSyncing ? 'Syncing Google Ads...' : 'Sync Google Ads'}
        </button>
      </div>

      <OurAdsSection />

      {syncMessage && (
        <div style={{ padding: '12px 16px', borderRadius: '6px', background: syncErrors.length > 0 ? '#fffbeb' : '#eef2ff', color: syncErrors.length > 0 ? '#92400e' : '#3730a3', border: `1px solid ${syncErrors.length > 0 ? '#fde68a' : '#c7d2fe'}`, marginBottom: '20px', fontSize: '13px' }}>
          <div style={{ fontWeight: 600 }}>{syncMessage}</div>
          {syncErrors.length > 0 && (
            <div style={{ marginTop: '6px', fontSize: '12px' }}>
              <strong>Errors per identity:</strong>
              <ul style={{ margin: '4px 0 0 16px', padding: 0 }}>
                {syncErrors.map((err, idx) => (
                  <li key={idx}>{err}</li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}

      <div className="stats-grid-4">
        <div className="stat-card">
          <div className="stat-number">{isLoading ? <span className="skeleton skeleton-number" /> : kpis.ingested}</div>
          <div className="stat-label">Ingested in the last sync</div>
          <div className="stat-sub">Creatives the collector app sent</div>
        </div>
        <div className="stat-card">
          <div className="stat-number">{isLoading ? <span className="skeleton skeleton-number" /> : kpis.stored}</div>
          <div className="stat-label">Stored creatives</div>
          <div className="stat-sub">Rows kept for the competitors</div>
        </div>
        <div className="stat-card">
          <div className="stat-number">{isLoading ? <span className="skeleton skeleton-number" /> : kpis.displayed}</div>
          <div className="stat-label">Shown on this page</div>
          <div className="stat-sub">Active creatives, Meta and Google</div>
        </div>
        <div className="stat-card">
          <div className="stat-number">{isLoading ? <span className="skeleton skeleton-number" /> : kpis.googleAdvertisers}</div>
          <div className="stat-label">Google advertisers</div>
          <div className="stat-sub">{approvedIdentitiesCount} approved accounts in total</div>
        </div>
      </div>

      {/* Coverage: per store, Google and Meta, with the total shown. */}
      <div className="panel" style={{ marginBottom: '20px', padding: '16px 20px', borderRadius: '8px', border: '1px solid var(--line)', background: '#fff' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
          <h2 style={{ fontSize: '14px', fontWeight: 700, margin: 0, textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--muted)' }}>
            Coverage
          </h2>
          <a href="#/sources" style={{ fontSize: '12px', color: 'var(--action)', fontWeight: 600, textDecoration: 'none' }}>
            Manage sources &gt; Advertiser identities ↗
          </a>
        </div>

        {isLoading ? (
          <div className="skeleton skeleton-row" style={{ height: '24px', width: '100%' }} />
        ) : (
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '13px' }}>
              <thead>
                <tr style={{ borderBottom: '1px solid var(--line)', textAlign: 'left', color: 'var(--muted)', fontSize: '11px', textTransform: 'uppercase' }}>
                  <th style={{ padding: '8px 12px' }}>Store</th>
                  <th style={{ padding: '8px 12px' }}>Google Ads Transparency</th>
                  <th style={{ padding: '8px 12px' }}>Meta Ad Library</th>
                  <th style={{ padding: '8px 12px', textAlign: 'right' }}>Total ads</th>
                  <th style={{ padding: '8px 12px', textAlign: 'right' }}>Official library</th>
                </tr>
              </thead>
              <tbody>
                {coverage.stores.map(({ store, google, meta, metaPageId }) => {
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
                        <span className={`badge ${g.tone === 'bad' ? 'bad' : g.tone === 'good' ? 'good' : 'neutral'}`} style={{ fontSize: '11px' }}>
                          {g.text}
                        </span>
                        {g.detail && <div style={{ fontSize: '10px', color: 'var(--muted)', marginTop: '2px' }}>{g.detail}</div>}
                      </td>
                      <td style={{ padding: '10px 12px' }}>
                        <span className={`badge ${m.tone === 'bad' ? 'bad' : m.tone === 'good' ? 'good' : 'neutral'}`} style={{ fontSize: '11px' }}>
                          {m.text}
                        </span>
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
                  <td style={{ padding: '10px 12px', textAlign: 'right', fontWeight: 700 }}>
                    {coverage.total.google + coverage.total.meta}
                  </td>
                  <td />
                </tr>
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Filters */}
      <div className="matrix-filters-bar" style={{ marginBottom: '20px', borderRadius: '8px', border: '1px solid var(--line)' }}>
        <input
          type="search"
          placeholder="Search this page: ad copy, advertiser, product…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          style={{ padding: '8px 12px', border: '1px solid var(--line)', borderRadius: '6px' }}
        />

        <select
          value={selectedCompetitor}
          onChange={(e) => {
            setSelectedCompetitor(e.target.value);
            setPageNumber(1);
          }}
          style={{ padding: '8px 12px', border: '1px solid var(--line)', borderRadius: '6px' }}
        >
          <option value="all">All stores</option>
          {competitorOptions.map((s) => (
            <option key={s.id} value={s.id}>
              {s.name}
              {s.isReference ? ' (our store)' : ''}
            </option>
          ))}
        </select>

        <select
          value={selectedPlatform}
          onChange={(e) => {
            setSelectedPlatform(e.target.value);
            setPageNumber(1);
          }}
          style={{ padding: '8px 12px', border: '1px solid var(--line)', borderRadius: '6px' }}
        >
          <option value="all">All platforms</option>
          <option value="google">Google Ads Transparency</option>
          <option value="meta">Meta Ads Library</option>
        </select>

        <select
          value={selectedStatus}
          onChange={(e) => {
            setSelectedStatus(e.target.value);
            setPageNumber(1);
          }}
          style={{ padding: '8px 12px', border: '1px solid var(--line)', borderRadius: '6px' }}
        >
          <option value="active">Active (default)</option>
          <option value="inactive">Inactive</option>
          <option value="all">All statuses</option>
        </select>

        <div style={{ fontSize: '13px', color: 'var(--muted)', display: 'flex', alignItems: 'center', justifyContent: 'flex-end' }}>
          <span>
            {page && page.totalCount > 0 ? (
              <>
                Showing <strong>{rangeStart}–{rangeEnd}</strong> of <strong>{page.totalCount}</strong> campaigns
              </>
            ) : (
              'No campaigns'
            )}
          </span>
        </div>
      </div>

      {isLoading ? (
        <div className="campaigns-list">
          <div className="skeleton-card" style={{ marginBottom: '12px' }}>
            <div className="skeleton skeleton-row" style={{ width: '35%', height: '22px' }} />
            <div className="skeleton skeleton-row" style={{ width: '70%', height: '16px' }} />
          </div>
        </div>
      ) : error ? (
        <div className="callout" style={{ borderLeftColor: '#962f32', color: '#962f32' }}>
          {error}
        </div>
      ) : approvedIdentitiesCount === 0 ? (
        <div className="panel" style={{ padding: '48px 24px', textAlign: 'center', background: '#fff', border: '1px solid var(--line)', borderRadius: '8px' }}>
          <h2 style={{ fontSize: '18px', fontWeight: 700, margin: '0 0 8px 0' }}>No approved advertisers yet</h2>
          <p style={{ color: 'var(--muted)', fontSize: '14px', maxWidth: '520px', margin: '0 auto 20px auto', lineHeight: 1.5 }}>
            Approve a Meta page or add a Google advertiser in Manage sources to start tracking ads.
          </p>
          <a href="#/sources" className="btn btn-primary" style={{ display: 'inline-block', textDecoration: 'none', padding: '9px 18px', fontWeight: 600 }}>
            Go to Manage sources &gt; Advertiser identities →
          </a>
        </div>
      ) : isPageLoading && !page ? (
        <div className="panel" style={{ padding: '40px', textAlign: 'center', color: 'var(--muted)' }}>
          Loading campaigns…
        </div>
      ) : visibleCampaigns.length === 0 ? (
        <div className="panel" style={{ padding: '40px', textAlign: 'center', color: 'var(--muted)' }}>
          No campaigns found for these filters.
        </div>
      ) : (
        <>
          <div className="campaigns-list" style={{ opacity: isPageLoading ? 0.5 : 1 }}>
            {visibleCampaigns.map(renderCampaign)}
          </div>

          <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', gap: '12px', marginTop: '16px' }}>
            <button
              type="button"
              className="btn btn-secondary"
              disabled={pageNumber <= 1 || isPageLoading}
              onClick={() => setPageNumber((p) => Math.max(1, p - 1))}
            >
              ← Previous
            </button>
            <span style={{ fontSize: '13px', color: 'var(--muted)' }}>
              Page {pageNumber} of {totalPages}
            </span>
            <button
              type="button"
              className="btn btn-secondary"
              disabled={pageNumber >= totalPages || isPageLoading}
              onClick={() => setPageNumber((p) => Math.min(totalPages, p + 1))}
            >
              Next →
            </button>
          </div>
        </>
      )}
    </div>
  );
};
