import React, { useEffect, useMemo, useRef, useState } from 'react';
import { api, type AdvertiserIdentity, type Competitor } from '../../api/client';
import { fetchAdGallery, fetchAdGalleryDetail, type AdGalleryCard } from '../../api/adGallery';
import { AdCard } from './AdCard';
import { AdDetailModal } from './AdDetailModal';
import { OurAdsSection } from './OurAdsSection';
import { PageHeader } from '../shell/PageHeader';
import { useSectionActive } from '../shell/KeepAliveSection';
import { hashView, readHashParams, savedScrollFor, saveScrollFor, writeHashParams } from '../../lib/pageUrlState';
import './ads-gallery.css';

interface AdsCampaignsViewProps {
  onOpenProductDetail?: (productId: string, title?: string) => void;
  // Opens Price comparisons filtered to a product, by its title.
  onOpenPriceComparisons?: (title: string) => void;
  // The report date bar, shown in the page header row.
  toolbar?: React.ReactNode;
}

type Tab = 'competitor' | 'ours' | 'compare';

const TABS: { key: Tab; label: string }[] = [
  { key: 'competitor', label: 'Competitor ads' },
  { key: 'ours', label: 'Our ads' },
  { key: 'compare', label: 'Compare' },
];

const parseTab = (value: string | null): Tab => (value === 'ours' || value === 'compare' ? value : 'competitor');

