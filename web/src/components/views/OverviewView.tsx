import React, { useState, useEffect } from 'react';
import type { PriceMatrixResponse, CompetitorOffersResponse } from '../../api/client';
import { aiApi, type RankedAttentionItem } from '../../api/ai';
import { fetchAlerts, type AlertItem } from '../../api/alerts';

interface OverviewViewProps {
  matrix: PriceMatrixResponse | null;
  offers: CompetitorOffersResponse | null;
  reviewCount: number;
  onNavigate: (view: string) => void;
  onRefreshCollection: () => void;
}

export const OverviewView: React.FC<OverviewViewProps> = ({
  matrix,
  offers,
  reviewCount,
  onNavigate,
  onRefreshCollection,
}) => {
  const totalComparisons = matrix?.totalRows ?? 0;
  const suggestedActionsCount = matrix?.rows?.filter(r => r.position === 'more expensive' || r.position === 'lazyBeanOutOfStock').length ?? 0;

  // Urgent Alerts from Action Center
  const [urgentAlerts, setUrgentAlerts] = useState<AlertItem[]>([]);
  const [totalUrgentCount, setTotalUrgentCount] = useState<number>(0);
  const [rankedAttention, setRankedAttention] = useState<RankedAttentionItem[]>([]);
  const [lang, setLang] = useState<'en' | 'ar'>('en');
  const [isLoadingAttention, setIsLoadingAttention] = useState(true);

  useEffect(() => {
    let isMounted = true;
    fetchAlerts({ severity: 'Urgent', status: 'Active' })
      .then(items => {
        if (isMounted) {
          setTotalUrgentCount(items.length);
          setUrgentAlerts(items.slice(0, 5));
        }
      })
      .catch(err => console.warn('Failed to load urgent alerts on overview:', err));

    const fetchAttention = async () => {
      setIsLoadingAttention(true);
      try {
        const items = await aiApi.getAttentionItems();
        if (isMounted) {
          setRankedAttention(items);
        }
      } catch (err) {
        console.warn('Failed to fetch AI ranked attention items, falling back to local matrix:', err);
      } finally {
        if (isMounted) setIsLoadingAttention(false);
      }
    };
    fetchAttention();
    return () => { isMounted = false; };
  }, [matrix]);

  // Fallback items from matrix if API items not loaded
  const fallbackAttentionItems = matrix?.rows
    ?.filter(r => r.position === 'more expensive' || r.position === 'lazyBeanOutOfStock')
    .slice(0, 5) ?? [];

  // Sample competitor changes from offers
  const recentChanges = offers?.offers?.slice(0, 3) ?? [];

  return (
    <div className="overview-view">
      {/* Page Heading */}
      <div className="page-heading">
        <div>
          <div className="eyebrow">OPERATOR DASHBOARD</div>
          <h1 className="view-title">Overview</h1>
          <p className="subtitle">See what needs attention this week across monitored coffee stores in Egypt.</p>
        </div>
        <button type="button" className="btn-primary" onClick={onRefreshCollection}>
          ▶ Run Collection
        </button>
      </div>


      {/* 3 KPI Summary Cards */}
      <div className="stats-grid-3">
        <div className="stat-card" onClick={() => onNavigate('price-comparisons')}>
          <div className="stat-number">
            {matrix ? suggestedActionsCount : <span className="skeleton skeleton-number" />}
          </div>
          <div className="stat-label">Suggested actions</div>
          <div className="stat-sub">Prices needing review or restock</div>
        </div>

        <div className="stat-card" onClick={() => onNavigate('price-comparisons')}>
          <div className="stat-number">
            {matrix ? totalComparisons : <span className="skeleton skeleton-number" />}
          </div>
          <div className="stat-label">Supported price comparisons</div>
          <div className="stat-sub">Verified matching products</div>
        </div>

        <div className="stat-card" onClick={() => onNavigate('attention')}>
          <div className="stat-number">
            {isLoadingAttention && totalUrgentCount === 0 && reviewCount === 0 ? (
              <span className="skeleton skeleton-number" />
            ) : totalUrgentCount > 0 ? (
              totalUrgentCount
            ) : (
              reviewCount
            )}
          </div>
          <div className="stat-label">Urgent action alerts</div>
          <div className="stat-sub">Action Center items requiring decision</div>
        </div>
      </div>

      {/* Changes at Competitors */}
      <div className="panel">
        <div className="panel-header">
          <h2 className="panel-title">Changes at competitors</h2>
          <button
            type="button"
            className="btn-secondary"
            style={{ fontSize: '12px', padding: '4px 10px' }}
            onClick={() => onNavigate('price-comparisons')}
          >
            View all
          </button>
        </div>

        {offers === null ? (
          <div style={{ padding: '12px 0' }}>
            <div className="skeleton skeleton-row" />
            <div className="skeleton skeleton-row" />
            <div className="skeleton skeleton-row" />
          </div>
        ) : recentChanges.length > 0 ? (
          recentChanges.map((change) => (
            <div key={change.offerId} className="decision-row">
              <div className="decision-content">
                <div className="decision-title">{change.productTitle}</div>
                <div className="decision-meta">
                  <span className="badge neutral">{change.competitorName}</span>
                  <span>
                    Price: <strong>{change.price.toFixed(2)} EGP</strong> (was {change.compareAtPrice.toFixed(2)} EGP)
                  </span>
                  <span className="badge good">-{change.discountPercentage.toFixed(0)}%</span>
                  <span className="badge warn">{change.status}</span>
                </div>
              </div>
              <a
                href={change.canonicalUrl}
                target="_blank"
                rel="noreferrer"
                className="btn-secondary"
                style={{ fontSize: '12px', padding: '4px 10px' }}
              >
                Open store ↗
              </a>
            </div>
          ))
        ) : (
          <p style={{ color: 'var(--muted)', fontSize: '13px' }}>
            No supported competitor changes recorded in the latest snapshot.
          </p>
        )}
      </div>

      {/* What Needs Attention? */}
      <div className="panel urgent-alerts-panel">
        <div className="urgent-alerts-header">
          <div className="urgent-alerts-title">
            <h2 className="panel-title">Top Urgent Alerts (Action Center)</h2>
            <span className="badge bad" style={{ fontSize: '11px', textTransform: 'uppercase' }}>
              {totalUrgentCount > 0 ? `${totalUrgentCount} Urgent Issues` : 'Real-time AI'}
            </span>
          </div>
          <div className="urgent-alerts-controls">
            {/* Arabic / English language toggle */}
            <div style={{ display: 'flex', border: '1px solid var(--line)', borderRadius: '6px', overflow: 'hidden' }}>
              <button
                type="button"
                onClick={() => setLang('en')}
                style={{
                  padding: '4px 8px',
                  fontSize: '12px',
                  fontWeight: lang === 'en' ? 700 : 400,
                  background: lang === 'en' ? 'var(--action)' : '#fff',
                  color: lang === 'en' ? '#fff' : 'var(--text)',
                  border: 'none',
                  cursor: 'pointer',
                }}
              >
                EN
              </button>
              <button
                type="button"
                onClick={() => setLang('ar')}
                style={{
                  padding: '4px 8px',
                  fontSize: '12px',
                  fontWeight: lang === 'ar' ? 700 : 400,
                  background: lang === 'ar' ? 'var(--action)' : '#fff',
                  color: lang === 'ar' ? '#fff' : 'var(--text)',
                  border: 'none',
                  cursor: 'pointer',
                }}
              >
                عربي
              </button>
            </div>
            <button
              type="button"
              className="btn-secondary"
              style={{ fontSize: '12px', padding: '4px 10px' }}
              onClick={() => onNavigate('price-comparisons')}
            >
              Compare prices
            </button>
            <button
              type="button"
              className="btn-primary"
              style={{ fontSize: '12px', padding: '4px 12px' }}
              onClick={() => onNavigate('attention')}
            >
              View all ({totalUrgentCount}) →
            </button>
          </div>
        </div>

        {urgentAlerts.length > 0 ? (
          <div className="urgent-alerts-list">
            {urgentAlerts.map((item) => {
              const headline = lang === 'ar' && item.titleAr ? item.titleAr : item.title;
              const explanation = lang === 'ar' && item.reasonAr ? item.reasonAr : item.reason;
              const isRtl = lang === 'ar';

              return (
                <article key={item.id} className="urgent-alert-card" dir={isRtl ? 'rtl' : 'ltr'}>
                  <div className="urgent-alert-title">
                    <span className="badge bad urgent-alert-severity">{item.severity}</span>
                    {item.isBestseller && (
                      <span className="badge-bestseller urgent-alert-bestseller">★ Bestseller</span>
                    )}
                    <strong className="urgent-alert-headline">{headline}</strong>
                  </div>

                  <button
                    type="button"
                    className="btn-secondary urgent-alert-action-btn"
                    onClick={() => onNavigate('attention')}
                  >
                    Act Now →
                  </button>

                  <p className="urgent-alert-explanation">{explanation}</p>

                  <div className="urgent-alert-bottom">
                    {item.competitorName && (
                      <span className="badge neutral">
                        {isRtl ? `المنافس: ${item.competitorName}` : `Competitor: ${item.competitorName}`}
                      </span>
                    )}
                    <span className="badge bad" style={{ fontWeight: 600 }}>
                      {item.egpImpact.toFixed(2)} EGP gap
                    </span>
                    <span className="urgent-alert-next">
                      {isRtl ? 'الإجراء:' : 'Action:'} {lang === 'ar' && item.suggestedActionAr ? item.suggestedActionAr : item.suggestedAction}
                    </span>
                  </div>
                </article>
              );
            })}
          </div>
        ) : isLoadingAttention ? (
          <div style={{ padding: '12px 0' }}>
            <div className="skeleton skeleton-row" />
            <div className="skeleton skeleton-row" />
            <div className="skeleton skeleton-row" />
          </div>
        ) : rankedAttention.length > 0 ? (
          <div className="urgent-alerts-list">
            {rankedAttention.map((item) => {
              const headline = lang === 'ar' && item.headlineAr ? item.headlineAr : item.headline;
              const explanation = lang === 'ar' && item.explanationAr ? item.explanationAr : item.explanation;
              const isRtl = lang === 'ar';

              return (
                <article key={item.id} className="urgent-alert-card" dir={isRtl ? 'rtl' : 'ltr'}>
                  <div className="urgent-alert-title">
                    <span
                      className={`badge ${item.priority === 'high' ? 'bad' : item.priority === 'low' ? 'good' : 'warn'} urgent-alert-severity`}
                    >
                      {item.priority}
                    </span>
                    <strong className="urgent-alert-headline">{headline}</strong>
                  </div>

                  <button
                    type="button"
                    className="btn-secondary urgent-alert-action-btn"
                    onClick={() => onNavigate('price-comparisons')}
                  >
                    {isRtl ? 'عرض في المقارنة ←' : 'View in matrix →'}
                  </button>

                  <p className="urgent-alert-explanation">{explanation}</p>

                  <div className="urgent-alert-bottom">
                    {item.brand && <span className="badge neutral">{item.brand}</span>}
                    <span className="badge neutral">
                      {isRtl ? `المنافس: ${item.competitorName}` : `Competitor: ${item.competitorName}`}
                    </span>
                    <span>
                      {isRtl ? 'ليزي بين:' : 'Lazy Bean:'} <strong>{item.lazyBeanPrice.toFixed(2)} EGP</strong>
                    </span>
                    <span>
                      {isRtl ? 'المنافس:' : `${item.competitorName}:`} <strong>{item.competitorPrice.toFixed(2)} EGP</strong>
                    </span>
                  </div>
                </article>
              );
            })}
          </div>
        ) : fallbackAttentionItems.length > 0 ? (
          <div className="urgent-alerts-list">
            {fallbackAttentionItems.map((item) => (
              <article key={item.lazyBeanProductId} className="urgent-alert-card urgent-alert-card--no-explain">
                <div className="urgent-alert-title">
                  <strong className="urgent-alert-headline">{item.lazyBeanTitle}</strong>
                </div>

                <button
                  type="button"
                  className="btn-secondary urgent-alert-action-btn"
                  onClick={() => onNavigate('price-comparisons')}
                >
                  View in matrix →
                </button>

                <div className="urgent-alert-bottom">
                  {item.brand && <span className="badge neutral">{item.brand}</span>}
                  <span>
                    Lazy Bean: <strong>{item.lazyBeanPrice.toFixed(2)} EGP</strong>
                  </span>
                  {item.position === 'lazyBeanOutOfStock' ? (
                    <span className="badge bad">Lazy Bean Out of Stock</span>
                  ) : (
                    <span className="badge warn">
                      Competitor cheaper ({item.cheapestCompetitorName}: {item.cheapestCompetitorPrice?.toFixed(2)} EGP)
                    </span>
                  )}
                </div>
              </article>
            ))}
          </div>
        ) : (
          <p style={{ color: 'var(--muted)', fontSize: '13px' }}>
            All compared products currently have favorable positions.
          </p>
        )}
      </div>

      {/* Monitored Competitors Grid */}
      <div className="panel">
        <div className="panel-header">
          <h2 className="panel-title">Monitored Stores & Coverage</h2>
          <span className="badge good">5 Stores + Reference Active</span>
        </div>
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
            gap: '12px',
          }}
        >
          <div style={{ padding: '12px', background: '#fafbfc', border: '1px solid var(--line)', borderRadius: '6px' }}>
            <div style={{ fontWeight: 600, fontSize: '14px' }}>Lazy Bean (Reference)</div>
            <div style={{ fontSize: '12px', color: 'var(--muted)', marginTop: '4px' }}>lazzybean.com</div>
            <div style={{ marginTop: '8px' }}><span className="badge good">Reference catalog</span></div>
          </div>

          <div style={{ padding: '12px', background: '#fafbfc', border: '1px solid var(--line)', borderRadius: '6px' }}>
            <div style={{ fontWeight: 600, fontSize: '14px' }}>CAFELAX</div>
            <div style={{ fontSize: '12px', color: 'var(--muted)', marginTop: '4px' }}>cafelax.com (Shopify)</div>
            <div style={{ marginTop: '8px' }}><span className="badge good">Active</span></div>
          </div>

          <div style={{ padding: '12px', background: '#fafbfc', border: '1px solid var(--line)', borderRadius: '6px' }}>
            <div style={{ fontWeight: 600, fontSize: '14px' }}>Coffee & Cream</div>
            <div style={{ fontSize: '12px', color: 'var(--muted)', marginTop: '4px' }}>coffeeandcream.app (Shopify)</div>
            <div style={{ marginTop: '8px' }}><span className="badge good">Active</span></div>
          </div>

          <div style={{ padding: '12px', background: '#fafbfc', border: '1px solid var(--line)', borderRadius: '6px' }}>
            <div style={{ fontWeight: 600, fontSize: '14px' }}>Fengany</div>
            <div style={{ fontSize: '12px', color: 'var(--muted)', marginTop: '4px' }}>fengany.com (Shopify)</div>
            <div style={{ marginTop: '8px' }}><span className="badge good">Active</span></div>
          </div>

          <div style={{ padding: '12px', background: '#fafbfc', border: '1px solid var(--line)', borderRadius: '6px' }}>
            <div style={{ fontWeight: 600, fontSize: '14px' }}>Coffee Zone Misr</div>
            <div style={{ fontSize: '12px', color: 'var(--muted)', marginTop: '4px' }}>coffeezonemisr.com (Shopify)</div>
            <div style={{ marginTop: '8px' }}><span className="badge good">Active</span></div>
          </div>

          <div style={{ padding: '12px', background: '#fafbfc', border: '1px solid var(--line)', borderRadius: '6px' }}>
            <div style={{ fontWeight: 600, fontSize: '14px' }}>Strong Coffee</div>
            <div style={{ fontSize: '12px', color: 'var(--muted)', marginTop: '4px' }}>strongcoffee-eg.com (WooCommerce)</div>
            <div style={{ marginTop: '8px' }}><span className="badge good">Active</span></div>
          </div>
        </div>
      </div>
    </div>
  );
};
