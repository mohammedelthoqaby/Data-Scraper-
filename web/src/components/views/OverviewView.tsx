import React from 'react';
import type { PriceMatrixResponse, CompetitorOffersResponse } from '../../api/client';

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

  // Sample items needing attention from price matrix
  const attentionItems = matrix?.rows
    ?.filter(r => r.position === 'more expensive' || r.position === 'lazyBeanOutOfStock')
    .slice(0, 3) ?? [];

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

      {/* Warning Banner */}
      <div className="warning-banner">
        <span>⚠️</span>
        <span>
          <strong>Notice:</strong> Some competitor information is incomplete or may be older. Check the source date before acting on price differences.
        </span>
      </div>

      {/* 3 KPI Summary Cards */}
      <div className="stats-grid-3">
        <div className="stat-card" onClick={() => onNavigate('price-comparisons')}>
          <div className="stat-number">{suggestedActionsCount}</div>
          <div className="stat-label">Suggested actions</div>
          <div className="stat-sub">Prices needing review or restock</div>
        </div>

        <div className="stat-card" onClick={() => onNavigate('price-comparisons')}>
          <div className="stat-number">{totalComparisons}</div>
          <div className="stat-label">Supported price comparisons</div>
          <div className="stat-sub">Verified matching products</div>
        </div>

        <div className="stat-card" onClick={() => onNavigate('review-queue')}>
          <div className="stat-number">{reviewCount}</div>
          <div className="stat-label">Product matches to review</div>
          <div className="stat-sub">Candidate matches awaiting approval</div>
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

        {recentChanges.length > 0 ? (
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
      <div className="panel">
        <div className="panel-header">
          <h2 className="panel-title">What needs attention?</h2>
          <button
            type="button"
            className="btn-secondary"
            style={{ fontSize: '12px', padding: '4px 10px' }}
            onClick={() => onNavigate('price-comparisons')}
          >
            Compare prices
          </button>
        </div>

        {attentionItems.length > 0 ? (
          attentionItems.map((item) => (
            <div key={item.lazyBeanProductId} className="decision-row">
              <div className="decision-content">
                <div className="decision-title">{item.lazyBeanTitle}</div>
                <div className="decision-meta">
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
              </div>
              <button
                type="button"
                className="btn-secondary"
                style={{ fontSize: '12px', padding: '4px 10px' }}
                onClick={() => onNavigate('price-comparisons')}
              >
                View in matrix →
              </button>
            </div>
          ))
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
            <div style={{ fontSize: '12px', color: 'var(--muted)', marginTop: '4px' }}>strongcoffee-eg.com (Woo)</div>
            <div style={{ marginTop: '8px' }}><span className="badge good">Active</span></div>
          </div>
        </div>
      </div>
    </div>
  );
};
