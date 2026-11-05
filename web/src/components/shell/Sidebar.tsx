import React from 'react';
import type { AuthenticatedUser, RunProgress } from '../../api/client';

interface SidebarProps {
  activeView: string;
  onNavigate: (view: string) => void;
  reviewCount: number;
  alertCount?: number;
  user: AuthenticatedUser | null;
  onOpenLogin: () => void;
  onLogout: () => void;
  activeRunProgress?: RunProgress | null;
}

export const Sidebar: React.FC<SidebarProps> = ({
  activeView,
  onNavigate,
  reviewCount,
  alertCount,
  user,
  onOpenLogin,
  onLogout,
  activeRunProgress,
}) => {
  return (
    <aside className="sidebar">
      {/* Brand Tile */}
      <div className="brand">
        <div className="mark">
          <img src="/assets/logo.png" alt="Lazy Bean logo" width="26" height="26" style={{ objectFit: 'contain' }} />
        </div>
        <div className="brand-text">
          Lazy Bean
          <small>MARKET INTELLIGENCE</small>
        </div>
      </div>

      {/* Main Navigation */}
      <nav className="main-nav" aria-label="Main navigation">
        <button
          type="button"
          aria-current={activeView === 'overview' ? 'page' : undefined}
          onClick={() => onNavigate('overview')}
        >
          <span>Overview</span>
        </button>

        <button
          type="button"
          aria-current={activeView === 'offers' || activeView === 'weekly-actions' ? 'page' : undefined}
          onClick={() => onNavigate('offers')}
        >
          <span>Promotions & offers</span>
        </button>

        <button
          type="button"
          aria-current={activeView === 'price-comparisons' ? 'page' : undefined}
          onClick={() => onNavigate('price-comparisons')}
        >
          <span>Price comparisons</span>
        </button>

        <button
          type="button"
          aria-current={activeView === 'ads-campaigns' ? 'page' : undefined}
          onClick={() => onNavigate('ads-campaigns')}
        >
          <span>Ads & campaigns</span>
        </button>

        <button
          type="button"
          aria-current={activeView === 'attention' ? 'page' : undefined}
          onClick={() => onNavigate('attention')}
        >
          <span>Needs attention</span>
          {(alertCount ?? 0) > 0 && (
            <span className="nav-badge warn">{alertCount}</span>
          )}
        </button>

        <details className="nav-secondary" open>
          <summary>Settings & orchestration</summary>
          <button
            type="button"
            aria-current={activeView === 'runs' || activeView === 'history' ? 'page' : undefined}
            onClick={() => onNavigate('runs')}
          >
            <span>Collection runs</span>
            {activeRunProgress && (
              <span
                className="nav-badge good"
                style={{
                  fontSize: '10px',
                  padding: '2px 6px',
                  backgroundColor: '#dbeafe',
                  color: '#1e40af',
                }}
              >
                {activeRunProgress.overallPercent}%
              </span>
            )}
          </button>
          <button
            type="button"
            aria-current={activeView === 'sources' ? 'page' : undefined}
            onClick={() => onNavigate('sources')}
          >
            <span>Manage sources & identities</span>
          </button>
          {(user?.username?.toLowerCase() === 'admin' || Boolean(user)) && (
            <>
              <button
                type="button"
                aria-current={activeView === 'settings-alerts' ? 'page' : undefined}
                onClick={() => onNavigate('settings-alerts')}
              >
                <span>Alert thresholds</span>
              </button>
              <button
                type="button"
                aria-current={activeView === 'data-quality' || activeView === 'review-queue' ? 'page' : undefined}
                onClick={() => onNavigate('data-quality')}
              >
                <span>Data quality & reviews</span>
                {reviewCount > 0 && (
                  <span className="nav-badge" style={{ background: '#f1f5f9', color: '#475569' }}>
                    {reviewCount}
                  </span>
                )}
              </button>
              <button
                type="button"
                aria-current={activeView === 'settings-ai' ? 'page' : undefined}
                onClick={() => onNavigate('settings-ai')}
              >
                <span>AI Configuration (Gemini)</span>
              </button>
            </>
          )}
        </details>
      </nav>

      {/* Sidebar Footer */}
      <div className="sidebar-footer">
        <div className="side-note">
          <span className="dot" />
          <span>Live read-only intelligence</span>
        </div>
        {user ? (
          <button type="button" className="lock-btn" onClick={onLogout} title="Sign out">
            🔒 <span>Lock (Sign out)</span>
          </button>
        ) : (
          <button type="button" className="lock-btn" onClick={onOpenLogin} title="Unlock full access">
            🔓 <span>Unlock operations</span>
          </button>
        )}
      </div>
    </aside>
  );
};
