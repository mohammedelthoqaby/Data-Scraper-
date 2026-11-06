import React, { useState, useEffect, useMemo } from 'react';
import { api, type AdCampaignDto, type AdDto, type Competitor, type AdvertiserIdentity } from '../../api/client';

interface AdsCampaignsViewProps {
  onOpenProductDetail?: (productId: string, title?: string) => void;
}

const SEED_META_PAGE_IDS: Record<string, string> = {
  'cafelax': '100456846237420',
  'coffee-and-cream': '109786085108938',
  'coffee-zone': '110140451538461',
  'coffee-zone-misr': '110140451538461',
  'fengany': '833564813655781',
  'strong-coffee': '102631558265863',
};

export const AdsCampaignsView: React.FC<AdsCampaignsViewProps> = ({ onOpenProductDetail }) => {
  const [campaigns, setCampaigns] = useState<AdCampaignDto[]>([]);
  const [sources, setSources] = useState<Competitor[]>([]);
  const [identities, setIdentities] = useState<AdvertiserIdentity[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isSyncing, setIsSyncing] = useState(false);
  const [syncMessage, setSyncMessage] = useState<string | null>(null);
  const [syncErrors, setSyncErrors] = useState<string[]>([]);

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

      const campRes = await api.GET('/api/ads/campaigns');
      if (campRes.data) {
        setCampaigns(campRes.data);
      }
      const idRes = await api.GET('/api/identities');
      if (idRes.data) {
        setIdentities(idRes.data);
      }
    } catch (err: any) {
      setSyncMessage(err?.message || 'Failed to sync Google Ads.');
    } finally {
      setIsSyncing(false);
    }
  };

  // Filters
  const [selectedCompetitor, setSelectedCompetitor] = useState<string>('all');
  const [selectedPlatform, setSelectedPlatform] = useState<string>('all');
  const [selectedStatus, setSelectedStatus] = useState<string>('all');
  const [search, setSearch] = useState<string>('');

  // Fetch campaigns, sources, and identities on mount
  useEffect(() => {
    let isMounted = true;
    const fetchData = async () => {
      setIsLoading(true);
      setError(null);
      try {
        const [campRes, srcRes, idRes] = await Promise.all([
          api.GET('/api/ads/campaigns'),
          api.GET('/api/sources'),
          api.GET('/api/identities'),
        ]);

        if (isMounted) {
          if (campRes.data) setCampaigns(campRes.data);
          if (srcRes.data) setSources(srcRes.data);
          if (idRes.data) setIdentities(idRes.data);
        }
      } catch (err: any) {
        if (isMounted) setError(err?.message || 'Failed to load campaigns');
      } finally {
        if (isMounted) setIsLoading(false);
      }
    };

    fetchData();
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

  // Distinct competitors list for filter dropdown - always list all 5 competitors from the competitors table
  const competitorOptions = useMemo(() => {
    const nonRef = sources.filter((s) => !s.isReference);
    if (nonRef.length > 0) {
      return nonRef.map((s) => s.name || s.key).sort();
    }
    // Standard 5 competitors fallback if sources not yet loaded
    return ['CAFELAX', 'Coffee & Cream', 'Coffee Zone Misr', 'Fengany', 'Strong Coffee'];
  }, [sources]);

  // Approved identities count
  const approvedIdentitiesCount = useMemo(() => {
    return identities.filter((i) => (i.status || '').toLowerCase() === 'approved').length;
  }, [identities]);

  // Per-competitor/platform status summary table
  const competitorStatusList = useMemo(() => {
    const nonRef = sources.filter((s) => !s.isReference);
    const storeList = nonRef.length > 0
      ? nonRef
      : [
          { id: '1', key: 'cafelax', name: 'CAFELAX' },
          { id: '2', key: 'coffee-and-cream', name: 'Coffee & Cream' },
          { id: '3', key: 'coffee-zone', name: 'Coffee Zone Misr' },
          { id: '4', key: 'fengany', name: 'Fengany' },
          { id: '5', key: 'strong-coffee', name: 'Strong Coffee' },
        ];

    return storeList.map((store) => {
      const storeName = store.name || store.key;
      const storeIdentities = identities.filter(
        (i) => i.competitorId === store.id || (i.competitorName && i.competitorName.toLowerCase() === storeName.toLowerCase())
      );

      // Meta status & ads
      const metaId = storeIdentities.find((i) => (i.platform || '').toLowerCase() === 'meta');
      const metaStatus = metaId ? metaId.status : 'No identity';
      const metaPageId =
        metaId?.externalId ||
        SEED_META_PAGE_IDS[store.key?.toLowerCase() || ''] ||
        SEED_META_PAGE_IDS[storeName.toLowerCase().replace(/[^a-z0-9]/g, '-')];
      const metaCampaigns = campaigns.filter(
        (c) => (c.competitorName || '').toLowerCase() === storeName.toLowerCase() && (c.platform || '').toLowerCase() === 'meta'
      );
      const metaAdCount = metaCampaigns.reduce((acc, c) => acc + (c.adCount ?? 0), 0);
      let latestMetaSync: string | null = null;
      metaCampaigns.forEach((c) => {
        if (c.lastSeenAt && (!latestMetaSync || c.lastSeenAt > latestMetaSync)) {
          latestMetaSync = c.lastSeenAt;
        }
      });

      // Google status & ads
      const googleId = storeIdentities.find((i) => (i.platform || '').toLowerCase() === 'google');
      const googleStatus = googleId ? googleId.status : 'No identity';
      const googleCampaigns = campaigns.filter(
        (c) => (c.competitorName || '').toLowerCase() === storeName.toLowerCase() && (c.platform || '').toLowerCase() === 'google'
      );
      const googleAdCount = googleCampaigns.reduce((acc, c) => acc + (c.adCount ?? 0), 0);

      // Latest sync time
      const allStoreCampaigns = campaigns.filter((c) => (c.competitorName || '').toLowerCase() === storeName.toLowerCase());
      let latestSync: string | null = null;
      allStoreCampaigns.forEach((c) => {
        if (c.lastSeenAt && (!latestSync || c.lastSeenAt > latestSync)) {
          latestSync = c.lastSeenAt;
        }
      });

      // Meta display label: "Synced <date>, N ads" or "Last sync <date>" (never error/red)
      const metaFormattedDate = latestMetaSync
        ? new Date(latestMetaSync).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })
        : null;

      let metaLabel = 'Not synced yet';
      let metaBadgeClass = 'badge neutral';
      if (metaAdCount > 0 && metaFormattedDate) {
        metaLabel = `Synced ${metaFormattedDate}, ${metaAdCount} ads`;
        metaBadgeClass = 'badge good';
      } else if (metaFormattedDate) {
        metaLabel = `Last sync ${metaFormattedDate}`;
        metaBadgeClass = 'badge neutral';
      }

      return {
        id: store.id,
        name: storeName,
        metaStatus,
        metaPageId,
        metaLabel,
        metaBadgeClass,
        metaAdCount,
        googleStatus,
        googleAdCount,
        totalAds: metaAdCount + googleAdCount,
        latestSync,
      };
    });
  }, [sources, identities, campaigns]);

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

  const renderStatusBadge = (status?: string | null) => {
    const s = (status || 'No identity').toLowerCase();
    if (s === 'approved') return <span className="badge good">Approved</span>;
    if (s === 'pending' || s === 'pendingapproval') return <span className="badge warn">Pending</span>;
    if (s === 'rejected') return <span className="badge bad">Rejected</span>;
    return <span className="badge neutral">No identity</span>;
  };

  return (
    <div className="ads-campaigns-view">
      {/* Page Heading */}
      <div className="page-heading" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <div>
          <div className="eyebrow">MARKET INTELLIGENCE</div>
          <h1 className="view-title">Ads & Campaigns</h1>
          <p className="subtitle">
            Competitor catalog ad campaigns, creative variants, and linked product price comparisons observed across Meta & Google Ad Libraries.
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

      {/* KPI Cards */}
      <div className="stats-grid-4">
        <div className="stat-card">
          <div className="stat-number">
            {isLoading ? <span className="skeleton skeleton-number" /> : stats.totalCampaigns}
          </div>
          <div className="stat-label">Active campaigns</div>
          <div className="stat-sub">Grouped promotional creative sets</div>
        </div>
        <div className="stat-card">
          <div className="stat-number">
            {isLoading ? <span className="skeleton skeleton-number" /> : stats.totalAds}
          </div>
          <div className="stat-label">Observed ad creatives</div>
          <div className="stat-sub">Individual product ad variations</div>
        </div>
        <div className="stat-card">
          <div className="stat-number">
            {isLoading ? <span className="skeleton skeleton-number" /> : stats.linkedProducts}
          </div>
          <div className="stat-label">Linked to catalog products</div>
          <div className="stat-sub">Mapped ad links to verified SKUs</div>
        </div>
        <div className="stat-card">
          <div className="stat-number">
            {isLoading ? <span className="skeleton skeleton-number" /> : stats.competitorCount}
          </div>
          <div className="stat-label">Monitored advertisers</div>
          <div className="stat-sub">Active advertising competitor stores</div>
        </div>
      </div>

      {/* Competitor / Platform Advertiser Status Grid */}
      <div className="panel" style={{ marginBottom: '20px', padding: '16px 20px', borderRadius: '8px', border: '1px solid var(--line)', background: '#fff' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
          <h2 style={{ fontSize: '14px', fontWeight: 700, margin: 0, textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--muted)' }}>
            Advertiser Identities & Platform Coverage
          </h2>
          <a href="#/sources" style={{ fontSize: '12px', color: 'var(--action)', fontWeight: 600, textDecoration: 'none' }}>
            Manage sources &gt; Advertiser identities ↗
          </a>
        </div>

        {isLoading ? (
          <div>
            <div className="skeleton skeleton-row" style={{ height: '24px', width: '100%', marginBottom: '8px' }} />
            <div className="skeleton skeleton-row" style={{ height: '24px', width: '100%', marginBottom: '8px' }} />
            <div className="skeleton skeleton-row" style={{ height: '24px', width: '100%' }} />
          </div>
        ) : (
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '13px' }}>
              <thead>
                <tr style={{ borderBottom: '1px solid var(--line)', textAlign: 'left', color: 'var(--muted)', fontSize: '11px', textTransform: 'uppercase' }}>
                  <th style={{ padding: '8px 12px' }}>Competitor Store</th>
                  <th style={{ padding: '8px 12px' }}>Meta Ad Library</th>
                  <th style={{ padding: '8px 12px' }}>Google Ads Transparency</th>
                  <th style={{ padding: '8px 12px', textAlign: 'center' }}>Total Ads</th>
                  <th style={{ padding: '8px 12px', textAlign: 'right' }}>Official Library</th>
                </tr>
              </thead>
              <tbody>
                {competitorStatusList.map((item) => (
                  <tr key={item.name} style={{ borderBottom: '1px solid #f1f5f9' }}>
                    <td style={{ padding: '10px 12px', fontWeight: 600 }}>{item.name}</td>
                    <td style={{ padding: '10px 12px' }}>
                      <span className={item.metaBadgeClass} style={{ fontSize: '11px' }}>
                        {item.metaLabel}
                      </span>
                    </td>
                    <td style={{ padding: '10px 12px' }}>
                      <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px' }}>
                        {item.googleStatus === 'No identity' ? (
                          <span className="badge neutral">No Google advertiser found</span>
                        ) : (
                          renderStatusBadge(item.googleStatus)
                        )}
                        {item.googleAdCount > 0 && (
                          <span style={{ fontSize: '11px', color: 'var(--muted)' }}>({item.googleAdCount} ads)</span>
                        )}
                      </span>
                    </td>
                    <td style={{ padding: '10px 12px', textAlign: 'center', fontWeight: 600 }}>
                      {item.totalAds > 0 ? item.totalAds : '—'}
                    </td>
                    <td style={{ padding: '10px 12px', textAlign: 'right' }}>
                      {item.metaPageId ? (
                        <a
                          href={`https://www.facebook.com/ads/library/?active_status=active&ad_type=all&country=EG&view_all_page_id=${encodeURIComponent(item.metaPageId)}`}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="btn-xs ghost"
                          style={{
                            textDecoration: 'none',
                            display: 'inline-flex',
                            alignItems: 'center',
                            gap: '4px',
                            fontWeight: 600,
                            padding: '4px 8px',
                            borderRadius: '4px',
                            border: '1px solid var(--line)',
                            background: '#f8fafc',
                            color: 'var(--action)',
                            fontSize: '11px',
                          }}
                          title={`View ${item.name} active ads on Meta Ad Library`}
                        >
                          View on Meta Ad Library ↗
                        </a>
                      ) : (
                        <span style={{ color: 'var(--muted)', fontSize: '11px' }}>—</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
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
          <span>Showing <strong>{filteredCampaigns.length}</strong> campaigns</span>
        </div>
      </div>

      {/* Content */}
      {isLoading ? (
        <div className="campaigns-list">
          <div className="skeleton-card" style={{ marginBottom: '12px' }}>
            <div className="skeleton skeleton-row" style={{ width: '35%', height: '22px' }} />
            <div className="skeleton skeleton-row" style={{ width: '70%', height: '16px' }} />
            <div className="skeleton skeleton-row" style={{ width: '90%', height: '14px' }} />
          </div>
          <div className="skeleton-card" style={{ marginBottom: '12px' }}>
            <div className="skeleton skeleton-row" style={{ width: '35%', height: '22px' }} />
            <div className="skeleton skeleton-row" style={{ width: '70%', height: '16px' }} />
            <div className="skeleton skeleton-row" style={{ width: '90%', height: '14px' }} />
          </div>
        </div>
      ) : error ? (
        <div className="callout" style={{ borderLeftColor: '#962f32', color: '#962f32' }}>
          {error}
        </div>
      ) : approvedIdentitiesCount === 0 ? (
        <div className="panel" style={{ padding: '48px 24px', textAlign: 'center', background: '#fff', border: '1px solid var(--line)', borderRadius: '8px' }}>
          <div style={{ fontSize: '32px', marginBottom: '12px' }}>📢</div>
          <h2 style={{ fontSize: '18px', fontWeight: 700, margin: '0 0 8px 0' }}>No approved advertisers yet</h2>
          <p style={{ color: 'var(--muted)', fontSize: '14px', maxWidth: '520px', margin: '0 auto 20px auto', lineHeight: 1.5 }}>
            No approved advertiser identities found. Approve seeded Meta page IDs or discover Google advertisers in Manage sources to begin tracking campaigns.
          </p>
          <a
            href="#/sources"
            className="btn btn-primary"
            style={{ display: 'inline-block', textDecoration: 'none', padding: '9px 18px', fontWeight: 600 }}
          >
            Go to Manage sources &gt; Advertiser identities →
          </a>
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

                  <div style={{ fontSize: '12px', color: 'var(--muted)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <span>
                      Last seen:{' '}
                      {campaign.lastSeenAt
                        ? new Date(campaign.lastSeenAt).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
                        : 'Recently'}
                    </span>
                    {campaign.platform === 'Meta' && SEED_META_PAGE_IDS[(campaign.competitorName || '').toLowerCase().replace(/[^a-z0-9]/g, '-')] && (
                      <a
                        href={`https://www.facebook.com/ads/library/?active_status=active&ad_type=all&country=EG&view_all_page_id=${encodeURIComponent(SEED_META_PAGE_IDS[(campaign.competitorName || '').toLowerCase().replace(/[^a-z0-9]/g, '-')])}`}
                        target="_blank"
                        rel="noopener noreferrer"
                        style={{ color: 'var(--action)', fontWeight: 600, textDecoration: 'none' }}
                      >
                        View on Meta Ad Library ↗
                      </a>
                    )}
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
                                  <strong>{ad.advertisedPrice != null ? `${ad.advertisedPrice.toFixed(2)} EGP` : '—'}</strong>
                                </div>

                                <div>
                                  <span style={{ color: 'var(--muted)' }}>Store: </span>
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

                                {ad.hasPriceDiscrepancy && (
                                  <span className="badge warn" style={{ fontSize: '10px' }}>
                                    Ad price ≠ store price
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
                                  ) : ad.pricePosition === 'same price' ? (
                                    <span className="badge neutral">Same price as Lazy Bean</span>
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
