import React, { useState, useEffect, useMemo, useRef, useCallback } from 'react';
import { api, type CompetitorOffer, type ReferenceOffer } from '../../api/client';
import { PageHeader } from '../shell/PageHeader';
import { useKeepScroll } from '../shell/useKeepScroll';
import { hashView, readHashParams, writeHashParams } from '../../lib/pageUrlState';

interface OffersViewProps {
  selectedRunId: string | null;
  onOpenProductDetail?: (productId: string, title?: string) => void;
  // The report date bar, shown in the page header row.
  toolbar?: React.ReactNode;
}

export const OffersView: React.FC<OffersViewProps> = ({ selectedRunId, onOpenProductDetail, toolbar }) => {
  // Offer Source: 'competitor' vs 'reference'
  // The tab and filters start from the URL hash, so a reload or a return to the page restores them.
  const [initial] = useState(() => readHashParams());
  const [sourceType, setSourceType] = useState<'competitor' | 'reference'>(() =>
    initial.get('source') === 'reference' ? 'reference' : 'competitor'
  );
  
  // Status filter: 'ongoing' (default on every fresh visit) | 'new' | 'ended' | 'all'. A manual pick holds while
  // the page is open: switching top tabs, Reload and the report date keep it. Leaving the page unmounts this
  // component, so the next visit starts on Ongoing again. An empty Ongoing list stays open; it does not jump.
  const [statusFilter, setStatusFilter] = useState<'all' | 'new' | 'ongoing' | 'ended'>(() => {
    const value = initial.get('status');
    return value === 'new' || value === 'ended' || value === 'all' ? value : 'ongoing';
  });
  
  // Search & Competitor filters
  const [search, setSearch] = useState(() => initial.get('q') ?? '');
  const [selectedCompetitor, setSelectedCompetitor] = useState<string>(() => initial.get('store') ?? 'all');
  
  // The page keeps its scroll position when a tab or pill changes the list.
  const rootRef = useRef<HTMLDivElement>(null);
  const keepScroll = useKeepScroll(rootRef, `${sourceType}|${statusFilter}|${selectedCompetitor}|${search}`);

  // Data state
  const [competitorOffers, setCompetitorOffers] = useState<CompetitorOffer[]>([]);
  const [referenceOffers, setReferenceOffers] = useState<ReferenceOffer[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Fetches the offers for the selected run. Retry calls the same function. A response for a run that is no longer
  // selected is ignored.
  const latestRequest = useRef(0);
  const fetchOffers = useCallback(async () => {
    const request = ++latestRequest.current;
    const isCurrent = () => request === latestRequest.current;
    setIsLoading(true);
    setError(null);
    try {
      if (selectedRunId) {
        // Competitor offers for this run with status=all, then the reference offers.
        const res = await api.GET('/api/runs/{id}/competitor-offers', {
          params: { path: { id: selectedRunId }, query: { status: 'all' } },
        });
        if (isCurrent() && res.data?.offers) {
          setCompetitorOffers(res.data.offers);
        }

        const refRes = await api.GET('/api/runs/{id}/reference-offers', {
          params: { path: { id: selectedRunId } },
        });
        if (isCurrent() && refRes.data?.offers) {
          setReferenceOffers(refRes.data.offers);
        }
      } else {
        // Fallback to the reference offers endpoint without a run id.
        const refRes = await api.GET('/api/reference-offers');
        if (isCurrent() && refRes.data?.offers) {
          setReferenceOffers(refRes.data.offers);
        }
      }
    } catch (err: any) {
      if (isCurrent()) setError(err?.message || 'Failed to load offers');
    } finally {
      if (isCurrent()) setIsLoading(false);
    }
  }, [selectedRunId]);

  useEffect(() => {
    void fetchOffers();
    return () => {
      // Responses that arrive after this run is left are ignored.
      latestRequest.current++;
    };
  }, [fetchOffers]);

  // Compute status counts for competitor offers
  const competitorCounts = useMemo(() => {
    const counts = { all: competitorOffers.length, new: 0, ongoing: 0, ended: 0 };
    for (const off of competitorOffers) {
      const s = (off.status || '').toLowerCase();
      if (s === 'new') counts.new++;
      else if (s === 'ongoing') counts.ongoing++;
      else if (s === 'ended') counts.ended++;
    }
    return counts;
  }, [competitorOffers]);

  // Compute status counts for reference offers
  const referenceCounts = useMemo(() => {
    const counts = { all: referenceOffers.length, new: 0, ongoing: 0, ended: 0 };
    for (const off of referenceOffers) {
      const s = (off.status || '').toLowerCase();
      if (s === 'new') counts.new++;
      else if (s === 'ongoing') counts.ongoing++;
      else if (s === 'ended') counts.ended++;
    }
    return counts;
  }, [referenceOffers]);

  // Distinct competitor names
  const competitorNames = useMemo(() => {
    const names = new Set<string>();
    competitorOffers.forEach((o) => {
      if (o.competitorName) names.add(o.competitorName);
    });
    return Array.from(names).sort();
  }, [competitorOffers]);

  // Filtered active list
  const filteredOffers = useMemo(() => {
    if (sourceType === 'competitor') {
      return competitorOffers.filter((o) => {
        // Status filter
        if (statusFilter !== 'all' && (o.status || '').toLowerCase() !== statusFilter) {
          return false;
        }
        // Competitor filter
        if (selectedCompetitor !== 'all' && o.competitorName !== selectedCompetitor) {
          return false;
        }
        // Search text
        if (search.trim()) {
          const q = search.toLowerCase();
          const title = (o.productTitle || '').toLowerCase();
          const comp = (o.competitorName || '').toLowerCase();
          const brand = (o.brand || '').toLowerCase();
          const vendor = (o.vendor || '').toLowerCase();
          if (!title.includes(q) && !comp.includes(q) && !brand.includes(q) && !vendor.includes(q)) {
            return false;
          }
        }
        return true;
      });
    } else {
      return referenceOffers.filter((o) => {
        // Status filter
        if (statusFilter !== 'all' && (o.status || '').toLowerCase() !== statusFilter) {
          return false;
        }
        // Search text
        if (search.trim()) {
          const q = search.toLowerCase();
          const title = (o.productTitle || '').toLowerCase();
          const brand = (o.brand || '').toLowerCase();
          const vendor = (o.vendor || '').toLowerCase();
          if (!title.includes(q) && !brand.includes(q) && !vendor.includes(q)) {
            return false;
          }
        }
        return true;
      });
    }
  }, [sourceType, competitorOffers, referenceOffers, statusFilter, selectedCompetitor, search]);

  // The tab and filters are written to the URL with replaceState, so Back returns to the previous section and not to the
  // last filter state.
  useEffect(() => {
    writeHashParams(hashView(), {
      source: sourceType === 'reference' ? 'reference' : undefined,
      status: statusFilter === 'ongoing' ? undefined : statusFilter,
      store: selectedCompetitor === 'all' ? undefined : selectedCompetitor,
      q: search.trim() || undefined,
    });
  }, [sourceType, statusFilter, selectedCompetitor, search]);

  // Every matching offer is listed. The list scrolls inside its box, so the page stays put.
  const displayedOffers = filteredOffers;

  return (
    <div className="offers-view" ref={rootRef}>
      <PageHeader
        title="Promotions & Offers"
        info="Review active discounts, newly started promotional deals, and catalog price cuts across monitored coffee stores in Egypt."
        actions={toolbar}
      />

      {/* Top Source Switcher (Competitor vs Reference) */}
      <div className="tab-bar">
        <button
          type="button"
          className={`tab-btn ${sourceType === 'competitor' ? 'active' : ''}`}
          aria-selected={sourceType === 'competitor'}
          onClick={() => {
            keepScroll();
            setSourceType('competitor');
          }}
        >
          Competitor Offers
          <span className="tab-count">{competitorCounts.all}</span>
        </button>
        <button
          type="button"
          className={`tab-btn ${sourceType === 'reference' ? 'active' : ''}`}
          aria-selected={sourceType === 'reference'}
          onClick={() => {
            keepScroll();
            setSourceType('reference');
          }}
        >
          Lazy Bean Reference Offers
          <span className="tab-count">{referenceCounts.all}</span>
        </button>
      </div>

      {/* Status Tabs - Defaults to Ongoing, All is opt-in */}
      <div className="tab-bar" style={{ borderBottom: '1px solid var(--line)', marginBottom: '16px' }}>
        <button
          type="button"
          className={`tab-btn ${statusFilter === 'new' ? 'active' : ''}`}
          onClick={() => { keepScroll(); setStatusFilter('new'); }}
        >
          New (Last 7 Days)
          <span className="tab-count" style={{ background: '#e7f3ec', color: '#1b5238' }}>
            {isLoading ? (
              <span className="skeleton" style={{ width: '16px', height: '12px', display: 'inline-block' }} />
            ) : sourceType === 'competitor' ? (
              competitorCounts.new
            ) : (
              referenceCounts.new
            )}
          </span>
        </button>
        <button
          type="button"
          className={`tab-btn ${statusFilter === 'ongoing' ? 'active' : ''}`}
          onClick={() => { keepScroll(); setStatusFilter('ongoing'); }}
        >
          Ongoing
          <span className="tab-count">
            {isLoading ? (
              <span className="skeleton" style={{ width: '16px', height: '12px', display: 'inline-block' }} />
            ) : sourceType === 'competitor' ? (
              competitorCounts.ongoing
            ) : (
              referenceCounts.ongoing
            )}
          </span>
        </button>
        <button
          type="button"
          className={`tab-btn ${statusFilter === 'ended' ? 'active' : ''}`}
          onClick={() => { keepScroll(); setStatusFilter('ended'); }}
        >
          Ended
          <span className="tab-count">
            {isLoading ? (
              <span className="skeleton" style={{ width: '16px', height: '12px', display: 'inline-block' }} />
            ) : sourceType === 'competitor' ? (
              competitorCounts.ended
            ) : (
              referenceCounts.ended
            )}
          </span>
        </button>
        <button
          type="button"
          className={`tab-btn ${statusFilter === 'all' ? 'active' : ''}`}
          onClick={() => { keepScroll(); setStatusFilter('all'); }}
        >
          All Offers (Opt-in)
          <span className="tab-count">
            {isLoading ? (
              <span className="skeleton" style={{ width: '16px', height: '12px', display: 'inline-block' }} />
            ) : sourceType === 'competitor' ? (
              competitorCounts.all
            ) : (
              referenceCounts.all
            )}
          </span>
        </button>
      </div>

      {/* Filters Bar */}
      <div className="offers-filters-bar">
        <input
          type="search"
          placeholder="Search by product, brand, or vendor..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          style={{ width: '100%', padding: '8px 12px', border: '1px solid var(--line)', borderRadius: '6px' }}
        />

        {sourceType === 'competitor' && (
          <select
            value={selectedCompetitor}
            onChange={(e) => setSelectedCompetitor(e.target.value)}
            style={{ padding: '8px 12px', border: '1px solid var(--line)', borderRadius: '6px' }}
          >
            <option value="all">All Competitors ({competitorNames.length})</option>
            {competitorNames.map((name) => (
              <option key={name} value={name}>
                {name}
              </option>
            ))}
          </select>
        )}

        <div style={{ fontSize: '13px', color: 'var(--muted)', textAlign: 'right', whiteSpace: 'nowrap' }}>
          Showing all <strong>{filteredOffers.length}</strong> offers
        </div>
      </div>

      {/* Offers List */}
      {isLoading && competitorOffers.length === 0 && referenceOffers.length === 0 ? (
        <div className="offers-list">
          <div className="skeleton-card" style={{ marginBottom: '12px' }}>
            <div className="skeleton skeleton-row" style={{ width: '40%', height: '20px' }} />
            <div className="skeleton skeleton-row" style={{ width: '80%', height: '16px' }} />
            <div className="skeleton skeleton-row" style={{ width: '30%', height: '14px' }} />
          </div>
          <div className="skeleton-card" style={{ marginBottom: '12px' }}>
            <div className="skeleton skeleton-row" style={{ width: '40%', height: '20px' }} />
            <div className="skeleton skeleton-row" style={{ width: '80%', height: '16px' }} />
            <div className="skeleton skeleton-row" style={{ width: '30%', height: '14px' }} />
          </div>
          <div className="skeleton-card" style={{ marginBottom: '12px' }}>
            <div className="skeleton skeleton-row" style={{ width: '40%', height: '20px' }} />
            <div className="skeleton skeleton-row" style={{ width: '80%', height: '16px' }} />
            <div className="skeleton skeleton-row" style={{ width: '30%', height: '14px' }} />
          </div>
        </div>
      ) : error ? (
        <div
          className="callout"
          style={{
            borderLeftColor: '#962f32',
            color: '#962f32',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: '12px',
          }}
        >
          <span>{error}</span>
          <button
            type="button"
            className="btn-secondary"
            style={{
              background: '#fff',
              borderColor: '#962f32',
              color: '#962f32',
              fontWeight: 600,
              padding: '4px 12px',
              fontSize: '12px',
              flexShrink: 0,
            }}
            onClick={fetchOffers}
          >
            Retry
          </button>
        </div>
      ) : displayedOffers.length === 0 ? (
        <div className="panel" style={{ padding: '40px', textAlign: 'center', color: 'var(--muted)' }}>
          No offers found matching the current filters.
        </div>
      ) : (
        <div className="offers-list" style={{ maxHeight: 'calc(100vh - 360px)', minHeight: 240, overflowY: 'auto', paddingRight: 4 }}>
          {displayedOffers.map((offer: any) => {
            const isNew = (offer.status || '').toLowerCase() === 'new';
            const isEnded = (offer.status || '').toLowerCase() === 'ended';
            const discountPct = offer.discountPercentage ?? 0;

            return (
              <div key={offer.offerId} className="offer-card">
                <div className="offer-main">
                  <div className="offer-title-row">
                    <span className="badge neutral">{offer.competitorName}</span>

                    {isNew ? (
                      <span className="badge good">New offer</span>
                    ) : isEnded ? (
                      <span className="badge bad">Ended</span>
                    ) : (
                      <span className="badge neutral">Ongoing</span>
                    )}

                    {offer.brand && (
                      <span className="badge" style={{ background: '#f0f3f2' }}>
                        Brand: {offer.brand}
                      </span>
                    )}

                    {offer.vendor && offer.vendor !== offer.brand && (
                      <span className="badge" style={{ background: '#f0f3f2' }}>
                        Vendor: {offer.vendor}
                      </span>
                    )}
                  </div>

                  <a
                    href={offer.canonicalUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="offer-title"
                  >
                    {offer.productTitle} ↗
                  </a>

                  <div className="offer-details">
                    <span>
                      Observed: {new Date(offer.createdAt).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })}
                    </span>
                    {offer.offerType && <span>Type: {offer.offerType}</span>}
                    {offer.productId && onOpenProductDetail && (
                      <button
                        type="button"
                        className="btn-xs ghost"
                        onClick={() => onOpenProductDetail(offer.productId, offer.productTitle)}
                      >
                        Inspect Price History
                      </button>
                    )}
                  </div>

                  {offer.matchedLazyBeanProductTitle && (
                    <div className="offer-matched-ref">
                      <span>Equiv. Own Product:</span>
                      <strong>{offer.matchedLazyBeanProductTitle}</strong>
                    </div>
                  )}
                </div>

                <div className="offer-prices">
                  <div className="offer-current-price">
                    {offer.price?.toFixed(2)} {offer.currency || 'EGP'}
                  </div>
                  {offer.compareAtPrice && offer.compareAtPrice > offer.price && (
                    <div className="offer-compare-price">
                      was {offer.compareAtPrice.toFixed(2)} {offer.currency || 'EGP'}
                    </div>
                  )}
                  {discountPct > 0 && (
                    <div className="offer-discount-pill">
                      -{discountPct.toFixed(0)}% OFF
                    </div>
                  )}
                </div>
              </div>
            );
          })}

        </div>
      )}
    </div>
  );
};
