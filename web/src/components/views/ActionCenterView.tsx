import React, { useState, useEffect, useCallback } from 'react';
import {
  fetchAlerts,
  fetchAlertSummary,
  fetchDataHealth,
  syncAlerts,
  performAlertAction,
  fetchAlertHistory,
  type AlertItem,
  type AlertSummary,
  type DataHealthInfo,
  type AlertHistoryEntry,
} from '../../api/alerts';

interface ActionCenterViewProps {
  onOpenProductDetail: (productId: string, title?: string) => void;
  onRefreshAlertCount?: () => void;
}

export const ActionCenterView: React.FC<ActionCenterViewProps> = ({
  onOpenProductDetail,
  onRefreshAlertCount,
}) => {
  const [alerts, setAlerts] = useState<AlertItem[]>([]);
  const [summary, setSummary] = useState<AlertSummary | null>(null);
  const [health, setHealth] = useState<DataHealthInfo | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [isSyncing, setIsSyncing] = useState<boolean>(false);

  // Filters
  const [severityFilter, setSeverityFilter] = useState<string>('All');
  const [statusFilter, setStatusFilter] = useState<string>('Active');
  const [bestsellerOnly, setBestsellerOnly] = useState<boolean>(false);
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [language, setLanguage] = useState<'en' | 'ar'>('en');

  // Dismiss Modal State
  const [dismissingAlertId, setDismissingAlertId] = useState<string | null>(null);
  const [dismissNote, setDismissNote] = useState<string>('');

  // History Modal State
  const [historyAlertId, setHistoryAlertId] = useState<string | null>(null);
  const [historyItems, setHistoryItems] = useState<AlertHistoryEntry[]>([]);
  const [isHistoryLoading, setIsHistoryLoading] = useState<boolean>(false);

  const loadData = useCallback(async () => {
    setIsLoading(true);
    try {
      const [alertList, summaryData, healthData] = await Promise.all([
        fetchAlerts({
          severity: severityFilter,
          status: statusFilter,
          isBestseller: bestsellerOnly ? true : undefined,
          search: searchQuery || undefined,
        }),
        fetchAlertSummary(),
        fetchDataHealth(),
      ]);
      setAlerts(alertList);
      setSummary(summaryData);
      setHealth(healthData);
      onRefreshAlertCount?.();
    } catch (err) {
      console.error('Failed to load Action Center data:', err);
    } finally {
      setIsLoading(false);
    }
  }, [severityFilter, statusFilter, bestsellerOnly, searchQuery, onRefreshAlertCount]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleSync = async () => {
    setIsSyncing(true);
    try {
      await syncAlerts();
      await loadData();
    } catch (err) {
      console.error('Failed to sync alerts:', err);
    } finally {
      setIsSyncing(false);
    }
  };

  const handleAction = async (id: string, action: 'done' | 'snooze' | 'dismiss' | 'reopen', note?: string) => {
    try {
      await performAlertAction(id, action, note);
      await loadData();
    } catch (err) {
      console.error(`Failed to perform action ${action}:`, err);
    }
  };

  const handleOpenDismiss = (id: string) => {
    setDismissingAlertId(id);
    setDismissNote('');
  };

  const handleConfirmDismiss = async () => {
    if (!dismissingAlertId) return;
    await handleAction(dismissingAlertId, 'dismiss', dismissNote);
    setDismissingAlertId(null);
    setDismissNote('');
  };

  const handleOpenHistory = async (id: string) => {
    setHistoryAlertId(id);
    setIsHistoryLoading(true);
    try {
      const entries = await fetchAlertHistory(id);
      setHistoryItems(entries);
    } catch (err) {
      console.error('Failed to fetch alert history:', err);
    } finally {
      setIsHistoryLoading(false);
    }
  };

  const getSeverityBadgeClass = (severity: string) => {
    switch (severity.toLowerCase()) {
      case 'urgent':
        return 'badge-urgent';
      case 'opportunity':
        return 'badge-opportunity';
      case 'watch':
        return 'badge-watch';
      default:
        return 'badge-neutral';
    }
  };

  return (
    <div className={`action-center-view ${language === 'ar' ? 'rtl-mode' : ''}`} dir={language === 'ar' ? 'rtl' : 'ltr'}>
      {/* View Header */}
      <div className="view-header">
        <div>
          <div className="eyebrow">{language === 'ar' ? 'مركز عمليات المدير' : 'MANAGER ACTION CENTER'}</div>
          <h1>{language === 'ar' ? 'تنبيهات تتطلب إجراء' : 'Needs attention'}</h1>
          <p className="subtitle">
            {language === 'ar'
              ? 'تنبيهات فورية مبنية على البيانات لفرص الأسعار والمخزون وحملات المنافسين.'
              : 'Real-time data-driven alerts for competitor price shifts, inventory gaps, and promotional threats.'}
          </p>
        </div>
        <div style={{ display: 'flex', gap: '10px', alignItems: 'center' }}>
          {/* Language Toggle */}
          <div className="lang-toggle-group">
            <button
              type="button"
              className={`lang-btn ${language === 'en' ? 'active' : ''}`}
              onClick={() => setLanguage('en')}
            >
              EN
            </button>
            <button
              type="button"
              className={`lang-btn ${language === 'ar' ? 'active' : ''}`}
              onClick={() => setLanguage('ar')}
            >
              عربي
            </button>
          </div>

          <button
            type="button"
            className="btn btn-secondary"
            onClick={handleSync}
            disabled={isSyncing}
          >
            {isSyncing ? '↻ Re-evaluating...' : '↻ Refresh Alerts'}
          </button>
        </div>
      </div>

      {/* 1. Data Health Strip (Small, distinct status banner) */}
      {health && (
        <div className={`data-health-strip ${health.allHealthy ? 'healthy' : 'warning'}`}>
          <div className="health-item">
            <span className={`status-indicator ${health.failedRunsCount === 0 ? 'green' : 'red'}`} />
            <span>
              <strong>Collection Runs:</strong> {health.failedRunsCount === 0 ? 'All 100% Succeeded' : `${health.failedRunsCount} Failed/Partial`}
            </span>
          </div>
          <div className="health-separator">•</div>
          <div className="health-item">
            <span className={`status-indicator ${health.staleCompetitors.length === 0 ? 'green' : 'amber'}`} />
            <span>
              <strong>Data Freshness:</strong> {health.staleCompetitors.length === 0 ? 'All 5 Stores Fresh (<48h)' : `Stale: ${health.staleCompetitors.join(', ')}`}
            </span>
          </div>
          <div className="health-separator">•</div>
          <div className="health-item">
            <span className={`status-indicator ${health.blockedAdCollectors.length === 0 ? 'green' : 'red'}`} />
            <span>
              <strong>Ad Scrapers:</strong> {health.blockedAdCollectors.length === 0 ? '0 Blocked' : `Blocked: ${health.blockedAdCollectors.join(', ')}`}
            </span>
          </div>
          <div className="health-separator">•</div>
          <div className="health-item">
            <span className={`status-indicator ${health.metaTokenStatus.includes('Active') ? 'green' : 'amber'}`} />
            <span>
              <strong>Meta Ad Token:</strong> {health.metaTokenStatus}
            </span>
          </div>
        </div>
      )}

      {/* 2. KPI Metrics Summary */}
      <div className="action-center-kpis">
        <div
          className={`action-kpi-card urgent ${severityFilter === 'Urgent' ? 'selected' : ''}`}
          onClick={() => setSeverityFilter(severityFilter === 'Urgent' ? 'All' : 'Urgent')}
        >
          <div className="kpi-num">
            {isLoading && !summary ? <span className="skeleton skeleton-number" /> : summary?.urgentCount ?? 0}
          </div>
          <div className="kpi-label">{language === 'ar' ? 'تنبيهات عاجلة' : 'Urgent Alerts'}</div>
          <div className="kpi-hint">{language === 'ar' ? 'فوارق أسعار، إعلانات، ونفاد مخزون' : 'Gaps ≥10%, ad threats, OOS'}</div>
        </div>

        <div
          className={`action-kpi-card opportunity ${severityFilter === 'Opportunity' ? 'selected' : ''}`}
          onClick={() => setSeverityFilter(severityFilter === 'Opportunity' ? 'All' : 'Opportunity')}
        >
          <div className="kpi-num">
            {isLoading && !summary ? <span className="skeleton skeleton-number" /> : summary?.opportunityCount ?? 0}
          </div>
          <div className="kpi-label">{language === 'ar' ? 'فرص ربحية' : 'Opportunities'}</div>
          <div className="kpi-hint">{language === 'ar' ? 'مخزون حصري وهامش زيادة أسعار' : 'Exclusive inventory, margin room'}</div>
        </div>

        <div
          className={`action-kpi-card watch ${severityFilter === 'Watch' ? 'selected' : ''}`}
          onClick={() => setSeverityFilter(severityFilter === 'Watch' ? 'All' : 'Watch')}
        >
          <div className="kpi-num">
            {isLoading && !summary ? <span className="skeleton skeleton-number" /> : summary?.watchCount ?? 0}
          </div>
          <div className="kpi-label">{language === 'ar' ? 'مراقبة ومتابعة' : 'Watch Items'}</div>
          <div className="kpi-hint">{language === 'ar' ? 'حملات خصم وفوارق زمنية > 14 يوماً' : 'Big campaigns, prolonged gaps'}</div>
        </div>

        <div
          className={`action-kpi-card total ${severityFilter === 'All' ? 'selected' : ''}`}
          onClick={() => setSeverityFilter('All')}
        >
          <div className="kpi-num">
            {isLoading && !summary ? <span className="skeleton skeleton-number" /> : summary?.totalActive ?? 0}
          </div>
          <div className="kpi-label">{language === 'ar' ? 'إجمالي النشطة' : 'Total Active'}</div>
          <div className="kpi-hint">{language === 'ar' ? 'تتطلب قراراً من المدير' : 'Requiring manager decision'}</div>
        </div>
      </div>

      {/* 3. Action Filters Bar */}
      <div className="action-filter-bar">
        {/* Status Filter Tabs */}
        <div className="filter-tabs">
          {['Active', 'Done', 'Snoozed', 'Dismissed', 'All'].map((st) => (
            <button
              key={st}
              type="button"
              className={`tab-btn ${statusFilter === st ? 'active' : ''}`}
              onClick={() => setStatusFilter(st)}
            >
              {st}
            </button>
          ))}
        </div>

        {/* Bestseller Toggle */}
        <button
          type="button"
          className={`bestseller-toggle ${bestsellerOnly ? 'active' : ''}`}
          onClick={() => setBestsellerOnly(!bestsellerOnly)}
          title="Filter to Lazy Bean Bestsellers"
        >
          ★ {language === 'ar' ? 'الأكثر مبيعاً فقط' : 'Bestsellers only'}
        </button>

        {/* Search Input */}
        <div className="action-search">
          <input
            type="text"
            placeholder={language === 'ar' ? 'بحث عن منتج، متجر، أو ماركة...' : 'Search product, competitor, or brand...'}
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
          />
        </div>
      </div>

      {/* 4. Alert Cards List */}
      <div className="alert-cards-container">
        {isLoading ? (
          <div>
            <div className="skeleton-card" style={{ marginBottom: '16px' }}>
              <div className="skeleton skeleton-row" style={{ width: '30%', height: '20px' }} />
              <div className="skeleton skeleton-row" style={{ width: '75%', height: '24px' }} />
              <div className="skeleton skeleton-row" style={{ width: '90%', height: '16px' }} />
            </div>
            <div className="skeleton-card" style={{ marginBottom: '16px' }}>
              <div className="skeleton skeleton-row" style={{ width: '30%', height: '20px' }} />
              <div className="skeleton skeleton-row" style={{ width: '75%', height: '24px' }} />
              <div className="skeleton skeleton-row" style={{ width: '90%', height: '16px' }} />
            </div>
          </div>
        ) : alerts.length === 0 ? (
          <div className="empty-state-card">
            <h3>✓ {language === 'ar' ? 'لا توجد تنبيهات تطابق المعايير' : 'No alerts matching criteria'}</h3>
            <p>{language === 'ar' ? 'جميع الأسعار والمخزون في نطاق السياسة المستهدفة.' : 'All competitor prices and inventory levels are within configured thresholds.'}</p>
          </div>
        ) : (
          alerts.map((alert) => {
            const title = language === 'ar' && alert.titleAr ? alert.titleAr : alert.title;
            const reason = language === 'ar' && alert.reasonAr ? alert.reasonAr : alert.reason;
            const action = language === 'ar' && alert.suggestedActionAr ? alert.suggestedActionAr : alert.suggestedAction;

            return (
              <div key={alert.id} className={`alert-card ${alert.severity.toLowerCase()}`}>
                {/* Card Top: Badges & Impact */}
                <div className="alert-card-header">
                  <div className="alert-badges">
                    <span className={`alert-badge ${getSeverityBadgeClass(alert.severity)}`}>
                      {alert.severity}
                    </span>
                    <span className="alert-type-badge">
                      {alert.type.replace(/([A-Z])/g, ' $1').trim()}
                    </span>
                    {alert.isBestseller && (
                      <span className="badge-bestseller">
                        ★ {language === 'ar' ? 'الأكثر مبيعاً' : 'Bestseller'}
                      </span>
                    )}
                    {alert.competitorName && (
                      <span className="badge-competitor">
                        {alert.competitorName}
                      </span>
                    )}
                  </div>

                  <div className="alert-impact-tag">
                    {alert.severity === 'Opportunity'
                      ? (language === 'ar' ? `+${alert.egpImpact.toFixed(2)} ج.م إمكانية ربح` : `+${alert.egpImpact.toFixed(2)} EGP margin`)
                      : (language === 'ar' ? `${alert.egpImpact.toFixed(2)} ج.م فارق` : `${alert.egpImpact.toFixed(2)} EGP gap`)}
                  </div>
                </div>

                {/* Card Body: Title & Factual Reason */}
                <div className="alert-card-body">
                  <h3 className="alert-title">{title}</h3>
                  <p className="alert-reason">{reason}</p>

                  {/* Suggested Action Callout */}
                  <div className="alert-action-box">
                    <strong>{language === 'ar' ? 'الإجراء المقترح:' : 'Suggested Action:'}</strong> {action}
                  </div>
                </div>

                {/* Card Footer: Evidence Links & Actions */}
                <div className="alert-card-footer">
                  <div className="evidence-links">
                    {alert.referenceProductUrl && (
                      <a
                        href={alert.referenceProductUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="evidence-link"
                      >
                        Lazy Bean Product ↗
                      </a>
                    )}
                    {alert.competitorProductUrl && (
                      <a
                        href={alert.competitorProductUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="evidence-link"
                      >
                        {alert.competitorName ?? 'Competitor'} Product ↗
                      </a>
                    )}
                    {alert.adLibraryUrl && (
                      <a
                        href={alert.adLibraryUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="evidence-link ad-link"
                      >
                        Meta Ad Library ↗
                      </a>
                    )}
                    {alert.referenceProductId && (
                      <button
                        type="button"
                        className="evidence-link-btn"
                        onClick={() => onOpenProductDetail(alert.referenceProductId!, alert.referenceProductTitle ?? alert.title)}
                      >
                        Price History & Evidence ↗
                      </button>
                    )}
                  </div>

                  {/* Lifecycle Action Buttons */}
                  <div className="lifecycle-buttons">
                    {alert.status === 'Active' ? (
                      <>
                        <button
                          type="button"
                          className="btn-action btn-done"
                          onClick={() => handleAction(alert.id, 'done')}
                          title="Mark action as completed"
                        >
                          ✓ {language === 'ar' ? 'تم التنفيذ' : 'Done'}
                        </button>
                        <button
                          type="button"
                          className="btn-action btn-snooze"
                          onClick={() => handleAction(alert.id, 'snooze')}
                          title="Snooze for 7 days"
                        >
                          💤 {language === 'ar' ? 'تأجيل 7 أيام' : 'Snooze 7d'}
                        </button>
                        <button
                          type="button"
                          className="btn-action btn-dismiss"
                          onClick={() => handleOpenDismiss(alert.id)}
                          title="Dismiss alert with optional note"
                        >
                          ✕ {language === 'ar' ? 'تجاهل' : 'Dismiss'}
                        </button>
                      </>
                    ) : (
                      <button
                        type="button"
                        className="btn-action btn-reopen"
                        onClick={() => handleAction(alert.id, 'reopen')}
                        title="Reopen alert"
                      >
                        ↺ {language === 'ar' ? 'إعادة تفعيل' : 'Reopen'}
                      </button>
                    )}

                    <button
                      type="button"
                      className="btn-action btn-history"
                      onClick={() => handleOpenHistory(alert.id)}
                      title="View alert lifecycle audit history"
                    >
                      🕒
                    </button>
                  </div>
                </div>
              </div>
            );
          })
        )}
      </div>

      {/* Dismiss with Note Modal */}
      {dismissingAlertId && (
        <div className="modal-backdrop">
          <div className="modal-card">
            <h3>Dismiss Alert</h3>
            <p>Provide an optional reason or context for dismissing this alert:</p>
            <textarea
              className="modal-textarea"
              placeholder="e.g., Seasonal clearance item; supplier renegotiation in progress; deliberate loss-leader."
              rows={3}
              value={dismissNote}
              onChange={(e) => setDismissNote(e.target.value)}
            />
            <div className="modal-actions">
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => setDismissingAlertId(null)}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-primary"
                onClick={handleConfirmDismiss}
              >
                Confirm Dismiss
              </button>
            </div>
          </div>
        </div>
      )}

      {/* History Audit Modal */}
      {historyAlertId && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: '540px' }}>
            <h3>Alert Lifecycle Audit History</h3>
            <div className="history-list">
              {isHistoryLoading ? (
                <p>Loading history...</p>
              ) : historyItems.length === 0 ? (
                <p style={{ color: 'var(--muted)' }}>No history entries recorded yet.</p>
              ) : (
                historyItems.map((item) => (
                  <div key={item.id} className="history-entry">
                    <div className="history-header">
                      <span className="history-action">{item.action}</span>
                      <span className="history-date">
                        {new Date(item.createdAt).toLocaleString()}
                      </span>
                    </div>
                    {item.note && <div className="history-note">"{item.note}"</div>}
                    <div className="history-user">By: {item.performedBy}</div>
                  </div>
                ))
              )}
            </div>
            <div className="modal-actions">
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => setHistoryAlertId(null)}
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
