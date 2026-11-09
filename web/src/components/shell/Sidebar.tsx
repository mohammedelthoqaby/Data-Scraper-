import React, { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import type { AuthenticatedUser, RunProgress } from '../../api/client';
import { SidebarIcon, type SidebarIconName } from './SidebarIcons';

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

// The pages behind the Settings button. The button shows its active state while one of them is open.
const SETTINGS_VIEWS = ['sources', 'settings-account', 'settings-alerts', 'data-quality', 'review-queue', 'settings-ai', 'settings-collector'];

const MENU_ITEMS: { view: string; label: string; icon: SidebarIconName }[] = [
  { view: 'sources', label: 'Manage sources & identities', icon: 'sources' },
  { view: 'settings-account', label: 'Account & password', icon: 'account' },
  { view: 'settings-alerts', label: 'Alert thresholds', icon: 'thresholds' },
  { view: 'data-quality', label: 'Data quality & reviews', icon: 'quality' },
  { view: 'settings-ai', label: 'AI configuration', icon: 'ai' },
  { view: 'settings-collector', label: 'Collector (desktop app)', icon: 'collector' },
];

const MENU_WIDTH = 260;
const EDGE = 8;
const MIN_ROOM = 240;

interface MenuPlacement {
  left: number;
  width: number;
  maxHeight: number;
  top?: number;
  bottom?: number;
}

// Above the Settings button when there is room, otherwise below it. Kept inside the viewport on both axes.
function placeMenu(anchor: DOMRect): MenuPlacement {
  const width = Math.min(MENU_WIDTH, window.innerWidth - EDGE * 2);
  const left = Math.min(Math.max(EDGE, anchor.left), window.innerWidth - width - EDGE);
  const roomAbove = anchor.top - EDGE * 2;
  const roomBelow = window.innerHeight - anchor.bottom - EDGE * 2;
  if (roomAbove >= MIN_ROOM || roomAbove >= roomBelow) {
    return { left, width, bottom: window.innerHeight - anchor.top + EDGE, maxHeight: Math.max(120, roomAbove) };
  }
  return { left, width, top: anchor.bottom + EDGE, maxHeight: Math.max(120, roomBelow) };
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
  const [menuOpen, setMenuOpen] = useState(false);
  const [placement, setPlacement] = useState<MenuPlacement | null>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  // Set when the menu is opened by the button, so its first item takes focus once the menu is on screen.
  const focusFirstItem = useRef(false);
  const settingsActive = SETTINGS_VIEWS.includes(activeView);

  const closeMenu = useCallback(() => {
    setMenuOpen(false);
    buttonRef.current?.focus();
  }, []);

  // Placed before the first paint, and again when the window or the sidebar moves.
  useLayoutEffect(() => {
    if (!menuOpen) return;
    const update = () => {
      if (buttonRef.current) setPlacement(placeMenu(buttonRef.current.getBoundingClientRect()));
    };
    update();
    const onScroll = (event: Event) => {
      if (menuRef.current && event.target instanceof Node && menuRef.current.contains(event.target)) return;
      update();
    };
    window.addEventListener('resize', update);
    window.addEventListener('scroll', onScroll, true);
    return () => {
      window.removeEventListener('resize', update);
      window.removeEventListener('scroll', onScroll, true);
    };
  }, [menuOpen]);

  // A click outside the menu and the button closes it. Escape closes it and returns focus to the button. Tab leaves it.
  useEffect(() => {
    if (!menuOpen) return;
    const onPointer = (event: PointerEvent) => {
      const target = event.target as Node;
      if (menuRef.current?.contains(target) || buttonRef.current?.contains(target)) return;
      setMenuOpen(false);
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        closeMenu();
      } else if (event.key === 'Tab') {
        setMenuOpen(false);
      }
    };
    document.addEventListener('pointerdown', onPointer);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('pointerdown', onPointer);
      document.removeEventListener('keydown', onKey);
    };
  }, [menuOpen, closeMenu]);

  // The keyboard starts in the menu: once it is placed on screen, its first item takes focus.
  useEffect(() => {
    if (!focusFirstItem.current || !placement) return;
    const first = menuRef.current?.querySelector<HTMLElement>('[role="menuitem"]');
    if (first) {
      focusFirstItem.current = false;
      first.focus();
    }
  }, [placement]);

  const onMenuKey = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return;
    const items = Array.from(event.currentTarget.querySelectorAll<HTMLElement>('[role="menuitem"]'));
    if (items.length === 0) return;
    event.preventDefault();
    const index = items.indexOf(document.activeElement as HTMLElement);
    const next =
      index < 0
        ? 0
        : event.key === 'ArrowDown'
          ? (index + 1) % items.length
          : (index - 1 + items.length) % items.length;
    items[next].focus();
  };

  const go = (view: string) => {
    closeMenu();
    onNavigate(view);
  };

  const signOut = () => {
    closeMenu();
    onLogout();
  };

  const signIn = () => {
    closeMenu();
    onOpenLogin();
  };

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
          className="nav-item"
          aria-current={activeView === 'overview' ? 'page' : undefined}
          onClick={() => onNavigate('overview')}
        >
          <SidebarIcon name="overview" />
          <span className="nav-item__label">Overview</span>
        </button>

        <button
          type="button"
          className="nav-item"
          aria-current={activeView === 'offers' || activeView === 'weekly-actions' ? 'page' : undefined}
          onClick={() => onNavigate('offers')}
        >
          <SidebarIcon name="offers" />
          <span className="nav-item__label">Promotions & offers</span>
        </button>

        <button
          type="button"
          className="nav-item"
          aria-current={activeView === 'price-comparisons' ? 'page' : undefined}
          onClick={() => onNavigate('price-comparisons')}
        >
          <SidebarIcon name="prices" />
          <span className="nav-item__label">Price comparisons</span>
        </button>

        <button
          type="button"
          className="nav-item"
          aria-current={activeView === 'ads-campaigns' ? 'page' : undefined}
          onClick={() => onNavigate('ads-campaigns')}
        >
          <SidebarIcon name="ads" />
          <span className="nav-item__label">Ads & campaigns</span>
        </button>

        <button
          type="button"
          className="nav-item"
          aria-current={activeView === 'attention' ? 'page' : undefined}
          onClick={() => onNavigate('attention')}
        >
          <SidebarIcon name="attention" />
          <span className="nav-item__label">Needs attention</span>
          {(alertCount ?? 0) > 0 && <span className="nav-badge warn">{alertCount}</span>}
        </button>

        <div className="nav-label">Operations</div>
        <button
          type="button"
          className="nav-item"
          aria-current={activeView === 'runs' || activeView === 'history' ? 'page' : undefined}
          onClick={() => onNavigate('runs')}
        >
          <SidebarIcon name="runs" />
          <span className="nav-item__label">Collection runs</span>
          {activeRunProgress && <span className="nav-badge run">{activeRunProgress.overallPercent}%</span>}
        </button>

        {/* Pinned to the bottom of the sidebar: the status line, then the Settings button. */}
        <div className="nav-foot">
          <div className="side-note">
            <span className="dot" />
            <span>Live read-only intelligence</span>
          </div>
          <button
            ref={buttonRef}
            type="button"
            className={`settings-btn${settingsActive ? ' is-active' : ''}`}
            aria-haspopup="menu"
            aria-expanded={menuOpen}
            onClick={() => {
              if (menuOpen) {
                setMenuOpen(false);
                return;
              }
              focusFirstItem.current = true;
              setMenuOpen(true);
            }}
          >
            <SidebarIcon name="settings" />
            <span className="nav-item__label">Settings</span>
            <span className="settings-btn__caret" aria-hidden="true">
              {menuOpen ? '▾' : '▴'}
            </span>
          </button>
        </div>
      </nav>

      {menuOpen &&
        placement &&
        createPortal(
          <div
            ref={menuRef}
            className="settings-menu"
            role="menu"
            aria-label="Settings"
            onKeyDown={onMenuKey}
            style={{
              left: placement.left,
              width: placement.width,
              maxHeight: placement.maxHeight,
              top: placement.top,
              bottom: placement.bottom,
            }}
          >
            {user ? (
              <>
                {MENU_ITEMS.map((item) => {
                  const current = activeView === item.view || (item.view === 'data-quality' && activeView === 'review-queue');
                  return (
                    <button
                      key={item.view}
                      type="button"
                      role="menuitem"
                      className="settings-menu__item"
                      aria-current={current ? 'page' : undefined}
                      onClick={() => go(item.view)}
                    >
                      <SidebarIcon name={item.icon} />
                      <span className="nav-item__label">{item.label}</span>
                      {item.view === 'data-quality' && reviewCount > 0 && <span className="nav-badge">{reviewCount}</span>}
                    </button>
                  );
                })}
                <div className="settings-menu__divider" role="separator" />
                <button type="button" role="menuitem" className="settings-menu__item settings-menu__item--danger" onClick={signOut}>
                  <SidebarIcon name="signout" />
                  <span className="nav-item__label">Sign out</span>
                </button>
              </>
            ) : (
              <button type="button" role="menuitem" className="settings-menu__item" onClick={signIn}>
                <SidebarIcon name="signin" />
                <span className="nav-item__label">Sign in to unlock operations</span>
              </button>
            )}
          </div>,
          document.body
        )}
    </aside>
  );
};
