import React, { useState, useEffect } from 'react';
import { fetchAuditMatches, undoMatch, fetchDataHealth, type MatchAuditItem, type DataHealthInfo } from '../../api/alerts';
import { ReviewQueueView } from './ReviewQueueView';

interface DataQualityViewProps {
  onOpenProductDetail: (productId: string, title?: string) => void;
  onRefreshReviewCount?: () => void;
}

export const DataQualityView: React.FC<DataQualityViewProps> = ({
  onOpenProductDetail,
  onRefreshReviewCount,
}) => {
  const [activeTab, setActiveTab] = useState<'review' | 'audit' | 'health'>('review');
  const [auditMatches, setAuditMatches] = useState<MatchAuditItem[]>([]);
  const [healthInfo, setHealthInfo] = useState<DataHealthInfo | null>(null);
  const [isLoadingAudit, setIsLoadingAudit] = useState<boolean>(false);
  const [auditMessage, setAuditMessage] = useState<string | null>(null);

  const loadAuditData = async () => {
    setIsLoadingAudit(true);
    try {
      const items = await fetchAuditMatches();
      setAuditMatches(items);
    } catch (err) {
      console.error('Failed to load match audit:', err);
    } finally {
      setIsLoadingAudit(false);
    }
  };

  const loadHealthData = async () => {
    try {
      const data = await fetchDataHealth();
      setHealthInfo(data);
    } catch (err) {
      console.error('Failed to load health info:', err);
    }
  };

  useEffect(() => {
    if (activeTab === 'audit') loadAuditData();
    if (activeTab === 'health') loadHealthData();
  }, [activeTab]);

  const handleUndo = async (matchId: string) => {
    try {
      await undoMatch(matchId);
      setAuditMessage('✓ Match successfully reverted and unlinked from Price Matrix.');
      setTimeout(() => setAuditMessage(null), 3000);
      setAuditMatches((prev) => prev.filter((m) => m.matchId !== matchId));
      onRefreshReviewCount?.();
    } catch (err) {
      console.error('Failed to undo match:', err);
    }
  };

  const handleKeep = (matchId: string) => {
    setAuditMessage('✓ Match confirmed in weekly audit.');
    setTimeout(() => setAuditMessage(null), 3000);
    setAuditMatches((prev) => prev.filter((m) => m.matchId !== matchId));
  };

  return (
    <div className="data-quality-view">
      <div className="view-header">
        <div>
          <div className="eyebrow">SETTINGS & DATA GOVERNANCE</div>
          <h1>Data Quality & Match Operations</h1>
          <p className="subtitle">
            Manage matching automation exceptions, inspect weekly AI audit samples, and monitor collection health.
          </p>
        </div>

        {/* Tab Switcher */}
        <div className="tabs-nav">
          <button
            type="button"
            className={`tab-btn ${activeTab === 'review' ? 'active' : ''}`}
            onClick={() => setActiveTab('review')}
          >
            Match Review (Unsure pairs)
          </button>
          <button
            type="button"
            className={`tab-btn ${activeTab === 'audit' ? 'active' : ''}`}
            onClick={() => setActiveTab('audit')}
          >
            Weekly AI Audit (10 Samples)
          </button>
          <button
            type="button"
            className={`tab-btn ${activeTab === 'health' ? 'active' : ''}`}
            onClick={() => setActiveTab('health')}
          >
            Data Health & Tokens
          </button>
        </div>
      </div>

      {auditMessage && (
        <div className="banner banner-success" style={{ marginBottom: '16px' }}>
          {auditMessage}
        </div>
      )}

      {/* Tab 1: Match Review for Unsure Items */}
      {activeTab === 'review' && (
        <div>
          <div className="panel" style={{ marginBottom: '20px', padding: '16px 20px', background: '#f8fafc' }}>
            <p style={{ margin: 0, fontSize: '13px', color: 'var(--muted)' }}>
              ℹ️ <strong>Automated Triage Active:</strong> Pairs with AI verdict <em>Same</em> (≥95%) and matching brand/size/format are auto-approved as <code>AI</code>.
              Pairs with AI verdict <em>Different</em> (≥90%) are auto-rejected. Only borderline or unsure items enter this queue.
            </p>
          </div>

          <ReviewQueueView
            onOpenProductDetail={onOpenProductDetail}
            onRefreshReviewsCount={onRefreshReviewCount}
          />
        </div>
      )}

      {/* Tab 2: Weekly AI Audit (10 Random Matches) */}
      {activeTab === 'audit' && (
        <div className="panel">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
            <div>
              <h3 style={{ margin: 0, fontSize: '16px' }}>Weekly AI-Approved Match Sample (10 Random Pairs)</h3>
              <p style={{ margin: '4px 0 0', fontSize: '13px', color: 'var(--muted)' }}>
                Inspect randomly sampled automated matches to ensure quality integrity. Revert any false positives with "Undo match".
              </p>
            </div>
            <button type="button" className="btn btn-secondary" onClick={loadAuditData} disabled={isLoadingAudit}>
              {isLoadingAudit ? 'Refreshing...' : '↻ Sample New 10'}
            </button>
          </div>

          {isLoadingAudit ? (
            <div style={{ padding: '40px', textAlign: 'center' }}>
              <div className="spinner" />
              <p>Sampling AI matches...</p>
            </div>
          ) : auditMatches.length === 0 ? (
            <div style={{ padding: '30px', textAlign: 'center', color: 'var(--muted)' }}>
              No AI-approved matches available to sample.
            </div>
          ) : (
            <div className="audit-table-wrapper">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Reference Product (Lazy Bean)</th>
                    <th>Competitor Match</th>
                    <th>Score / Method</th>
                    <th>Audit Action</th>
                  </tr>
                </thead>
                <tbody>
                  {auditMatches.map((m) => (
                    <tr key={m.matchId}>
                      <td>
                        <div style={{ fontWeight: 600 }}>{m.referenceTitle}</div>
                        <div style={{ fontSize: '12px', color: 'var(--muted)' }}>
                          Brand: {m.referenceBrand ?? '—'} • {m.referencePrice > 0 ? `${m.referencePrice.toFixed(2)} EGP` : 'Price unlisted'}
                        </div>
                        {m.referenceUrl && (
                          <a href={m.referenceUrl} target="_blank" rel="noopener noreferrer" style={{ fontSize: '12px', color: 'var(--brand)' }}>
                            View Storefront ↗
                          </a>
                        )}
                      </td>
                      <td>
                        <div style={{ fontWeight: 600 }}>{m.competitorTitle}</div>
                        <div style={{ fontSize: '12px', color: 'var(--muted)' }}>
                          Store: <strong>{m.competitorName}</strong> • {m.competitorPrice > 0 ? `${m.competitorPrice.toFixed(2)} EGP` : 'Price unlisted'}
                        </div>
                        {m.competitorUrl && (
                          <a href={m.competitorUrl} target="_blank" rel="noopener noreferrer" style={{ fontSize: '12px', color: 'var(--brand)' }}>
                            View Competitor ↗
                          </a>
                        )}
                      </td>
                      <td>
                        <span className="badge-ai" style={{ padding: '3px 8px', borderRadius: '4px', fontSize: '12px', background: '#ecfdf5', color: '#047857', fontWeight: 600 }}>
                          Method: {m.method} ({(m.score * 100).toFixed(0)}%)
                        </span>
                        <div style={{ fontSize: '11px', color: 'var(--muted)', marginTop: '4px' }}>
                          Approved: {new Date(m.approvedAt).toLocaleDateString()}
                        </div>
                      </td>
                      <td>
                        <div style={{ display: 'flex', gap: '8px' }}>
                          <button
                            type="button"
                            className="btn btn-secondary"
                            style={{ fontSize: '12px', padding: '4px 10px' }}
                            onClick={() => handleKeep(m.matchId)}
                            title="Confirm match quality"
                          >
                            ✓ Keep
                          </button>
                          <button
                            type="button"
                            className="btn btn-secondary"
                            style={{ fontSize: '12px', padding: '4px 10px', color: '#dc2626', borderColor: '#fca5a5' }}
                            onClick={() => handleUndo(m.matchId)}
                            title="Revert match and unlink"
                          >
                            Undo match
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* Tab 3: Data Health */}
      {activeTab === 'health' && healthInfo && (
        <div className="panel" style={{ maxWidth: '780px' }}>
          <h3 style={{ fontSize: '16px', marginBottom: '16px' }}>Collection & API Integration Health</h3>

          <div className="health-details-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '16px' }}>
            <div className="health-card" style={{ padding: '16px', border: '1px solid var(--line)', borderRadius: '8px' }}>
              <div style={{ fontSize: '12px', color: 'var(--muted)', textTransform: 'uppercase' }}>Run Pipeline</div>
              <div style={{ fontSize: '18px', fontWeight: 700, marginTop: '4px', color: healthInfo.failedRunsCount === 0 ? 'var(--brand)' : '#dc2626' }}>
                {healthInfo.failedRunsCount === 0 ? '✓ 100% Operational' : `⚠️ ${healthInfo.failedRunsCount} Failed Runs`}
              </div>
              <div style={{ fontSize: '12px', color: 'var(--muted)', marginTop: '4px' }}>Last 48 hours collection runs</div>
            </div>

            <div className="health-card" style={{ padding: '16px', border: '1px solid var(--line)', borderRadius: '8px' }}>
              <div style={{ fontSize: '12px', color: 'var(--muted)', textTransform: 'uppercase' }}>Scrape Freshness</div>
              <div style={{ fontSize: '18px', fontWeight: 700, marginTop: '4px', color: healthInfo.staleCompetitors.length === 0 ? 'var(--brand)' : '#b45309' }}>
                {healthInfo.staleCompetitors.length === 0 ? '✓ All Stores Fresh' : `⚠️ ${healthInfo.staleCompetitors.length} Stale`}
              </div>
              <div style={{ fontSize: '12px', color: 'var(--muted)', marginTop: '4px' }}>
                {healthInfo.staleCompetitors.length === 0 ? 'All 5 stores observed in <48h' : healthInfo.staleCompetitors.join(', ')}
              </div>
            </div>

            <div className="health-card" style={{ padding: '16px', border: '1px solid var(--line)', borderRadius: '8px' }}>
              <div style={{ fontSize: '12px', color: 'var(--muted)', textTransform: 'uppercase' }}>Ad Scrapers</div>
              <div style={{ fontSize: '18px', fontWeight: 700, marginTop: '4px', color: healthInfo.blockedAdCollectors.length === 0 ? 'var(--brand)' : '#dc2626' }}>
                {healthInfo.blockedAdCollectors.length === 0 ? '✓ Zero Blocked' : `⚠️ ${healthInfo.blockedAdCollectors.length} Blocked`}
              </div>
              <div style={{ fontSize: '12px', color: 'var(--muted)', marginTop: '4px' }}>Page scrapers and ad library connections</div>
            </div>

            <div className="health-card" style={{ padding: '16px', border: '1px solid var(--line)', borderRadius: '8px' }}>
              <div style={{ fontSize: '12px', color: 'var(--muted)', textTransform: 'uppercase' }}>Meta Ad Token</div>
              <div style={{ fontSize: '18px', fontWeight: 700, marginTop: '4px', color: healthInfo.metaTokenStatus.includes('Active') ? 'var(--brand)' : '#dc2626' }}>
                {healthInfo.metaTokenStatus.includes('Active') ? '✓ Active & Valid' : '⚠️ Expired / Invalid'}
              </div>
              <div style={{ fontSize: '12px', color: 'var(--muted)', marginTop: '4px' }}>
                {healthInfo.metaTokenExpiresAt ? `Expires: ${new Date(healthInfo.metaTokenExpiresAt).toLocaleDateString()}` : healthInfo.metaTokenStatus}
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
