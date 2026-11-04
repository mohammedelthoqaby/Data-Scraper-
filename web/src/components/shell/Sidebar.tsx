import React from 'react';
import type { AuthenticatedUser } from '../../api/client';

interface SidebarProps {
  activeView: string;
  onNavigate: (view: string) => void;
  reviewCount: number;
  user: AuthenticatedUser | null;
  onOpenLogin: () => void;
  onLogout: () => void;
}

export const Sidebar: React.FC<SidebarProps> = ({
  activeView,
  onNavigate,
  reviewCount,
  user,
  onOpenLogin,
  onLogout,
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
          aria-current={activeView === 'weekly-actions' ? 'page' : undefined}
          onClick={() => onNavigate('weekly-actions')}
        >
          <span>Weekly actions</span>
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
          aria-current={activeView === 'review-queue' ? 'page' : undefined}
          onClick={() => onNavigate('review-queue')}
        >
          <span>Reviews needing attention</span>
          {reviewCount > 0 && (
            <span className="nav-badge warn">{reviewCount}</span>
          )}
        </button>

        <details className="nav-secondary" open>
          <summary>Settings & history</summary>
          <button
            type="button"
            aria-current={activeView === 'sources' ? 'page' : undefined}
            onClick={() => onNavigate('sources')}
          >
            <span>Manage sources</span>
          </button>
          <button
            type="button"
            aria-current={activeView === 'history' ? 'page' : undefined}
            onClick={() => onNavigate('history')}
          >
            <span>Report history</span>
          </button>
          <button
            type="button"
            aria-current={activeView === 'improve-recommendations' ? 'page' : undefined}
            onClick={() => onNavigate('improve-recommendations')}
          >
            <span>Internal data import</span>
          </button>
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
