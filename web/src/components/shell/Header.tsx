import React from 'react';
import type { AuthenticatedUser } from '../../api/client';

interface HeaderProps {
  onToggleSidebar: () => void;
  user: AuthenticatedUser | null;
  onOpenLogin: () => void;
}

export const Header: React.FC<HeaderProps> = ({ onToggleSidebar, user, onOpenLogin }) => {
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
