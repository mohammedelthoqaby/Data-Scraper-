import React, { useState, useEffect, useMemo } from 'react';
import { api, type AdCampaignDto, type AdDto } from '../../api/client';

interface AdsCampaignsViewProps {
  onOpenProductDetail?: (productId: string, title?: string) => void;
}

export const AdsCampaignsView: React.FC<AdsCampaignsViewProps> = ({ onOpenProductDetail }) => {
  const [campaigns, setCampaigns] = useState<AdCampaignDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [selectedCompetitor, setSelectedCompetitor] = useState<string>('all');
  const [selectedPlatform, setSelectedPlatform] = useState<string>('all');
  const [selectedStatus, setSelectedStatus] = useState<string>('all');
  const [search, setSearch] = useState<string>('');

  // Fetch campaigns from /api/ads/campaigns
  useEffect(() => {
    let isMounted = true;
    const fetchCampaigns = async () => {
      setIsLoading(true);
      setError(null);
      try {
        const res = await api.GET('/api/ads/campaigns');
        if (isMounted && res.data) {
          setCampaigns(res.data);
        }
      } catch (err: any) {
        if (isMounted) setError(err?.message || 'Failed to load campaigns');
      } finally {
        if (isMounted) setIsLoading(false);
      }
    };

    fetchCampaigns();
    return () => {
      isMounted = false;
    };
  }, []);

  // Compute stats
  const stats = useMemo(() => {
    let totalAds = 0;
    let linkedProducts = 0;
    const competitors = new Set<string>();

    campaigns.forEach((c) => {
      totalAds += c.adCount ?? 0;
      if (c.competitorName) competitors.add(c.competitorName);
      c.ads?.forEach((ad) => {
        if (ad.productId) linkedProducts++;
      });
    });

    return {
      totalCampaigns: campaigns.length,
      totalAds,
      linkedProducts,
      competitorCount: competitors.size,
    };
  }, [campaigns]);

  // Distinct competitors
  const competitorOptions = useMemo(() => {
    const names = new Set<string>();
    campaigns.forEach((c) => {
      if (c.competitorName) names.add(c.competitorName);
    });
    return Array.from(names).sort();
  }, [campaigns]);

  // Filtered campaigns
  const filteredCampaigns = useMemo(() => {
    return campaigns.filter((c) => {
      if (selectedCompetitor !== 'all' && c.competitorName !== selectedCompetitor) return false;
      if (selectedPlatform !== 'all' && (c.platform || '').toLowerCase() !== selectedPlatform.toLowerCase()) return false;
      if (selectedStatus !== 'all' && (c.status || '').toLowerCase() !== selectedStatus.toLowerCase()) return false;
      if (search.trim()) {
        const q = search.toLowerCase();
        const body = (c.body || '').toLowerCase();
        const comp = (c.competitorName || '').toLowerCase();
        const adMatch = c.ads?.some((a) =>
          (a.title || '').toLowerCase().includes(q) ||
          (a.productTitle || '').toLowerCase().includes(q) ||
          (a.lazyBeanProductTitle || '').toLowerCase().includes(q)
        );
        if (!body.includes(q) && !comp.includes(q) && !adMatch) return false;
      }
      return true;
    });
  }, [campaigns, selectedCompetitor, selectedPlatform, selectedStatus, search]);

  return (
    <div className="ads-campaigns-view">
      {/* Page Heading */}
      <div className="page-heading">
        <div>
          <div className="eyebrow">MARKET INTELLIGENCE</div>
          <h1 className="view-title">Ads & Campaigns</h1>
          <p className="subtitle">
            Competitor catalog ad campaigns, creative variants, and linked product price comparisons observed across Meta & Google Ad Libraries.
          </p>
        </div>
      </div>

      {/* KPI Cards */}
      <div className="stats-grid-4">
        <div className="stat-card">
          <div className="stat-number">{stats.totalCampaigns}</div>
          <div className="stat-label">Active campaigns</div>
          <div className="stat-sub">Grouped promotional creative sets</div>
        </div>
        <div className="stat-card">
          <div className="stat-number">{stats.totalAds}</div>
          <div className="stat-label">Observed ad creatives</div>
          <div className="stat-sub">Individual product ad variations</div>
        </div>
        <div className="stat-card">
          <div className="stat-number">{stats.linkedProducts}</div>
          <div className="stat-label">Linked to catalog products</div>
          <div className="stat-sub">Mapped ad links to verified SKUs</div>
        </div>
        <div className="stat-card">
          <div className="stat-number">{stats.competitorCount}</div>
          <div className="stat-label">Monitored advertisers</div>
          <div className="stat-sub">Active advertising competitor stores</div>
        </div>
      </div>

      {/* Filters Bar */}
      <div className="matrix-filters-bar" style={{ marginBottom: '20px', borderRadius: '8px', border: '1px solid var(--line)' }}>
        <input
          type="search"
          placeholder="Search ad copy, product titles, or brands..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          style={{ padding: '8px 12px', border: '1px solid var(--line)', borderRadius: '6px' }}
        />

        <select
          value={selectedCompetitor}
          onChange={(e) => setSelectedCompetitor(e.target.value)}
          style={{ padding: '8px 12px', border: '1px solid var(--line)', borderRadius: '6px' }}
        >
          <option value="all">All Competitors ({competitorOptions.length})</option>
          {competitorOptions.map((name) => (
            <option key={name} value={name}>
              {name}
            </option>
          ))}
        </select>

        <select
          value={selectedPlatform}
          onChange={(e) => setSelectedPlatform(e.target.value)}
          style={{ padding: '8px 12px', border: '1px solid var(--line)', borderRadius: '6px' }}
        >
          <option value="all">All Platforms</option>
          <option value="meta">Meta Ads Library</option>
          <option value="google">Google Ads Transparency</option>
        </select>

        <select
          value={selectedStatus}
          onChange={(e) => setSelectedStatus(e.target.value)}
          style={{ padding: '8px 12px', border: '1px solid var(--line)', borderRadius: '6px' }}
        >
          <option value="all">All Statuses</option>
          <option value="active">Active Only</option>
          <option value="inactive">Inactive Only</option>
        </select>

        <div style={{ fontSize: '13px', color: 'var(--muted)', display: 'flex', alignItems: 'center', justifyContent: 'flex-end' }}>
          Showing <strong>{filteredCampaigns.length}</strong> campaigns
        </div>
      </div>

      {/* Content */}
      {isLoading ? (
        <div className="panel" style={{ padding: '40px', textAlign: 'center', color: 'var(--muted)' }}>
          Loading ad campaigns...
        </div>
      ) : error ? (
        <div className="callout" style={{ borderLeftColor: '#962f32', color: '#962f32' }}>
          {error}
        </div>
      ) : filteredCampaigns.length === 0 ? (
        <div className="panel" style={{ padding: '40px', textAlign: 'center', color: 'var(--muted)' }}>
          No campaigns found matching the specified filters.
        </div>
      ) : (
        <div className="campaigns-list">
          {filteredCampaigns.map((campaign) => {
            const isActive = (campaign.status || '').toLowerCase() === 'active';
            const ads = campaign.ads ?? [];

            return (
              <div key={campaign.id} className="campaign-card">
                {/* Campaign Header */}
                <div className="campaign-header">
                  <div className="campaign-tags">
                    <span className="badge neutral" style={{ fontWeight: 700 }}>
                      {campaign.competitorName}
                    </span>
                    <span className="badge" style={{ background: '#eaf1ee', color: '#164f40', fontWeight: 600 }}>
                      {campaign.platform} Ads
                    </span>
                    <span className={`badge ${isActive ? 'good' : 'warn'}`}>
                      {campaign.status}
                    </span>
                    <span className="badge" style={{ background: '#f0f3f2' }}>
                      {campaign.adCount} creative {campaign.adCount === 1 ? 'variant' : 'variants'}
                    </span>
                  </div>

                  <div style={{ fontSize: '12px', color: 'var(--muted)' }}>
                    Last seen:{' '}
                    {campaign.lastSeenAt
                      ? new Date(campaign.lastSeenAt).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
                      : 'Recently'}
                  </div>
                </div>

                {/* Campaign Body */}
                {campaign.body ? (
                  <div className="campaign-body">"{campaign.body}"</div>
                ) : (
                  <div className="campaign-body" style={{ color: 'var(--muted)' }}>
                    (Visual creative campaign without headline text)
                  </div>
                )}

                {/* Ads in this Campaign */}
                <div className="campaign-ads-container">
                  <div className="campaign-ads-title">
                    Catalog Ad Variations ({ads.length})
                  </div>

                  <div className="ad-variants-grid">
                    {ads.map((ad: AdDto) => {
                      const adActive = (ad.status || '').toLowerCase() === 'active';

                      return (
                        <div key={ad.id} className="ad-variant-card">
                          <div>
                            <div className="ad-variant-header">
                              <span className="ad-variant-title">{ad.title || 'Untitled Ad'}</span>
                              <span className={`badge ${adActive ? 'good' : 'neutral'}`} style={{ fontSize: '10px' }}>
                                {ad.status}
                              </span>
                            </div>

                            <div style={{ fontSize: '11px', color: 'var(--muted)', marginTop: '4px' }}>
                              <span>Ad ID: {ad.externalAdId}</span>
                              {ad.snapshotUrl && (
                                <a
                                  href={ad.snapshotUrl}
                                  target="_blank"
                                  rel="noopener noreferrer"
                                  style={{ marginLeft: '8px', color: 'var(--action)', fontWeight: 600 }}
                                >
                                  View in Ad Library ↗
                                </a>
                              )}
                            </div>
                          </div>

                          {/* Linked Product & Price Comparison Box */}
                          {ad.productId ? (
                            <div className="ad-product-link-box">
                              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                <span style={{ fontWeight: 600, color: 'var(--ink)' }}>
                                  Linked Product:
                                </span>
                                {ad.productUrl && (
                                  <a
                                    href={ad.productUrl}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    style={{ color: 'var(--action)', textDecoration: 'none' }}
                                  >
                                    View Store Page ↗
                                  </a>
                                )}
                              </div>
                              <div style={{ color: '#333' }}>{ad.productTitle}</div>

                              {/* Price Comparison Row */}
                              <div className="ad-price-comparison-row">
                                <div>
                                  <span style={{ color: 'var(--muted)' }}>Advertised: </span>
                                  <strong>{ad.competitorPrice != null ? `${ad.competitorPrice.toFixed(2)} EGP` : '—'}</strong>
                                </div>

                                {ad.lazyBeanProductId ? (
                                  <div>
                                    <span style={{ color: 'var(--muted)' }}>Lazy Bean: </span>
                                    <strong>{ad.lazyBeanPrice != null ? `${ad.lazyBeanPrice.toFixed(2)} EGP` : '—'}</strong>
                                  </div>
                                ) : (
                                  <span className="badge neutral" style={{ fontSize: '10px' }}>
                                    Unmatched
                                  </span>
                                )}
                              </div>

                              {/* Price Position Badge */}
                              {ad.lazyBeanProductId && (
                                <div style={{ marginTop: '4px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                  {ad.pricePosition === 'cheapest' ? (
                                    <span className="badge good">
                                      Lazy Bean is {Math.abs(ad.priceDiffPercent ?? 0).toFixed(1)}% cheaper
                                    </span>
                                  ) : ad.pricePosition === 'more expensive' ? (
                                    <span className="badge bad">
                                      Competitor is {Math.abs(ad.priceDiffPercent ?? 0).toFixed(1)}% cheaper
                                    </span>
                                  ) : ad.pricePosition === 'within 5%' ? (
                                    <span className="badge neutral">
                                      Prices within 5% ({ad.priceDiffPercent?.toFixed(1)}%)
                                    </span>
                                  ) : ad.pricePosition === 'lazyBeanOutOfStock' ? (
                                    <span className="badge warn">
                                      Lazy Bean Out of Stock
                                    </span>
                                  ) : (
                                    <span className="badge neutral">{ad.pricePosition}</span>
                                  )}

                                  {onOpenProductDetail && (
                                    <button
                                      type="button"
                                      className="btn-xs ghost"
                                      onClick={() => onOpenProductDetail(ad.lazyBeanProductId!, ad.lazyBeanProductTitle ?? undefined)}
                                    >
                                      Price History
                                    </button>
                                  )}
                                </div>
                              )}
                            </div>
                          ) : (
                            <div style={{ fontSize: '11px', color: 'var(--muted)', fontStyle: 'italic', padding: '6px' }}>
                              Ad links to store homepage or collection page
                            </div>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};
