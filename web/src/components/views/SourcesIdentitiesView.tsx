import React, { useState, useEffect, useMemo } from 'react';
import { api, type Competitor, type AdvertiserIdentity } from '../../api/client';
import { addIdentityFromUrl } from '../../api/ownAds';
import { hashView, readHashParams, writeHashParams } from '../../lib/pageUrlState';
import { GOOGLE_ADVERTISER_ID_ERROR, parseGoogleAdvertiserId } from '../../api/googleAdvertiser';
import { apiErrorMessage } from '../../api/errors';

export const SourcesIdentitiesView: React.FC = () => {
  // The sub-tab is kept in the URL (tab=identities), so a reload or a return opens the same one.
  const [activeTab, setActiveTab] = useState<'sources' | 'identities'>(() =>
    readHashParams().get('tab') === 'identities' ? 'identities' : 'sources'
  );

  // Sources State
  const [sources, setSources] = useState<Competitor[]>([]);
  const [isSourcesLoading, setIsSourcesLoading] = useState(true);

  // Identities State
  const [identities, setIdentities] = useState<AdvertiserIdentity[]>([]);
  const [isIdentitiesLoading, setIsIdentitiesLoading] = useState(true);
  // Filters start from the URL hash, so a reload or a return restores them.
  const [platformFilter, setPlatformFilter] = useState<string>(() => readHashParams().get('platform') ?? 'all');
  const [statusFilter, setStatusFilter] = useState<string>(() => readHashParams().get('status') ?? 'all');
  const [actionNotice, setActionNotice] = useState<string | null>(null);

  // The filters go to the URL with replaceState, so Back returns to the previous section and not to a filter state.
  useEffect(() => {
    writeHashParams(hashView(), {
      tab: activeTab === 'identities' ? 'identities' : undefined,
      platform: platformFilter === 'all' ? undefined : platformFilter,
      status: statusFilter === 'all' ? undefined : statusFilter,
    });
  }, [activeTab, platformFilter, statusFilter]);

  // Discover & Add Identity State
  const [discoveringId, setDiscoveringId] = useState<string | null>(null);
  const [showAddModal, setShowAddModal] = useState<boolean>(false);
  const [newCompId, setNewCompId] = useState<string>('');
  const [newPlatform, setNewPlatform] = useState<string>('Meta');
  const [newExternalId, setNewExternalId] = useState<string>('');
  const [newFacebookUrl, setNewFacebookUrl] = useState<string>('');
  const [isAddingFromLink, setIsAddingFromLink] = useState<boolean>(false);
  const [newName, setNewName] = useState<string>('');
  const [isSubmittingNew, setIsSubmittingNew] = useState<boolean>(false);
  const [formError, setFormError] = useState<string | null>(null);

  // Fetch sources
  const fetchSources = async () => {
    setIsSourcesLoading(true);
    try {
      const res = await api.GET('/api/sources');
      if (res.data) setSources(res.data);
    } catch (err: any) {
      console.error('Failed to fetch sources:', err);
    } finally {
      setIsSourcesLoading(false);
    }
  };

  // Fetch identities
  const fetchIdentities = async () => {
    setIsIdentitiesLoading(true);
    try {
      const res = await api.GET('/api/identities');
      if (res.data) setIdentities(res.data);
    } catch (err: any) {
      console.error('Failed to fetch identities:', err);
    } finally {
      setIsIdentitiesLoading(false);
    }
  };

  useEffect(() => {
    fetchSources();
    fetchIdentities();
  }, []);

  // Toggle source monitoring
  const handleToggleMonitoring = async (source: Competitor) => {
    const nextState = !source.enabled;
    setActionNotice(null);
    try {
      await api.PATCH('/api/sources/{id}', {
        params: { path: { id: source.id! } },
        body: { enabled: nextState },
      });
      setSources((prev) =>
        prev.map((s) => (s.id === source.id ? { ...s, enabled: nextState } : s))
      );
      setActionNotice(`Monitoring ${nextState ? 'enabled' : 'disabled'} for ${source.name}.`);
    } catch (err: any) {
      alert(err?.message || 'Failed to toggle source monitoring');
    }
  };

  // Approve identity
  const handleApproveIdentity = async (id: string, name?: string | null) => {
    setActionNotice(null);
    try {
      await api.POST('/api/identities/{id}/approve', {
        params: { path: { id } },
        body: { notes: 'Approved official advertising account' },
      });
      setIdentities((prev) =>
        prev.map((item) => (item.id === id ? { ...item, status: 'Approved' } : item))
      );
      setActionNotice(`Advertiser account ${name || id} approved.`);
    } catch (err: any) {
      alert(err?.message || 'Failed to approve identity');
    }
  };

  // Reject identity
  const handleRejectIdentity = async (id: string, name?: string | null) => {
    setActionNotice(null);
    try {
      await api.POST('/api/identities/{id}/reject', {
        params: { path: { id } },
        body: { notes: 'Rejected - not an official account' },
      });
      setIdentities((prev) =>
        prev.map((item) => (item.id === id ? { ...item, status: 'Rejected' } : item))
      );
      setActionNotice(`Advertiser account ${name || id} rejected.`);
    } catch (err: any) {
      alert(err?.message || 'Failed to reject identity');
    }
  };

  // Discover Google Advertisers
  const handleDiscoverGoogle = async (source: Competitor) => {
    if (!source.id) return;
    setDiscoveringId(source.id);
    setActionNotice(null);
    try {
      const res = await api.POST('/api/competitors/{id}/discover-google-advertisers', {
        params: { path: { id: source.id } },
      });
      const result = res.data;
      await fetchIdentities();
      if (!result || result.status === 'none') {
        setActionNotice(`No Google advertiser found for ${source.name}.`);
      } else if (result.status === 'found') {
        setActionNotice(
          `Discovered ${result.candidatesFound} Google advertiser candidate(s) for ${source.name}. Review and approve them in the Advertiser Identities tab.`
        );
      } else {
        setActionNotice(result.message ?? `Google discovery did not complete for ${source.name}.`);
      }
    } catch (err: any) {
      alert(err?.message || `Failed to discover Google advertisers for ${source.name}`);
    } finally {
      setDiscoveringId(null);
    }
  };

  // Create manual advertiser identity
  // Adds a Meta page from a Facebook link. A page name is looked up by the desktop app before it is listed.
  const handleAddFromLink = async () => {
    if (!newCompId || !newFacebookUrl.trim()) {
      alert('Choose a competitor and paste a Facebook page link.');
      return;
    }
    setIsAddingFromLink(true);
    try {
      const result = await addIdentityFromUrl(newCompId, newFacebookUrl.trim());
      setActionNotice(result.message);
      setNewFacebookUrl('');
      if (result.status === 'pending' || result.status === 'exists') {
        setShowAddModal(false);
      }
      await fetchIdentities();
    } catch (err: any) {
      alert(err?.message || 'Could not add the Facebook page.');
    } finally {
      setIsAddingFromLink(false);
    }
  };

  const handleCreateManualIdentity = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);
    if (!newCompId) {
      setFormError('Choose the competitor store this account belongs to.');
      return;
    }

    // A Google account is checked before it is sent: the server stores only the advertiser ID, so a pasted link is
    // reduced to its ID first.
    let externalId = newExternalId.trim();
    if (newPlatform === 'Google') {
      const advertiserId = parseGoogleAdvertiserId(externalId);
      if (!advertiserId) {
        setFormError(GOOGLE_ADVERTISER_ID_ERROR);
        return;
      }
      externalId = advertiserId;
    } else if (!externalId) {
      setFormError('Enter the account ID of the Meta page.');
      return;
    }

    setIsSubmittingNew(true);
    setActionNotice(null);
    try {
      const { data, error } = await api.POST('/api/identities', {
        body: {
          competitorId: newCompId,
          platform: newPlatform,
          externalId,
          name: newName.trim() || undefined,
        },
      });
      if (error || !data) {
        // Show the server's own reason rather than failing silently.
        setFormError(apiErrorMessage(error, 'The advertiser account could not be saved.'));
        return;
      }
      await fetchIdentities();
      setShowAddModal(false);
      setNewExternalId('');
      setNewName('');
      setActionNotice(`Added identity '${data.name || data.externalId}' as Pending review.`);
    } catch (err: any) {
      setFormError(apiErrorMessage(err, 'The advertiser account could not be saved.'));
    } finally {
      setIsSubmittingNew(false);
    }
  };

  // Filtered identities
  const filteredIdentities = useMemo(() => {
    return identities.filter((item) => {
      if (platformFilter !== 'all' && (item.platform || '').toLowerCase() !== platformFilter.toLowerCase()) {
        return false;
      }
      if (statusFilter !== 'all' && (item.status || '').toLowerCase() !== statusFilter.toLowerCase()) {
        return false;
      }
      return true;
    });
  }, [identities, platformFilter, statusFilter]);

  return (
    <div className="sources-identities-view">
      {/* Page Heading */}
      <div className="page-heading">
        <div>
          <div className="eyebrow">CONFIGURATION & SCRAPER SETTINGS</div>
          <h1 className="view-title">Manage Sources & Identities</h1>
          <p className="subtitle">
            Manage monitored coffee retail stores, scraper adaptors, and review external advertiser identities across Meta and Google.
          </p>
        </div>
      </div>

      {actionNotice && (
        <div className="warning-banner" style={{ background: '#e7f3ec', borderColor: '#9bc9b1', color: '#1b5238', marginBottom: '16px' }}>
          ✓ {actionNotice}
        </div>
      )}

      {/* Tabs */}
      <div className="tab-bar">
        <button
          type="button"
          className={`tab-btn ${activeTab === 'sources' ? 'active' : ''}`}
          aria-selected={activeTab === 'sources'}
          onClick={() => setActiveTab('sources')}
        >
          Monitored Sources
          <span className="tab-count">{sources.length}</span>
        </button>
        <button
          type="button"
          className={`tab-btn ${activeTab === 'identities' ? 'active' : ''}`}
          aria-selected={activeTab === 'identities'}
          onClick={() => setActiveTab('identities')}
        >
          Advertiser Identities
          <span className="tab-count">{identities.length}</span>
        </button>
      </div>

      {/* Tab 1: Monitored Sources */}
      {activeTab === 'sources' && (
        <div>
          {isSourcesLoading ? (
            <div className="panel" style={{ padding: '40px', textAlign: 'center', color: 'var(--muted)' }}>
              Loading sources...
            </div>
          ) : (
            <div className="sources-grid">
              {sources.map((src) => (
                <div key={src.id} className="source-card">
                  <div>
                    <div className="source-card-header">
                      <div>
                        <div className="source-name">{src.name}</div>
                        <div style={{ fontSize: '11px', color: 'var(--muted)', marginTop: '2px' }}>
                          Key: <code>{src.key}</code>
                        </div>
                      </div>
                      <span className={`badge ${src.isReference ? 'neutral' : 'good'}`} style={{ fontWeight: 700 }}>
                        {src.isReference ? 'Reference Store' : 'Competitor'}
                      </span>
                    </div>

                    <div style={{ marginTop: '12px', display: 'flex', flexDirection: 'column', gap: '6px', fontSize: '13px' }}>
                      <div>
                        <span style={{ color: 'var(--muted)' }}>Platform: </span>
                        <strong>{src.platform}</strong>
                      </div>
                      <div>
                        <span style={{ color: 'var(--muted)' }}>Base URL: </span>
                        <a
                          href={src.baseUrl || '#'}
                          target="_blank"
                          rel="noopener noreferrer"
                          style={{ color: 'var(--action)', wordBreak: 'break-all' }}
                        >
                          {src.baseUrl} ↗
                        </a>
                      </div>
                      <div>
                        <span style={{ color: 'var(--muted)' }}>Rate Limit: </span>
                        <span>{src.minDelayMs ?? 500}ms delay / {src.maxConcurrency ?? 2} concurrent</span>
                      </div>
                    </div>
                  </div>

                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', paddingTop: '12px', borderTop: '1px solid #edf0f2', flexWrap: 'wrap', gap: '8px' }}>
                    <span className={`badge ${src.enabled ? 'good' : 'warn'}`}>
                      {src.enabled ? 'Monitoring Enabled' : 'Monitoring Disabled'}
                    </span>

                    {!src.isReference && (
                      <div style={{ display: 'flex', gap: '8px' }}>
                        <button
                          type="button"
                          className="btn-xs"
                          style={{ background: '#f0f5ff', color: '#1d4ed8', border: '1px solid #c7d2fe', fontWeight: 600 }}
                          disabled={discoveringId === src.id}
                          onClick={() => handleDiscoverGoogle(src)}
                        >
                          {discoveringId === src.id ? 'Discovering...' : '🔍 Discover Google Advertisers'}
                        </button>
                        <button
                          type="button"
                          className={`btn-xs ${src.enabled ? 'reject' : 'approve'}`}
                          onClick={() => handleToggleMonitoring(src)}
                        >
                          {src.enabled ? 'Disable' : 'Enable'}
                        </button>
                      </div>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Tab 2: Advertiser Identities */}
      {activeTab === 'identities' && (
        <div>
          {/* Header Action & Manual Add Button */}
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px', flexWrap: 'wrap', gap: '10px' }}>
            <div style={{ fontSize: '13px', color: 'var(--muted)' }}>
              Manage official advertising accounts across Meta Ad Library and Google Ads Transparency. Only approved accounts are synced.
            </div>
            <button
              type="button"
              className="btn btn-secondary"
              style={{ fontSize: '13px', padding: '6px 14px' }}
              onClick={() => setShowAddModal(!showAddModal)}
            >
              {showAddModal ? '✕ Close Form' : '+ Add Identity Manually'}
            </button>
          </div>

          {/* Manual Add Form */}
          {showAddModal && (
            <div className="panel" style={{ padding: '20px', marginBottom: '20px', border: '1px solid #cbd5e1', borderRadius: '8px', background: '#fafbfc' }}>
              <h3 style={{ margin: '0 0 12px 0', fontSize: '15px', fontWeight: 600 }}>Add Advertiser Identity Manually</h3>
              {formError && (
                <div role="alert" style={{ marginBottom: '12px', padding: '10px 12px', borderRadius: '6px', border: '1px solid #f1b5b5', background: '#fdecec', color: '#8a1f1f', fontSize: '13px' }}>
                  {formError}
                </div>
              )}
              <form onSubmit={handleCreateManualIdentity} style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '12px', alignItems: 'flex-end' }}>
                <div>
                  <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, marginBottom: '4px', color: 'var(--muted)' }}>
                    Competitor Store *
                  </label>
                  <select
                    value={newCompId}
                    onChange={(e) => setNewCompId(e.target.value)}
                    required
                    style={{ width: '100%', padding: '8px 10px', borderRadius: '6px', border: '1px solid var(--line)', background: '#fff' }}
                  >
                    <option value="">Select competitor...</option>
                    {sources.map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.isReference ? `${s.name} (reference store)` : s.name}
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, marginBottom: '4px', color: 'var(--muted)' }}>
                    Platform *
                  </label>
                  <select
                    value={newPlatform}
                    onChange={(e) => setNewPlatform(e.target.value)}
                    style={{ width: '100%', padding: '8px 10px', borderRadius: '6px', border: '1px solid var(--line)', background: '#fff' }}
                  >
                    <option value="Meta">Meta Ads Library</option>
                    <option value="Google">Google Ads Transparency</option>
                  </select>
                </div>

                <div>
                  <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, marginBottom: '4px', color: 'var(--muted)' }}>
                    Account ID / Domain *
                  </label>
                  <input
                    type="text"
                    placeholder={newPlatform === 'Meta' ? 'e.g. 100456846237420' : 'AR followed by 20 digits, or an Ads Transparency link'}
                    value={newExternalId}
                    onChange={(e) => setNewExternalId(e.target.value)}
                    required
                    style={{ width: '100%', padding: '8px 10px', borderRadius: '6px', border: '1px solid var(--line)', background: '#fff' }}
                  />
                </div>

                {newPlatform === 'Google' && (
                  <div style={{ gridColumn: '1 / -1', fontSize: '12px', color: 'var(--muted)', marginTop: '-4px' }}>
                    Use the advertiser ID (AR followed by 20 digits, for example AR01064852753525768193), or paste its Ads Transparency link.
                  </div>
                )}

                <div>
                  <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, marginBottom: '4px', color: 'var(--muted)' }}>
                    Display Name (Optional)
                  </label>
                  <input
                    type="text"
                    placeholder="Defaults to competitor name"
                    value={newName}
                    onChange={(e) => setNewName(e.target.value)}
                    style={{ width: '100%', padding: '8px 10px', borderRadius: '6px', border: '1px solid var(--line)', background: '#fff' }}
                  />
                </div>

                {newPlatform === 'Meta' && (
                  <div>
                    <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, marginBottom: '4px', color: 'var(--muted)' }}>
                      Or paste a Facebook page link
                    </label>
                    <div style={{ display: 'flex', gap: '8px' }}>
                      <input
                        type="url"
                        placeholder="https://www.facebook.com/yourpage"
                        value={newFacebookUrl}
                        onChange={(e) => setNewFacebookUrl(e.target.value)}
                        style={{ flex: 1, padding: '8px 10px', borderRadius: '6px', border: '1px solid var(--line)', background: '#fff' }}
                      />
                      <button
                        type="button"
                        className="btn btn-secondary"
                        disabled={isAddingFromLink || !newCompId}
                        onClick={handleAddFromLink}
                        style={{ padding: '8px 12px', fontSize: '13px' }}
                      >
                        {isAddingFromLink ? 'Adding…' : 'Add from link'}
                      </button>
                    </div>
                  </div>
                )}

                <div>
                  <button
                    type="submit"
                    className="btn btn-primary"
                    disabled={isSubmittingNew}
                    style={{ width: '100%', padding: '9px 14px', fontSize: '13px' }}
                  >
                    {isSubmittingNew ? 'Saving...' : 'Save Identity (Pending)'}
                  </button>
                </div>
              </form>
            </div>
          )}
          {/* Filters Bar */}
          <div className="matrix-filters-bar" style={{ marginBottom: '16px', borderRadius: '8px', border: '1px solid var(--line)' }}>
            <select
              value={platformFilter}
              onChange={(e) => setPlatformFilter(e.target.value)}
              style={{ padding: '8px 12px', border: '1px solid var(--line)', borderRadius: '6px' }}
            >
              <option value="all">All Platforms</option>
              <option value="meta">Meta Ads Library</option>
              <option value="google">Google Ads Transparency</option>
            </select>

            <select
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
              style={{ padding: '8px 12px', border: '1px solid var(--line)', borderRadius: '6px' }}
            >
              <option value="all">All Statuses</option>
              <option value="pending">Pending Review Only</option>
              <option value="approved">Approved Only</option>
              <option value="rejected">Rejected Only</option>
            </select>

            <div style={{ fontSize: '13px', color: 'var(--muted)', display: 'flex', alignItems: 'center', justifyContent: 'flex-end' }}>
              Showing <strong>{filteredIdentities.length}</strong> of <strong>{identities.length}</strong> identities
            </div>
          </div>

          {/* Table */}
          {isIdentitiesLoading ? (
            <div className="panel" style={{ padding: '40px', textAlign: 'center', color: 'var(--muted)' }}>
              Loading identities...
            </div>
          ) : (
            <div className="identity-table-container">
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '13px' }}>
                <thead style={{ background: '#fafbfc', borderBottom: '1px solid var(--line)' }}>
                  <tr>
                    <th style={{ padding: '10px 16px', textAlign: 'left' }}>Status</th>
                    <th style={{ padding: '10px 16px', textAlign: 'left' }}>Competitor</th>
                    <th style={{ padding: '10px 16px', textAlign: 'left' }}>Platform</th>
                    <th style={{ padding: '10px 16px', textAlign: 'left' }}>Account ID / Domain</th>
                    <th style={{ padding: '10px 16px', textAlign: 'left' }}>Public Library Link</th>
                    <th style={{ padding: '10px 16px', textAlign: 'right' }}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredIdentities.map((item) => {
                    const status = (item.status || '').toLowerCase();
                    const isApproved = status === 'approved';
                    const isPending = status === 'pending';
                    const isRejected = status === 'rejected';

                    const libraryUrl =
                      item.platform === 'Meta'
                        ? `https://www.facebook.com/ads/library/?active_status=all&ad_type=all&country=EG&view_all_page_ids=${item.externalId}`
                        : `https://adstransparency.google.com/advertiser/${item.externalId}?region=EG`;

                    return (
                      <tr key={item.id} style={{ borderBottom: '1px solid #edf0f2' }}>
                        <td style={{ padding: '12px 16px' }}>
                          <span className={`badge ${isApproved ? 'good' : isPending ? 'warn' : 'bad'}`}>
                            {item.status}
                          </span>
                        </td>
                        <td style={{ padding: '12px 16px', fontWeight: 600 }}>{item.competitorName}</td>
                        <td style={{ padding: '12px 16px' }}>
                          <span className="badge" style={{ background: '#eaf1ee', color: '#164f40' }}>
                            {item.platform}
                          </span>
                        </td>
                        <td style={{ padding: '12px 16px', fontFamily: 'monospace', fontSize: '12px' }}>
                          {item.externalId}
                        </td>
                        <td style={{ padding: '12px 16px' }}>
                          <a
                            href={libraryUrl}
                            target="_blank"
                            rel="noopener noreferrer"
                            style={{ color: 'var(--action)', fontWeight: 600 }}
                          >
                            Open Ad Library ↗
                          </a>
                        </td>
                        <td style={{ padding: '12px 16px', textAlign: 'right' }}>
                          {isPending ? (
                            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                              <button
                                type="button"
                                className="btn-xs approve"
                                onClick={() => handleApproveIdentity(item.id!, item.competitorName)}
                              >
                                ✓ Approve
                              </button>
                              <button
                                type="button"
                                className="btn-xs reject"
                                onClick={() => handleRejectIdentity(item.id!, item.competitorName)}
                              >
                                ✕ Reject
                              </button>
                            </div>
                          ) : (
                            <span style={{ fontSize: '12px', color: 'var(--muted)' }}>Reviewed</span>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </div>
  );
};
