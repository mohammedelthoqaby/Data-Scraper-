import React from 'react';
import type { AuthenticatedUser, RunProgress } from '../../api/client';
import { CollectorPill } from './CollectorPill';

interface HeaderProps {
  onToggleSidebar: () => void;
  user: AuthenticatedUser | null;
  onOpenLogin: () => void;
  activeRunProgress?: RunProgress | null;
  onNavigateToRuns?: () => void;
}

export const Header: React.FC<HeaderProps> = ({
  onToggleSidebar,
  user,
  onOpenLogin,
  activeRunProgress,
  onNavigateToRuns,
}) => {
  return (
    <header className="workspace-header">
      <div className="header-left">
        <button
          type="button"
          className="toggle-sidebar"
          onClick={onToggleSidebar}
          aria-label="Toggle navigation sidebar"
        >
          ☰
        </button>
        <div className="breadcrumb">
          Operations / Competitor intelligence
        </div>
      </div>

      <div className="header-right">
        {activeRunProgress && (
          <button
            type="button"
            className="global-run-indicator"
            onClick={onNavigateToRuns}
            title="Collection run in progress — click to view real-time progress"
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: '6px',
              padding: '4px 10px',
              borderRadius: '9999px',
              backgroundColor: '#eff6ff',
              color: '#1d4ed8',
              border: '1px solid #bfdbfe',
              fontSize: '12px',
              fontWeight: 600,
              cursor: 'pointer',
              lineHeight: 1,
            }}
          >
            <span
              style={{
                width: '8px',
                height: '8px',
                borderRadius: '50%',
                backgroundColor: '#2563eb',
                display: 'inline-block',
                boxShadow: '0 0 0 2px rgba(37,99,235,0.2)',
              }}
            />
            Collection running {activeRunProgress.overallPercent}%
          </button>
        )}
        <CollectorPill />
        <span className="timezone">Africa/Cairo</span>
        {user ? (
          <span className="user-badge" title="Authenticated session">
            👤 {user.username}
          </span>
        ) : (
          <button type="button" className="btn-secondary" style={{ padding: '4px 10px', fontSize: '12px' }} onClick={onOpenLogin}>
            🔓 Unlock
          </button>
        )}
      </div>
    </header>
  );
};