// The ads gallery: competitor ads by default, our own ads on their own tab, and the price comparison on the third.
// Every matching card is on one page; the browser loads the images as they come into view. Each card opens the ad window.
export const AdsCampaignsView: React.FC<AdsCampaignsViewProps> = ({
  onOpenProductDetail,
  onOpenPriceComparisons,
  toolbar,
}) => {
  // The tab and filters start from the URL hash, so a reload or a return to the page restores them.
  const [initial] = useState(() => readHashParams());
  // Whether this section is on screen. When it is not, the ad window closes and the URL does not change.
  const active = useSectionActive();
  // An ad named in the URL (?ad=<id>) opens once the cards are on screen, so a reload reopens it.
  const pendingAdRef = useRef<string | null>(initial.get('ad'));
  // The element that opened the ad window: focus goes back to it when the window closes.
  const openerRef = useRef<HTMLElement | null>(null);
  const [tab, setTab] = useState<Tab>(() => parseTab(initial.get('tab')));
  const [sources, setSources] = useState<Competitor[]>([]);
  const [identities, setIdentities] = useState<AdvertiserIdentity[]>([]);
  const [sourcesLoaded, setSourcesLoaded] = useState(false);
  const [activeAds, setActiveAds] = useState<number | null>(null);
  const [adsWithPrice, setAdsWithPrice] = useState<number | null>(null);

  // Filters. The default is Active, so the page shows what is live now.
  const [storeId, setStoreId] = useState(() => initial.get('store') ?? 'all');
  const [platform, setPlatform] = useState(() => initial.get('platform') ?? 'all');
  const [status, setStatus] = useState(() => initial.get('status') ?? 'Active');
  const [hasPrice, setHasPrice] = useState(() => initial.get('hasPrice') === '1');
  const [linkedOnly, setLinkedOnly] = useState(() => initial.get('linked') === '1');
  const [search, setSearch] = useState(() => initial.get('q') ?? '');
  const [debouncedSearch, setDebouncedSearch] = useState(() => (initial.get('q') ?? '').trim());
  const [groupByStore, setGroupByStore] = useState(() => initial.get('group') === '1');

  const [cards, setCards] = useState<AdGalleryCard[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [totalAds, setTotalAds] = useState(0);
  const [truncated, setTruncated] = useState(false);
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
      fetchAdGallery({ status: 'Active', countsOnly: true }),
      fetchAdGallery({ status: 'Active', hasPrice: true, countsOnly: true }),
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
    })
      .then((data) => {
        if (!alive) return;
        setCards(data.items);
        setTotalCount(data.totalCount);
        setTotalAds(data.totalAds);
        setTruncated(data.truncated);
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
  }, [tab, ownerId, platform, status, hasPrice, linkedOnly, debouncedSearch, groupByStore, reloadKey]);

  // The ad window's card (?ad=<id>) is written to the URL too, so a reload reopens it.
  const selectedId = selected?.id;

  // The tab, filters and open ad are written to the URL with replaceState, so Back returns to the previous section and not to
  // the last filter state. The hash is kept in a ref for the scroll memory below. Only the section on screen writes: a hidden
  // section writing here would overwrite the hash of the section that is shown.
  const hashRef = useRef('');
  useEffect(() => {
    if (!active) return;
    hashRef.current = writeHashParams('ads-campaigns', {
      tab: tab === 'competitor' ? undefined : tab,
      store: storeId === 'all' ? undefined : storeId,
      platform: platform === 'all' ? undefined : platform,
      status: status === 'Active' ? undefined : status,
      hasPrice: hasPrice ? '1' : undefined,
      linked: linkedOnly ? '1' : undefined,
      q: debouncedSearch || undefined,
      group: groupByStore ? '1' : undefined,
      ad: selectedId,
    });
  }, [active, tab, storeId, platform, status, hasPrice, linkedOnly, debouncedSearch, groupByStore, selectedId]);

  // Leaving the section closes its ad window: the section that is shown next starts with none open.
  useEffect(() => {
    if (!active) setSelected(null);
  }, [active]);

  // Opens the ad named in the URL once the cards are on screen. An ad outside the filtered list is loaded on its own.
  useEffect(() => {
    const adId = pendingAdRef.current;
    if (!adId || !active || isLoading || tab === 'compare') return;
    pendingAdRef.current = null;
    const found = cards.find((card) => card.id === adId);
    if (found) {
      setSelected(found);
      return;
    }
    fetchAdGalleryDetail(adId)
      .then((detail) => setSelected(detail.card))
      .catch(() => undefined);
  }, [active, isLoading, cards, tab]);

  // Closing the ad window gives focus back to the card that opened it. The page does not scroll for that.
  useEffect(() => {
    if (selected) return;
    const opener = openerRef.current;
    openerRef.current = null;
    if (opener?.isConnected) opener.focus({ preventScroll: true });
  }, [selected]);

  // Restores the scroll position of this URL once the cards are on screen. Saved positions are remembered per URL.
  const restoredRef = useRef(false);
  useEffect(() => {
    if (isLoading || restoredRef.current) return;
    restoredRef.current = true;
    const saved = savedScrollFor(hashRef.current);
    if (saved !== null && saved > 0) {
      window.scrollTo(0, saved);
    }
  }, [isLoading]);

  // Remembers where the user is on this page. Only while this page is the active view, so the position is not lost when
  // the page is left and the browser shortens it.
  useEffect(() => {
    let timer: number | undefined;
    const remember = () => {
      if (hashView() === 'ads-campaigns') {
        saveScrollFor(hashRef.current);
      }
    };
    const onScroll = () => {
      // Scroll events while the page is still loading describe a short page, not the user's place. Ignore them until the
      // saved position has been restored.
      if (!restoredRef.current) return;
      window.clearTimeout(timer);
      timer = window.setTimeout(remember, 150);
    };
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => {
      window.removeEventListener('scroll', onScroll);
      window.clearTimeout(timer);
    };
  }, []);

  const changeTab = (next: Tab) => {
    setTab(next);
    setSelected(null);
  };

  const resetFilters = () => {
    setStoreId('all');
    setPlatform('all');
    setStatus('Active');
    setHasPrice(false);
    setLinkedOnly(false);
    setSearch('');
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
      setReloadKey((k) => k + 1);
    } catch (err: any) {
      setSyncMessage(err?.message || 'Failed to sync Google Ads.');
    } finally {
      setIsSyncing(false);
    }
  };

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

  // The open ad's place in the filtered list, which Previous and Next follow.
  const selectedIndex = selected ? cards.findIndex((card) => card.id === selected.id) : -1;
  const stepSelected = (delta: number) => {
    const next = selectedIndex >= 0 ? cards[selectedIndex + delta] : undefined;
    if (next) setSelected(next);
  };

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
            <AdCard
              key={card.id}
              card={card}
              onOpen={(c, opener) => {
                openerRef.current = opener;
                setSelected(c);
              }}
              onOpenProduct={onOpenProductDetail}
            />
          ))}
        </div>
      </>
    );
  }

  return (
    <div className="ads-gallery ads-campaigns-view">
      <PageHeader
        title="Ads & campaigns"
        info="Competitor ads from Meta and Google, with the price each one advertises and the product of ours it matches."
        actions={
          <>
            {toolbar}
            <button className="btn btn-secondary" disabled={isSyncing} onClick={handleSyncGoogleAds}>
              {isSyncing ? 'Syncing Google Ads...' : 'Sync Google Ads'}
            </button>
          </>
        }
      />

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
                  onChange={(e) => setStoreId(e.target.value)}
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
                onChange={(e) => setPlatform(e.target.value)}
              >
                <option value="all">All platforms</option>
                <option value="Meta">Meta</option>
                <option value="Google">Google</option>
              </select>
              <select
                aria-label="Status"
                value={status}
                onChange={(e) => setStatus(e.target.value)}
              >
                <option value="Active">Active</option>
                <option value="Inactive">Inactive</option>
                <option value="all">All statuses</option>
              </select>
              <label>
                <input
                  type="checkbox"
                  checked={hasPrice}
                  onChange={(e) => setHasPrice(e.target.checked)}
                />
                Has price
              </label>
              <label>
                <input
                  type="checkbox"
                  checked={linkedOnly}
                  onChange={(e) => setLinkedOnly(e.target.checked)}
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
                    ? `All ${totalCount.toLocaleString('en-US')} ${totalCount === 1 ? 'card' : 'cards'} · ${totalAds.toLocaleString('en-US')} ads`
                    : 'No ads'}
              </span>
              <label style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                <input
                  type="checkbox"
                  checked={groupByStore}
                  onChange={(e) => setGroupByStore(e.target.checked)}
                />
                Group by store
              </label>
            </div>

            {truncated && !isLoading && (
              <div className="warning-banner" style={{ marginBottom: 12 }}>
                The filters match more ads than the page reads at once, so only the newest are shown. Narrow the filters to see the rest.
              </div>
            )}
            {content}
          </>
        )}
      </div>

      {selected && (
        <AdDetailModal
          card={selected}
          position={selectedIndex >= 0 ? { index: selectedIndex, total: cards.length } : null}
          canPrevious={selectedIndex > 0}
          canNext={selectedIndex >= 0 && selectedIndex < cards.length - 1}
          onPrevious={() => stepSelected(-1)}
          onNext={() => stepSelected(1)}
          onClose={() => setSelected(null)}
          onOpenPriceComparisons={onOpenPriceComparisons}
        />
      )}
    </div>
  );
};
