import React, { useState, useEffect, useCallback, useLayoutEffect, useRef } from 'react';
import {
  api,
  onUnauthorized,
  fetchActiveRun,
  type AuthenticatedUser,
  type CollectionRun,
  type PriceMatrixResponse,
  type CompetitorOffersResponse,
  type PriceMatrixRow,
  type RunProgress,
} from '../api/client';
import { Sidebar } from './shell/Sidebar';
import { Header } from './shell/Header';
import { Toolbar } from './shell/Toolbar';
import { Footer } from './shell/Footer';
import { LoginModal } from './auth/LoginModal';
import { UnlockScreen } from './auth/UnlockScreen';
import { OverviewView } from './views/OverviewView';
import { PriceMatrixView } from './views/PriceMatrixView';
import { OffersView } from './views/OffersView';
import { AdsCampaignsView } from './views/AdsCampaignsView';
import { ReviewQueueView } from './views/ReviewQueueView';
import { RunsView } from './views/RunsView';
import { SourcesIdentitiesView } from './views/SourcesIdentitiesView';
import { SettingsAiView } from './views/SettingsAiView';
import { ActionCenterView } from './views/ActionCenterView';
import { SettingsAlertsView } from './views/SettingsAlertsView';
import { SettingsCollectorView } from './views/SettingsCollectorView';
import { SettingsAccountView } from './views/SettingsAccountView';
import { CollectorBanner } from './shell/CollectorBanner';
import { DataQualityView } from './views/DataQualityView';
import { ProductDetailModal } from './views/ProductDetailModal';
import { fetchAlertSummary } from '../api/alerts';
import { KeepAliveSection, type SectionHandle } from './shell/KeepAliveSection';

// Each view belongs to a section. Views that share one component (Promotions and weekly actions, Runs and history,
// Data quality and review queue) share a section, so they keep the same mounted instance.
const SECTION_OF_VIEW: Record<string, string> = {
  overview: 'overview',
  offers: 'offers',
  'weekly-actions': 'offers',
  'price-comparisons': 'price',
  'ads-campaigns': 'ads',
  attention: 'attention',
  'settings-alerts': 'settings-alerts',
  'settings-account': 'settings-account',
  'settings-collector': 'settings-collector',
  'settings-ai': 'settings-ai',
  'data-quality': 'data-quality',
  'review-queue': 'data-quality',
  runs: 'runs',
  history: 'runs',
  sources: 'sources',
  'improve-recommendations': 'improve-recommendations',
};

const sectionOf = (view: string): string => SECTION_OF_VIEW[view] ?? view;

