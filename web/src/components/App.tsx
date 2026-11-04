import React, { useState, useEffect, useCallback } from 'react';
import { api, type AuthenticatedUser, type CollectionRun, type PriceMatrixResponse, type CompetitorOffersResponse } from '../api/client';
import { Sidebar } from './shell/Sidebar';
import { Header } from './shell/Header';
import { Toolbar } from './shell/Toolbar';
import { Footer } from './shell/Footer';
import { LoginModal } from './auth/LoginModal';
import { OverviewView } from './views/OverviewView';
import { PriceMatrixView } from './views/PriceMatrixView';

export const App: React.FC = () => {
  // Navigation State
  const [activeView, setActiveView] = useState<string>('overview');
  const [isSidebarCollapsed, setIsSidebarCollapsed] = useState(false);

  // Auth State
  const [user, setUser] = useState<AuthenticatedUser | null>(null);
  const [isLoginOpen, setIsLoginOpen] = useState(false);

  // Data State
  const [runs, setRuns] = useState<CollectionRun[]>([]);
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const [matrix, setMatrix] = useState<PriceMatrixResponse | null>(null);
  const [offers, setOffers] = useState<CompetitorOffersResponse | null>(null);
  const [reviewCount, setReviewCount] = useState<number>(0);
  const [isLoading, setIsLoading] = useState<boolean>(true);

  // Sync hash route with state
  useEffect(() => {
    const handleHashChange = () => {
      const hash = window.location.hash.replace(/^#\/?/, '').split('?')[0];
      if (hash && ['overview', 'price-comparisons', 'weekly-actions', 'ads-campaigns', 'review-queue', 'sources', 'history', 'improve-recommendations'].includes(hash)) {
        setActiveView(hash);
      }
    };

    window.addEventListener('hashchange', handleHashChange);
    handleHashChange();
    return () => window.removeEventListener('hashchange', handleHashChange);
  }, []);

  const navigateTo = (view: string) => {
    window.location.hash = `#/${view}`;
    setActiveView(view);
  };

  // Check existing session
  const checkSession = useCallback(async () => {
    try {
      const { data } = await api.GET('/api/auth/me');
      if (data) {
        setUser(data);
      }
    } catch {
      setUser(null);
    }
  }, []);

  // Fetch runs
  const fetchRuns = useCallback(async () => {
    try {
      const { data } = await api.GET('/api/runs', {
        params: { query: { page: 1, pageSize: 10 } },
      });
      if (data?.runs && data.runs.length > 0) {
        setRuns(data.runs);
        if (!selectedRunId) {
          const completedRun = data.runs.find(r => r.status === 'Completed') || data.runs[0];
          setSelectedRunId(completedRun.id);
        }
      }
    } catch (err) {
      console.error('Failed to fetch runs:', err);
    }
  }, [selectedRunId]);

  // Fetch reviews count
  const fetchReviewCount = useCallback(async () => {
    try {
      const { data } = await api.GET('/api/reviews');
      if (data?.totalCount != null) {
        setReviewCount(data.totalCount);
      }
    } catch (err) {
      console.error('Failed to fetch reviews:', err);
    }
  }, []);

  // Fetch price matrix
  const fetchMatrix = useCallback(async (runId?: string | null) => {
    setIsLoading(true);
    try {
      if (runId) {
        const { data } = await api.GET('/api/runs/{id}/price-matrix', {
          params: { path: { id: runId } },
        });
        if (data) setMatrix(data);
      } else {
        const { data } = await api.GET('/api/price-matrix/latest');
        if (data) setMatrix(data);
      }
    } catch (err) {
      console.error('Failed to fetch price matrix:', err);
    } finally {
      setIsLoading(false);
    }
  }, []);

  // Fetch competitor offers
  const fetchOffers = useCallback(async (runId: string) => {
    try {
      const { data } = await api.GET('/api/runs/{id}/competitor-offers', {
        params: { path: { id: runId }, query: { status: 'all' } },
      });
      if (data) setOffers(data);
    } catch (err) {
      console.error('Failed to fetch competitor offers:', err);
    }
  }, []);

  // Initial load
  useEffect(() => {
    checkSession();
    fetchRuns();
    fetchReviewCount();
    fetchMatrix();
  }, [checkSession, fetchRuns, fetchReviewCount, fetchMatrix]);

  // Reload data when selected run changes
  useEffect(() => {
    if (selectedRunId) {
      fetchMatrix(selectedRunId);
      fetchOffers(selectedRunId);
    }
  }, [selectedRunId, fetchMatrix, fetchOffers]);

  const handleLogout = async () => {
    try {
      await api.POST('/api/auth/logout');
      setUser(null);
    } catch (err) {
      console.error('Logout error:', err);
    }
  };

  const handleReloadAll = () => {
    fetchRuns();
    fetchMatrix(selectedRunId);
    if (selectedRunId) fetchOffers(selectedRunId);
    fetchReviewCount();
  };

  const handleStartRun = async () => {
    if (!user) {
      setIsLoginOpen(true);
      return;
    }
    try {
      await api.POST('/api/runs', { body: {} });
      fetchRuns();
    } catch (err: any) {
      alert(err?.message || 'Failed to trigger collection run');
    }
  };

  // Toggle body class for sidebar collapsed
  useEffect(() => {
    if (isSidebarCollapsed) {
      document.body.classList.add('sidebar-collapsed');
    } else {
      document.body.classList.remove('sidebar-collapsed');
    }
  }, [isSidebarCollapsed]);

  return (
    <div className="app-shell">
      {/* Sidebar Navigation */}
      <Sidebar
        activeView={activeView}
        onNavigate={navigateTo}
        reviewCount={reviewCount}
        user={user}
        onOpenLogin={() => setIsLoginOpen(true)}
        onLogout={handleLogout}
      />

      {/* Main Workspace */}
      <div className="workspace">
        <Header
          onToggleSidebar={() => setIsSidebarCollapsed((prev) => !prev)}
          user={user}
          onOpenLogin={() => setIsLoginOpen(true)}
        />

        <main className="main-content">
          <Toolbar
            runs={runs}
            selectedRunId={selectedRunId}
            onSelectRun={(id) => setSelectedRunId(id)}
            onReload={handleReloadAll}
            isLoading={isLoading}
          />

          {/* Active View Outlet */}
          {activeView === 'overview' && (
            <OverviewView
              matrix={matrix}
              offers={offers}
              reviewCount={reviewCount}
              onNavigate={navigateTo}
              onRefreshCollection={handleStartRun}
            />
          )}

          {activeView === 'price-comparisons' && (
            <PriceMatrixView
              matrix={matrix}
              selectedRunId={selectedRunId}
              isLoading={isLoading}
            />
          )}

          {activeView !== 'overview' && activeView !== 'price-comparisons' && (
            <div className="panel" style={{ padding: '40px', textAlign: 'center' }}>
              <h2>{activeView.replace('-', ' ').toUpperCase()}</h2>
              <p style={{ color: 'var(--muted)', marginTop: '8px' }}>
                This view is scheduled for the next milestone. Switch back to <strong>Overview</strong> or{' '}
                <strong>Price comparisons</strong>.
              </p>
              <button
                type="button"
                className="btn-primary"
                style={{ marginTop: '16px' }}
                onClick={() => navigateTo('price-comparisons')}
              >
                Go to Price comparisons
              </button>
            </div>
          )}
        </main>

        <Footer />
      </div>

      {/* Login Dialog Modal */}
      <LoginModal
        isOpen={isLoginOpen}
        onClose={() => setIsLoginOpen(false)}
        onSuccess={(loggedInUser) => {
          setUser(loggedInUser);
          handleReloadAll();
        }}
      />
    </div>
  );
};
