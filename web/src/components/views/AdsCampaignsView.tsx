import React, { useEffect, useMemo, useState } from 'react';
import { api, type AdvertiserIdentity, type Competitor } from '../../api/client';
import { fetchAdGallery, type AdGalleryCard } from '../../api/adGallery';
import { AdCard } from './AdCard';
import { AdSidePanel } from './AdSidePanel';
import { OurAdsSection } from './OurAdsSection';
import './ads-gallery.css';

interface AdsCampaignsViewProps {
  onOpenProductDetail?: (productId: string, title?: string) => void;
}

type Tab = 'competitor' | 'ours' | 'compare';

const PAGE_SIZE = 24;

const TABS: { key: Tab; label: string }[] = [
  { key: 'competitor', label: 'Competitor ads' },
  { key: 'ours', label: 'Our ads' },
  { key: 'compare', label: 'Compare' },
];

// The ads gallery: competitor ads by default, our own ads on their own tab, and the price comparison on the third.
// Cards are a flat grid of 24 per page. Each card opens the side panel.
export const AdsCampaignsView: React.FC<AdsCampaignsViewProps> = ({ onOpenProductDetail }) => {
  const [tab, setTab] = useState<Tab>('competitor');
  const [sources, setSources] = useState<Competitor[]>([]);
  const [identities, setIdentities] = useState<AdvertiserIdentity[]>([]);
  const [sourcesLoaded, setSourcesLoaded] = useState(false);
  const [activeAds, setActiveAds] = useState<number | null>(null);
  const [adsWithPrice, setAdsWithPrice] = useState<number | null>(null);

  // Filters. The default is Active, so the page shows what is live now.
  const [storeId, setStoreId] = useState('all');
  const [platform, setPlatform] = useState('all');
  const [status, setStatus] = useState('Active');
  const [hasPrice, setHasPrice] = useState(false);
  const [linkedOnly, setLinkedOnly] = useState(false);
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [groupByStore, setGroupByStore] = useState(false);
  const [pageNumber, setPageNumber] = useState(1);

  const [cards, setCards] = useState<AdGalleryCard[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<AdGalleryCard | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const [isSyncing, setIsSyncing] = useState(false);
  const [syncMessage, setSyncMessage] = useState<string | null>(null);
  const [syncErrors, setSyncErrors] = useState<string[]>([]);

  const reference = useMemo(() => sources.find((s) => s.isReference) ?? null, [sources]);
  const competitors = useMemo(
    () => sources.filter((s) => !s.isReference).sort((a, b) => (a.name || '').localeCompare(b.name || '')),
    [sources]
  );
  const approvedAdvertisers = useMemo(
    () => identities.filter((i) => (i.status || '').toLowerCase() === 'approved').length,
    [identities]
  );

  // Stores, advertisers and the two headline numbers. Reloaded after a sync.
  useEffect(() => {
    let alive = true;
    Promise.all([
      api.GET('/api/sources'),
      api.GET('/api/identities'),
      fetchAdGallery({ status: 'Active', pageSize: 1 }),
      fetchAdGallery({ status: 'Active', hasPrice: true, pageSize: 1 }),
    ])
      .then(([srcRes, idRes, active, priced]) => {
        if (!alive) return;
        setSources(srcRes.data ?? []);
        setIdentities(idRes.data ?? []);
        setActiveAds(active.totalAds);
        setAdsWithPrice(priced.totalAds);
      })
      .catch((err: Error) => {
        if (alive) setError(err.message || 'Could not load the ads.');
      })
      .finally(() => {
        if (alive) setSourcesLoaded(true);
      });
    return () => {
      alive = false;
    };
  }, [reloadKey]);

  // Typing in search waits a moment before it reaches the server.
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setDebouncedSearch(search.trim());
      setPageNumber(1);
    }, 300);
    return () => window.clearTimeout(timer);
  }, [search]);

  // Our tab is scoped to the reference store. The competitor tab leaves the scope unset, which the server reads as "every
  // monitored store, never the reference store".
  const ownerId = tab === 'ours' ? reference?.id : storeId !== 'all' ? storeId : undefined;

  useEffect(() => {
    if (tab === 'compare') return;
    if (tab === 'ours' && !reference) {
      setCards([]);
      setTotalCount(0);
      setIsLoading(false);
      return;
    }

    let alive = true;
    setIsLoading(true);
    setError(null);
    fetchAdGallery({
      competitorId: ownerId,
      platform: platform === 'all' ? undefined : platform,
      status: status === 'all' ? undefined : status,
      hasPrice,
      linkedToOurs: linkedOnly,
      search: debouncedSearch,
      sortByStore: groupByStore,
      page: pageNumber,
      pageSize: PAGE_SIZE,
    })
      .then((data) => {
        if (!alive) return;
        setCards(data.items);
        setTotalCount(data.totalCount);
      })
      .catch((err: Error) => {
        if (alive) setError(err.message || 'Could not load the ads.');
      })
      .finally(() => {
        if (alive) setIsLoading(false);
      });
    return () => {
      alive = false;
    };
  }, [tab, ownerId, platform, status, hasPrice, linkedOnly, debouncedSearch, groupByStore, pageNumber, reloadKey]);

  const changeTab = (next: Tab) => {
    setTab(next);
    setSelected(null);
    setPageNumber(1);
  };

  const resetFilters = () => {
    setStoreId('all');
    setPlatform('all');
    setStatus('Active');
    setHasPrice(false);
    setLinkedOnly(false);
    setSearch('');
    setPageNumber(1);
  };

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
      setSyncErrors(errs);
      setPageNumber(1);
      setReloadKey((k) => k + 1);
    } catch (err: any) {
      setSyncMessage(err?.message || 'Failed to sync Google Ads.');
    } finally {
      setIsSyncing(false);
    }
  };

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const rangeStart = totalCount > 0 ? (pageNumber - 1) * PAGE_SIZE + 1 : 0;
  const rangeEnd = Math.min(pageNumber * PAGE_SIZE, totalCount);
  const noAdvertisers = tab === 'competitor' && sourcesLoaded && approvedAdvertisers === 0;

  const stats = [
    { label: 'Active ads', value: activeAds === null ? '…' : activeAds.toLocaleString('en-US'), hint: 'Competitor ads live now' },
    {
      label: 'Advertisers',
      value: sourcesLoaded ? approvedAdvertisers.toLocaleString('en-US') : '…',
      hint: 'Approved accounts we track',
    },
    {
      label: 'Ads with a price',
      value: adsWithPrice === null ? '…' : adsWithPrice.toLocaleString('en-US'),
      hint: 'Live ads that show a price',
    },
  ];

  let content: React.ReactNode;
  if (tab === 'ours' && !reference) {
    content = <div className="ads-empty">No reference store is set up, so there are no ads of ours to show.</div>;
  } else if (error) {
    content = <div className="warning-banner">{error}</div>;
  } else if (isLoading) {
    content = (
      <div className="ads-grid" aria-busy="true">
        {Array.from({ length: 8 }, (_, i) => (
          <div key={i} className="skeleton" style={{ height: 320, borderRadius: 10 }} />
        ))}
      </div>
    );
  } else if (cards.length === 0) {
    content = (
      <div className="ads-empty">
        <p style={{ margin: 0 }}>No ads match these filters.</p>
        <button type="button" className="btn btn-secondary" style={{ marginTop: 12 }} onClick={resetFilters}>
          Reset filters
        </button>
      </div>
    );
  } else {
    content = (
      <>
        <div className="ads-grid">
          {cards.map((card) => (
            <AdCard key={card.id} card={card} onOpen={(c) => setSelected(c)} onOpenProduct={onOpenProductDetail} />
          ))}
        </div>
        <nav className="ads-pager" aria-label="Pages">
          <button
            type="button"
            className="btn btn-secondary"
            disabled={pageNumber <= 1 || isLoading}
            onClick={() => setPageNumber((p) => Math.max(1, p - 1))}
          >
            ← Previous
          </button>
          <span style={{ fontSize: 13, color: 'var(--gallery-muted)' }}>
            Page {pageNumber} of {totalPages}
          </span>
          <button
            type="button"
            className="btn btn-secondary"
            disabled={pageNumber >= totalPages || isLoading}
            onClick={() => setPageNumber((p) => Math.min(totalPages, p + 1))}
          >
            Next →
          </button>
        </nav>
      </>
    );
  }

  return (
    <div className="ads-gallery ads-campaigns-view">
      <div
        className="page-heading"
        style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 12, flexWrap: 'wrap' }}
      >
        <div>
          <div className="eyebrow">MARKET INTELLIGENCE</div>
          <h1 className="view-title">Ads & campaigns</h1>
          <p className="subtitle">
            Competitor ads from Meta and Google, with the price each one advertises and the product of ours it matches.
          </p>
        </div>
        <button
          className="btn btn-secondary"
          disabled={isSyncing}
          onClick={handleSyncGoogleAds}
          style={{ whiteSpace: 'nowrap', marginTop: 4 }}
        >
          {isSyncing ? 'Syncing Google Ads...' : 'Sync Google Ads'}
        </button>
      </div>

      <div className="ads-tabs" role="tablist" aria-label="Ads views">
        {TABS.map((t) => (
          <button
            key={t.key}
            type="button"
            role="tab"
            id={`ads-tab-${t.key}`}
            aria-selected={tab === t.key}
            aria-controls="ads-tabpanel"
            className="ads-tab"
            onClick={() => changeTab(t.key)}
          >
            {t.label}
          </button>
        ))}
      </div>

      {syncMessage && (
        <div
          role="status"
          style={{
            padding: '12px 16px',
            borderRadius: 6,
            background: syncErrors.length > 0 ? '#fffbeb' : '#eef2ff',
            color: syncErrors.length > 0 ? '#92400e' : '#3730a3',
            border: `1px solid ${syncErrors.length > 0 ? '#fde68a' : '#c7d2fe'}`,
            marginBottom: 16,
            fontSize: 13,
          }}
        >
          <div style={{ fontWeight: 600 }}>{syncMessage}</div>
          {syncErrors.length > 0 && (
            <div style={{ marginTop: 6, fontSize: 12 }}>
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

      <div role="tabpanel" id="ads-tabpanel" aria-labelledby={`ads-tab-${tab}`}>
        {tab === 'compare' ? (
          <OurAdsSection />
        ) : noAdvertisers ? (
          <div className="ads-empty" style={{ padding: '48px 24px' }}>
            <h2 style={{ fontSize: 18, fontWeight: 700, margin: '0 0 8px' }}>No approved advertisers yet</h2>
            <p style={{ maxWidth: 520, margin: '0 auto 20px', lineHeight: 1.5 }}>
              Approve a Meta page or add a Google advertiser in Manage sources to start tracking ads.
            </p>
            <a href="#/sources" className="btn btn-primary" style={{ display: 'inline-block', textDecoration: 'none', padding: '9px 18px', fontWeight: 600 }}>
              Go to Manage sources &gt; Advertiser identities →
            </a>
          </div>
        ) : (
          <>
            {tab === 'competitor' && (
              <div className="ads-stats">
                {stats.map((s) => (
                  <div className="ads-stat" key={s.label}>
                    <div className="ads-stat__value">{s.value}</div>
                    <div className="ads-stat__label">{s.label}</div>
                    <div className="ads-stat__label" style={{ marginTop: 2 }}>{s.hint}</div>
                  </div>
                ))}
              </div>
            )}

            <div className="ads-filters">
              <input
                type="search"
                aria-label="Search ads"
                placeholder="Search copy, product or store"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
              {tab === 'competitor' && (
                <select
                  aria-label="Store"
                  value={storeId}
                  onChange={(e) => {
                    setStoreId(e.target.value);
                    setPageNumber(1);
                  }}
                >
                  <option value="all">All stores</option>
                  {competitors.map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name}
                    </option>
                  ))}
                </select>
              )}
              <select
                aria-label="Platform"
                value={platform}
                onChange={(e) => {
                  setPlatform(e.target.value);
                  setPageNumber(1);
                }}
              >
                <option value="all">All platforms</option>
                <option value="Meta">Meta</option>
                <option value="Google">Google</option>
              </select>
              <select
                aria-label="Status"
                value={status}
                onChange={(e) => {
                  setStatus(e.target.value);
                  setPageNumber(1);
                }}
              >
                <option value="Active">Active</option>
                <option value="Inactive">Inactive</option>
                <option value="all">All statuses</option>
              </select>
              <label>
                <input
                  type="checkbox"
                  checked={hasPrice}
                  onChange={(e) => {
                    setHasPrice(e.target.checked);
                    setPageNumber(1);
                  }}
                />
                Has price
              </label>
              <label>
                <input
                  type="checkbox"
                  checked={linkedOnly}
                  onChange={(e) => {
                    setLinkedOnly(e.target.checked);
                    setPageNumber(1);
                  }}
                />
                Linked to our products
              </label>
            </div>

            <div
              style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                flexWrap: 'wrap',
                gap: 8,
                marginBottom: 12,
                fontSize: 13,
                color: 'var(--gallery-muted)',
              }}
            >
              <span>
                {isLoading
                  ? 'Loading…'
                  : totalCount > 0
                    ? `Showing ${rangeStart}–${rangeEnd} of ${totalCount} ads`
                    : 'No ads'}
              </span>
              <label style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                <input
                  type="checkbox"
                  checked={groupByStore}
                  onChange={(e) => {
                    setGroupByStore(e.target.checked);
                    setPageNumber(1);
                  }}
                />
                Group by store
              </label>
            </div>

            {content}
          </>
        )}
      </div>

      {selected && (
        <AdSidePanel key={selected.id} card={selected} onClose={() => setSelected(null)} onOpenProduct={onOpenProductDetail} />
      )}
    </div>
  );
};