export const App: React.FC = () => {
  // Navigation State
  const [activeView, setActiveView] = useState<string>('overview');
  // The latest view, for the handlers that run outside render (the hash and the sidebar).
  const activeViewRef = useRef(activeView);
  activeViewRef.current = activeView;
  // Per section: the window scroll position when it was left, and the handles that save and reset its inner scroll.
  const windowScrollRef = useRef<Record<string, number>>({});
  const sectionRegistry = useRef(new Map<string, SectionHandle>());
  const [isSidebarCollapsed, setIsSidebarCollapsed] = useState(false);

  // Auth State
  const [user, setUser] = useState<AuthenticatedUser | null>(null);
  const [isCheckingAuth, setIsCheckingAuth] = useState<boolean>(true);

  // Global Active Collection Run State
  const [activeRunProgress, setActiveRunProgress] = useState<RunProgress | null>(null);

  // Poll for active collection run across all pages every 10s
  useEffect(() => {
    let timer: any = null;
    const pollGlobalActiveRun = async () => {
      try {
        const active = await fetchActiveRun();
        if (active && (active.status === 'Running' || active.status === 'Queued')) {
          setActiveRunProgress(active);
        } else {
          setActiveRunProgress(null);
        }
      } catch (err) {
        // Silent poll error
      }
    };

    pollGlobalActiveRun();
    timer = setInterval(pollGlobalActiveRun, 10000);
    return () => clearInterval(timer);
  }, []);
  const [isLoginOpen, setIsLoginOpen] = useState(false);

  // Data State
  const [runs, setRuns] = useState<CollectionRun[]>([]);
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const [matrix, setMatrix] = useState<PriceMatrixResponse | null>(null);
  const [matrixError, setMatrixError] = useState<{ status?: number; message: string } | null>(null);
  const [offers, setOffers] = useState<CompetitorOffersResponse | null>(null);
  const [reviewCount, setReviewCount] = useState<number>(0);
  const [alertCount, setAlertCount] = useState<number>(0);
  const [isLoading, setIsLoading] = useState<boolean>(true);

  // Product Detail Modal State
  const [isProductDetailOpen, setIsProductDetailOpen] = useState(false);
  const [activeDetailProductId, setActiveDetailProductId] = useState<string | null>(null);
  const [activeDetailTitle, setActiveDetailTitle] = useState<string | undefined>(undefined);
  const [activeDetailRow, setActiveDetailRow] = useState<PriceMatrixRow | null>(null);

  const openProductDetail = (productId: string, title?: string, row?: PriceMatrixRow) => {
    setActiveDetailProductId(productId);
    setActiveDetailTitle(title);
    setActiveDetailRow(row || null);
    setIsProductDetailOpen(true);
  };

  const closeProductDetail = () => {
    setIsProductDetailOpen(false);
    setActiveDetailProductId(null);
    setActiveDetailTitle(undefined);
    setActiveDetailRow(null);
  };

  // Sync hash route with state
  useEffect(() => {
    const handleHashChange = () => {
      const hash = window.location.hash.replace(/^#\/?/, '').split('?')[0];
      if (
        hash &&
        [
          'overview',
          'attention',
          'offers',
          'weekly-actions',
          'price-comparisons',
          'ads-campaigns',
          'review-queue',
          'sources',
          'runs',
          'history',
          'settings-ai',
          'settings-alerts',
          'settings-collector',
          'settings-account',
          'data-quality',
          'improve-recommendations',
        ].includes(hash)
      ) {
        switchView(hash);
      }
    };

    window.addEventListener('hashchange', handleHashChange);
    handleHashChange();
    return () => window.removeEventListener('hashchange', handleHashChange);
  }, []);

  // Switching saves the window scroll and the inner scroll positions of the section that is left.
  const switchView = (next: string) => {
    const current = activeViewRef.current;
    if (next === current) return;
    const fromSection = sectionOf(current);
    windowScrollRef.current[fromSection] = window.scrollY;
    sectionRegistry.current.get(fromSection)?.save();
    activeViewRef.current = next;
    setActiveView(next);
  };

  // The sidebar item of the section that is already open scrolls it back to the top.
  const resetSectionScroll = (section: string) => {
    window.scrollTo(0, 0);
    sectionRegistry.current.get(section)?.reset();
  };

  const navigateTo = (view: string) => {
    if (view === activeViewRef.current) {
      resetSectionScroll(sectionOf(view));
    } else {
      switchView(view);
    }
    window.location.hash = `#/${view}`;
  };

  // Sections stay mounted once they have been opened. The section of the current view is shown; the others are hidden.
  const activeSection = sectionOf(activeView);
  const [visitedSections, setVisitedSections] = useState<string[]>([]);
  useEffect(() => {
    setVisitedSections((prev) => (prev.includes(activeSection) ? prev : [...prev, activeSection]));
  }, [activeSection]);
  const sectionsToRender = visitedSections.includes(activeSection)
    ? visitedSections
    : [...visitedSections, activeSection];

  // Back in the window where the section was left. A section opened for the first time starts at the top.
  useLayoutEffect(() => {
    window.scrollTo(0, windowScrollRef.current[activeSection] ?? 0);
  }, [activeSection]);

  // Check existing session
  const checkSession = useCallback(async () => {
    try {
      const { data, response } = await api.GET('/api/auth/me');
      if (response?.status === 200 && data) {
        setUser(data);
        return data;
      } else {
        setUser(null);
        return null;
      }
    } catch {
      setUser(null);
      return null;
    }
  }, []);

  // Fetch runs
  const fetchRuns = useCallback(async () => {
    try {
      const { data, response } = await api.GET('/api/runs', {
        params: { query: { page: 1, pageSize: 10 } },
      });
      if (response?.status === 200 && data) {
        const runsList: CollectionRun[] = Array.isArray(data)
          ? (data as any)
          : (data as any)?.runs ?? [];
        if (runsList.length > 0) {
          setRuns(runsList);
          const completedRun = runsList.find((r) => r.status === 'Completed') || runsList[0];
          if (completedRun?.id) {
            setSelectedRunId((prev) => prev ?? completedRun.id);
          }
        }
      }
    } catch (err) {
      console.error('Failed to fetch runs:', err);
    }
  }, []);

  // Fetch reviews count
  const fetchReviewCount = useCallback(async () => {
    try {
      const { data, response } = await api.GET('/api/reviews');
      if (response?.status === 200 && data?.totalCount != null) {
        setReviewCount(data.totalCount);
      }
    } catch (err) {
      console.error('Failed to fetch reviews:', err);
    }
  }, []);

  const fetchAlertCount = useCallback(async () => {
    try {
      const summary = await fetchAlertSummary();
      setAlertCount(summary.urgentCount > 0 ? summary.urgentCount : summary.totalActive);
    } catch {
      // ignore
    }
  }, []);

  // Fetch price matrix
  const fetchMatrix = useCallback(async (runId?: string | null) => {
    setIsLoading(true);
    try {
      if (runId) {
        const res = await api.GET('/api/runs/{id}/price-matrix', {
          params: { path: { id: runId } },
        });
        if (res.error || !res.response.ok) {
          setMatrixError({
            status: res.response.status,
            message: (res.error as any)?.title || (res.error as any)?.message || `Server returned error (${res.response.status})`,
          });
        } else if (res.data) {
          setMatrixError(null);
          setMatrix(res.data);
        }
      } else {
        const res = await api.GET('/api/price-matrix/latest');
        if (res.error || !res.response.ok) {
          setMatrixError({
            status: res.response.status,
            message: (res.error as any)?.title || (res.error as any)?.message || `Server returned error (${res.response.status})`,
          });
        } else if (res.data) {
          setMatrixError(null);
          setMatrix(res.data);
        }
      }
    } catch (err: any) {
      console.error('Failed to fetch price matrix:', err);
      setMatrixError({
        status: err?.status || 500,
        message: err?.message || 'Network error fetching price matrix',
      });
    } finally {
      setIsLoading(false);
    }
  }, []);

  // Fetch competitor offers
  const fetchOffers = useCallback(async (runId: string) => {
    try {
      const { data, response } = await api.GET('/api/runs/{id}/competitor-offers', {
        params: { path: { id: runId }, query: { status: 'all' } },
      });
      if (response?.status === 200 && data) setOffers(data);
    } catch (err) {
      console.error('Failed to fetch competitor offers:', err);
    }
  }, []);

  const clearData = useCallback(() => {
    setRuns([]);
    setSelectedRunId(null);
    setMatrix(null);
    setMatrixError(null);
    setOffers(null);
    setReviewCount(0);
  }, []);

  // Initial load: verify authentication before requesting protected data
  useEffect(() => {
    let mounted = true;
    setIsCheckingAuth(true);

    checkSession().then((authenticatedUser) => {
      if (!mounted) return;
      setIsCheckingAuth(false);
      if (authenticatedUser) {
        fetchRuns();
        fetchReviewCount();
        fetchAlertCount();
        fetchMatrix();
      } else {
        clearData();
      }
    });

    // When an API call returns 401 Unauthorized, revoke session and clear data
    const unsubscribe = onUnauthorized(() => {
      if (!mounted) return;
      setUser(null);
      clearData();
    });

    return () => {
      mounted = false;
      unsubscribe();
    };
  }, [checkSession, fetchRuns, fetchReviewCount, fetchMatrix, clearData]);

  // Reload data when selected run changes (only if authenticated)
  useEffect(() => {
    if (user && selectedRunId) {
      fetchMatrix(selectedRunId);
      fetchOffers(selectedRunId);
    }
  }, [user, selectedRunId, fetchMatrix, fetchOffers]);

  const handleLogout = async () => {
    try {
      await api.POST('/api/auth/logout');
    } catch (err) {
      console.error('Logout error:', err);
    } finally {
      setUser(null);
      clearData();
    }
  };

  const handleReloadAll = useCallback(() => {
    fetchRuns();
    fetchMatrix(selectedRunId);
    if (selectedRunId) fetchOffers(selectedRunId);
    fetchReviewCount();
    fetchAlertCount();
  }, [fetchRuns, fetchMatrix, fetchOffers, fetchReviewCount, fetchAlertCount, selectedRunId]);

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

  // The report date bar. On the pages that have a compact page header it sits in that header row; elsewhere it stays on top.
  const INLINE_TOOLBAR_VIEWS = ['overview', 'offers', 'weekly-actions', 'price-comparisons', 'ads-campaigns'];
  const reportToolbar = (
    <Toolbar
      runs={runs}
      selectedRunId={selectedRunId}
      onSelectRun={(id) => setSelectedRunId(id)}
      onReload={handleReloadAll}
      isLoading={isLoading}
    />
  );

  // The content of each section, with the props its view takes.
  const renderSection = (section: string): React.ReactNode => {
    switch (section) {
      case 'overview':
        return (
          <OverviewView
            matrix={matrix}
            offers={offers}
            reviewCount={reviewCount}
            onNavigate={navigateTo}
            onRefreshCollection={handleStartRun}
            toolbar={reportToolbar}
          />
        );
      case 'offers':
        return (
          <OffersView
            selectedRunId={selectedRunId}
            onOpenProductDetail={openProductDetail}
            toolbar={reportToolbar}
          />
        );
      case 'price':
        return (
          <PriceMatrixView
            matrix={matrix}
            matrixError={matrixError}
            selectedRunId={selectedRunId}
            isLoading={isLoading}
            onOpenProductDetail={openProductDetail}
            onRetry={() => fetchMatrix(selectedRunId)}
            toolbar={reportToolbar}
          />
        );
      case 'ads':
        return <AdsCampaignsView onOpenProductDetail={openProductDetail} toolbar={reportToolbar} />;
      case 'attention':
        return <ActionCenterView onOpenProductDetail={openProductDetail} onRefreshAlertCount={fetchAlertCount} />;
      case 'settings-alerts':
        return <SettingsAlertsView />;
      case 'settings-account':
        return <SettingsAccountView />;
      case 'settings-collector':
        return <SettingsCollectorView />;
      case 'data-quality':
        return <DataQualityView onOpenProductDetail={openProductDetail} onRefreshReviewCount={fetchReviewCount} />;
      case 'runs':
        return (
          <RunsView
            user={user}
            onOpenLogin={() => setIsLoginOpen(true)}
            onNavigateToMatrix={(runId) => {
              setSelectedRunId(runId);
              navigateTo('price-comparisons');
            }}
            onRefreshRuns={handleReloadAll}
            onActiveProgressChange={setActiveRunProgress}
          />
        );
      case 'sources':
        return <SourcesIdentitiesView />;
      case 'settings-ai':
        return <SettingsAiView />;
      case 'improve-recommendations':
        return (
          <div className="panel" style={{ padding: '40px', textAlign: 'center' }}>
            <div className="eyebrow">INTERNAL BUSINESS DATA</div>
            <h2 style={{ marginTop: '8px' }}>Internal Data Import</h2>
            <p style={{ color: 'var(--muted)', marginTop: '8px', maxWidth: '500px', margin: '8px auto 0' }}>
              Upload internal sales velocity, inventory, or margins (CSV/XLSX) to enrich market positioning recommendations.
            </p>
            <div style={{ marginTop: '20px', border: '2px dashed var(--line)', padding: '30px', borderRadius: '8px', background: '#fafbfc' }}>
              <p style={{ margin: 0, color: 'var(--muted)', fontSize: '13px' }}>
                Drag and drop <code>.csv</code> or <code>.xlsx</code> file here, or click to browse
              </p>
            </div>
          </div>
        );
      default:
        return null;
    }
  };

  return (
    <div className="app-shell">
      {/* Sidebar Navigation */}
      <Sidebar
        activeView={activeView}
        onNavigate={navigateTo}
        reviewCount={reviewCount}
        alertCount={alertCount}
        user={user}
        onOpenLogin={() => setIsLoginOpen(true)}
        onLogout={handleLogout}
        activeRunProgress={activeRunProgress}
      />

      {/* Main Workspace */}
      <div className={`workspace ${activeView === 'price-comparisons' ? 'workspace-fixed-matrix' : ''}`}>
        <Header
          onToggleSidebar={() => setIsSidebarCollapsed((prev) => !prev)}
          user={user}
          onOpenLogin={() => setIsLoginOpen(true)}
          activeRunProgress={activeRunProgress}
          onNavigateToRuns={() => navigateTo('runs')}
        />

        <main className={`main-content ${activeView === 'price-comparisons' ? 'main-content-matrix' : ''}`}>
          {isCheckingAuth ? (
            <div
              className="workspace-loading"
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                height: '60vh',
                flexDirection: 'column',
                gap: '12px',
              }}
            >
              <div className="mark" style={{ width: 36, height: 36 }}>
                <img
                  src="/assets/logo.png"
                  alt="Lazy Bean logo"
                  width="36"
                  height="36"
                  style={{ objectFit: 'contain' }}
                />
              </div>
              <div style={{ color: 'var(--muted)', fontSize: '14px' }}>
                Checking operations authorization...
              </div>
            </div>
          ) : !user ? (
            <UnlockScreen
              onSuccess={(loggedInUser) => {
                setUser(loggedInUser);
                setIsLoginOpen(false);
                fetchRuns();
                fetchReviewCount();
                fetchMatrix();
              }}
            />
          ) : (
            <>
              <CollectorBanner runActive={Boolean(activeRunProgress)} />

              {!INLINE_TOOLBAR_VIEWS.includes(activeView) && reportToolbar}

              {/* Sections: each one that has been opened stays mounted; only the section of the current view is shown. */}
              {sectionsToRender.map((section) => (
                <KeepAliveSection
                  key={section}
                  id={section}
                  active={section === activeSection}
                  registry={sectionRegistry.current}
                >
                  {renderSection(section)}
                </KeepAliveSection>
              ))}
            </>
          )}
        </main>

        <Footer />
      </div>

      {/* Product Detail & Price History Modal */}
      <ProductDetailModal
        isOpen={isProductDetailOpen}
        onClose={closeProductDetail}
        productId={activeDetailProductId}
        productTitle={activeDetailTitle}
        matrixRow={activeDetailRow}
      />

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
