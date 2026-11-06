import React, { useState, useEffect } from 'react';
import {
  api,
  fetchRunProgress,
  fetchActiveRun,
  triggerMatchingNow,
  redispatchGithubCollector,
  type RunDto,
  type AuthenticatedUser,
  type RunProgress,
} from '../../api/client';

interface RunsViewProps {
  user: AuthenticatedUser | null;
  onOpenLogin: () => void;
  onNavigateToMatrix: (runId: string) => void;
  onRefreshRuns: () => void;
  onActiveProgressChange?: (progress: RunProgress | null) => void;
}

export const RunsView: React.FC<RunsViewProps> = ({
  user,
  onOpenLogin,
  onNavigateToMatrix,
  onRefreshRuns,
  onActiveProgressChange,
}) => {
  const [runs, setRuns] = useState<RunDto[]>([]);
  const [runsError, setRunsError] = useState<{ status?: number; message: string; correlationId?: string } | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isStarting, setIsStarting] = useState(false);
  const [isCancelling, setIsCancelling] = useState(false);
  const [isMatching, setIsMatching] = useState(false);
  const [matchNotice, setMatchNotice] = useState<string | null>(null);
  const [activeProgress, setActiveProgress] = useState<RunProgress | null>(null);
  const [expandedRunIds, setExpandedRunIds] = useState<Set<string>>(new Set());
  const [redispatchingSlug, setRedispatchingSlug] = useState<string | null>(null);
  const [redispatchNotice, setRedispatchNotice] = useState<string | null>(null);

  const handleRedispatch = async (runId: string, slug: string) => {
    setRedispatchingSlug(slug);
    setRedispatchNotice(null);
    try {
      const res = await redispatchGithubCollector(runId, slug);
      if (res.success) {
        setRedispatchNotice(`Workflow dispatched to GitHub Actions for ${slug}.`);
      } else {
        alert(res.message || 'Failed to re-dispatch.');
      }
    } catch (err: any) {
      alert(err?.message || 'Error re-dispatching GitHub Actions collector.');
    } finally {
      setRedispatchingSlug(null);
    }
  };

  const fetchRunsList = async () => {
    setIsLoading(true);
    try {
      const res = await api.GET('/api/runs');
      if (res.error || !res.response.ok) {
        const errObj: any = res.error || {};
        setRunsError({
          status: res.response.status,
          message: errObj.title || errObj.detail || errObj.message || `Server returned error (${res.response.status})`,
          correlationId: errObj.correlationId || res.response.headers.get('x-correlation-id') || undefined,
        });
      } else if (res.data) {
        setRunsError(null);
        setRuns(res.data);
      }
    } catch (err: any) {
      console.error('Failed to fetch runs:', err);
      setRunsError({
        status: err.status || 500,
        message: err.message || 'Network error fetching collection history',
        correlationId: err.correlationId,
      });
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    fetchRunsList();
  }, []);

  // Poll real-time active run progress every 3s
  // Source of truth is always the server's /api/runs/active endpoint (DB + ProgressTracker)
  useEffect(() => {
    let timer: any = null;
    let isMounted = true;

    const pollActiveProgress = async () => {
      try {
        const prog = await fetchActiveRun();
        if (!isMounted) return;

        if (prog && (prog.status === 'Running' || prog.status === 'Queued')) {
          setActiveProgress(prog);
          onActiveProgressChange?.(prog);
        } else {
          setActiveProgress((prev) => {
            if (prev) {
              // Run finished or stopped! Reload historical runs
              fetchRunsList();
              onRefreshRuns();
              onActiveProgressChange?.(null);
            }
            return null;
          });
        }
      } catch (err) {
        console.warn('Active run poll error:', err);
      }
    };

    pollActiveProgress();
    timer = setInterval(pollActiveProgress, 3000);

    return () => {
      isMounted = false;
      clearInterval(timer);
    };
  }, []);

  const handleStartRun = async () => {
    if (!user) {
      onOpenLogin();
      return;
    }
    setIsStarting(true);
    try {
      const res = await fetch('/api/runs', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({}),
      });

      if (res.status === 409) {
        alert('A collection run is already in progress.');
        const active = await fetchActiveRun();
        if (active) {
          setActiveProgress(active);
          onActiveProgressChange?.(active);
        }
        return;
      }

      if (!res.ok) {
        const errJson = await res.json().catch(() => null);
        throw new Error(errJson?.detail || errJson?.error || `Failed to start run (${res.status})`);
      }

      // Rehydrate immediately from server
      const active = await fetchActiveRun();
      if (active) {
        setActiveProgress(active);
        onActiveProgressChange?.(active);
      }
      await fetchRunsList();
      onRefreshRuns();
    } catch (err: any) {
      alert(err?.message || 'Failed to start collection run');
    } finally {
      setIsStarting(false);
    }
  };

  const handleRunMatching = async () => {
    if (!user) {
      onOpenLogin();
      return;
    }
    setIsMatching(true);
    setMatchNotice(null);
    try {
      const res = await triggerMatchingNow();
      setMatchNotice(
        `Matching complete: ${res?.autoAcceptedCount ?? 0} auto-accepted, ${res?.reviewItemCount ?? 0} reviews flagged.`
      );
      await fetchRunsList();
      onRefreshRuns();
    } catch (err: any) {
      alert(err?.message || 'Failed to execute product matching');
    } finally {
      setIsMatching(false);
    }
  };

  const handleCancelRun = async (runId: string) => {
    if (!user) {
      onOpenLogin();
      return;
    }
    const confirmed = window.confirm('Are you sure you want to cancel the active collection run?');
    if (!confirmed) return;

    setIsCancelling(true);
    try {
      const res = await fetch(`/api/runs/${runId}/cancel`, {
        method: 'POST',
        credentials: 'include',
      });
      if (!res.ok) {
        throw new Error(`Failed to cancel collection run (${res.status})`);
      }
      const active = await fetchActiveRun();
      setActiveProgress(active);
      onActiveProgressChange?.(active);
      await fetchRunsList();
      onRefreshRuns();
    } catch (err: any) {
      alert(err?.message || 'Failed to cancel collection run');
    } finally {
      setIsCancelling(false);
    }
  };

  const toggleExpand = (runId: string) => {
    setExpandedRunIds((prev) => {
      const next = new Set(prev);
      if (next.has(runId)) {
        next.delete(runId);
      } else {
        next.add(runId);
      }
      return next;
    });
  };

  const formatDuration = (startStr?: string | null, finishStr?: string | null) => {
    if (!startStr) return '—';
    const start = new Date(startStr).getTime();
    const end = finishStr ? new Date(finishStr).getTime() : Date.now();
    const sec = Math.max(0, Math.round((end - start) / 1000));
    const mins = Math.floor(sec / 60);
    const remSec = sec % 60;
    return `${mins}m ${remSec}s`;
  };

  const isAnyRunActive = activeProgress != null || runs.some((r) => r.status === 'Running' || r.status === 'Queued');

  return (
    <div className="runs-view">
      {/* Page Heading */}
      <div className="page-heading">
        <div>
          <div className="eyebrow">SCRAPER PIPELINE & ORCHESTRATION</div>
          <h1 className="view-title">Collection Runs</h1>
          <p className="subtitle">
            Trigger new collection passes across the 5 competitor stores, monitor real-time weighted scraping progress, or run matching manually.
          </p>
        </div>
        <div style={{ display: 'flex', gap: '10px' }}>
          <button
            type="button"
            className="btn-secondary"
            disabled={isMatching || isStarting}
            onClick={handleRunMatching}
            style={{ fontWeight: 600 }}
          >
            ⚡ {isMatching ? 'Matching in progress...' : 'Run Matching Now'}
          </button>
          <button
            type="button"
            className="btn-primary"
            disabled={isStarting || isAnyRunActive}
            onClick={handleStartRun}
            title={isAnyRunActive ? 'A collection run is currently in progress' : undefined}
          >
            {isStarting ? 'Initiating Run...' : '▶ Start Collection Run'}
          </button>
        </div>

        {runsError && (
          <div
            style={{
              padding: '16px 20px',
              background: '#fee2e2',
              border: '1px solid #f87171',
              borderRadius: '8px',
              margin: '16px',
              color: '#991b1b',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: '12px',
            }}
          >
            <div>
              <div style={{ fontWeight: 700, fontSize: '14px', marginBottom: '4px' }}>
                ⚠️ Failed to load Collection History (HTTP {runsError.status || 500})
              </div>
              <div style={{ fontSize: '13px' }}>{runsError.message}</div>
              {runsError.correlationId && (
                <div style={{ fontSize: '11px', color: '#b91c1c', marginTop: '4px', fontFamily: 'monospace' }}>
                  Correlation ID: {runsError.correlationId}
                </div>
              )}
            </div>
            <button
              type="button"
              className="btn-secondary"
              style={{
                background: '#fff',
                borderColor: '#dc2626',
                color: '#dc2626',
                fontWeight: 600,
                padding: '6px 16px',
                flexShrink: 0,
              }}
              onClick={fetchRunsList}
            >
              Retry
            </button>
          </div>
        )}
      </div>

      {matchNotice && (
        <div
          className="warning-banner"
          style={{ background: '#e7f3ec', borderColor: '#9bc9b1', color: '#1b5238', marginBottom: '20px' }}
        >
          <span>✓</span>
          <span>{matchNotice}</span>
        </div>
      )}

      {/* Active Run Real-Time Progress Banner */}
      {activeProgress ? (
        <div className="run-live-banner" style={{ marginBottom: '24px' }}>
          <div className="run-live-header">
            <div>
              <span className="badge good" style={{ marginRight: '8px' }}>
                ● Active Pipeline in Progress
              </span>
              <strong>Run ID:</strong> {activeProgress.runId}
            </div>
            <button
              type="button"
              className="btn-xs reject"
              style={{ padding: '6px 14px', fontSize: '13px' }}
              disabled={isCancelling}
              onClick={() => handleCancelRun(activeProgress.runId)}
            >
              {isCancelling ? 'Stopping...' : '⏹ Cancel Run'}
            </button>
          </div>

          {/* Stalled Alert if no progress for > 3 minutes (suppressed if waiting on GitHub collector) */}
          {(() => {
            const isAnyGithubCollectorWaiting = activeProgress.competitors?.some(
              (c) => (c.collectionHost === 'github' || (c.lastMessage || '').includes('GitHub collector')) &&
                     ((c.status || '').toLowerCase() === 'running' || (c.status || '').toLowerCase() === 'queued')
            );
            return activeProgress.isStalled && !isAnyGithubCollectorWaiting ? (
              <div
                className="warning-banner"
                style={{
                  background: '#fff3cd',
                  borderColor: '#ffeeba',
                  color: '#856404',
                  marginTop: '12px',
                  marginBottom: '12px',
                }}
              >
                <span>⚠️</span>
                <span>
                  <strong>Scraper appears stalled:</strong> No progress updates received for &gt; 3 minutes.
                  {activeProgress.lastLogMessage && (
                    <span style={{ display: 'block', fontSize: '11px', marginTop: '4px' }}>
                      Last activity: <em>{activeProgress.lastLogMessage}</em>
                    </span>
                  )}
                </span>
              </div>
            ) : null;
          })()}

          {/* Redispatch notification */}
          {redispatchNotice && (
            <div
              className="success-banner"
              style={{
                background: '#d4edda',
                borderColor: '#c3e6cb',
                color: '#155724',
                padding: '8px 12px',
                borderRadius: '6px',
                marginTop: '12px',
                marginBottom: '12px',
                fontSize: '13px',
              }}
            >
              ✓ {redispatchNotice}
            </div>
          )}

          {/* Real Weighted Progress Bar */}
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '12px', marginBottom: '6px', fontWeight: 600 }}>
            <span>Overall Progress</span>
            <span>{activeProgress.overallPercent}%</span>
          </div>
          <div className="run-progress-track">
            <div
              className="run-progress-bar"
              style={{
                width: `${Math.max(activeProgress.overallPercent, 5)}%`,
                transition: 'width 0.4s ease',
              }}
            />
          </div>

          {(() => {
            const runStartedAt = activeProgress.startedAt;
            const matchingRun = runs.find((r) => r.id === activeProgress.runId);
            return (
              <div style={{ fontSize: '12px', color: 'var(--muted)', display: 'flex', gap: '20px', marginTop: '10px' }}>
                <span>Started: {runStartedAt ? new Date(runStartedAt).toLocaleTimeString('en-GB') : '—'}</span>
                <span>Trigger: {matchingRun?.trigger || 'Manual'}</span>
                <span>Elapsed: {formatDuration(runStartedAt, null)}</span>
              </div>
            );
          })()}

          {/* Real Per-Competitor Scraper Table */}
          {activeProgress?.competitors && activeProgress.competitors.length > 0 && (
            <div style={{ marginTop: '16px', overflowX: 'auto' }}>
              <div style={{ fontSize: '12px', fontWeight: 700, color: 'var(--muted)', marginBottom: '8px', textTransform: 'uppercase' }}>
                Competitor Store Progress
              </div>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '12px', background: '#fff', borderRadius: '6px', border: '1px solid var(--line)' }}>
                <thead>
                  <tr style={{ background: '#f8f9fa', borderBottom: '1px solid var(--line)', textAlign: 'left', color: 'var(--muted)' }}>
                    <th style={{ padding: '8px 12px' }}>Store</th>
                    <th style={{ padding: '8px 12px' }}>Status</th>
                    <th style={{ padding: '8px 12px' }}>Pages Scraped</th>
                    <th style={{ padding: '8px 12px' }}>Products Saved</th>
                    <th style={{ padding: '8px 12px' }}>Latest Activity</th>
                  </tr>
                </thead>
                <tbody>
                  {activeProgress.competitors.map((c) => {
                    const st = (c.status || '').toLowerCase();
                    const badgeClass = st === 'completed' ? 'good' : st === 'running' ? 'warn' : st === 'degraded' || st === 'suspicious' ? 'warn' : 'bad';
                    return (
                      <tr key={c.competitorId} style={{ borderBottom: '1px solid #f0f0f0' }}>
                        <td style={{ padding: '8px 12px', fontWeight: 600 }}>
                          {c.competitorName}
                          {c.isReference && (
                            <span className="badge good" style={{ fontSize: '9px', marginLeft: '6px' }}>
                              Reference
                            </span>
                          )}
                          {c.collectionHost === 'github' && (
                            <span className="badge info" style={{ fontSize: '9px', marginLeft: '6px', background: '#e9ecef', color: '#495057' }}>
                              GitHub Actions
                            </span>
                          )}
                        </td>
                        <td style={{ padding: '8px 12px' }}>
                          <span className={`badge ${badgeClass}`} style={{ fontSize: '10px' }}>
                            {c.status}
                          </span>
                        </td>
                        <td style={{ padding: '8px 12px' }}>
                          {c.totalPages && c.totalPages >= c.currentPage ? `${c.currentPage} / ${c.totalPages}` : `${c.currentPage} pages`}
                        </td>
                        <td style={{ padding: '8px 12px', fontWeight: 600 }}>
                          {c.productsSaved} items
                        </td>
                        <td style={{ padding: '8px 12px', color: 'var(--muted)' }}>
                          <div>{c.lastMessage || '—'}</div>
                          {c.collectionHost === 'github' && (
                            <div style={{ marginTop: '4px', display: 'flex', alignItems: 'center', gap: '8px', fontSize: '11px' }}>
                              <a
                                href="https://github.com/mohammedelthoqaby/Data-Scraper-/actions/workflows/github-collect.yml"
                                target="_blank"
                                rel="noopener noreferrer"
                                style={{ color: '#0366d6', textDecoration: 'underline' }}
                              >
                                View GitHub Actions ↗
                              </a>
                              {(st === 'running' || st === 'queued' || st === 'failed' || st === 'waiting') && (
                                <button
                                  type="button"
                                  className="btn-xs"
                                  style={{ padding: '2px 8px', fontSize: '10px' }}
                                  disabled={redispatchingSlug === c.competitorKey}
                                  onClick={() => handleRedispatch(activeProgress.runId, c.competitorKey)}
                                >
                                  {redispatchingSlug === c.competitorKey ? 'Dispatching...' : '↻ Re-dispatch'}
                                </button>
                              )}
                            </div>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}

          {/* Post-Processing Phases (Matching, AI, Alerts) */}
          {activeProgress?.phases && activeProgress.phases.length > 0 && (
            <div style={{ marginTop: '16px', borderTop: '1px solid var(--line)', paddingTop: '12px' }}>
              <div style={{ fontSize: '11px', fontWeight: 700, color: 'var(--muted)', marginBottom: '8px', textTransform: 'uppercase' }}>
                Post-Processing Pipeline
              </div>
              <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap' }}>
                {activeProgress.phases.map((ph) => {
                  const isDone = ph.status === 'completed';
                  const isRun = ph.status === 'Running';
                  const isSkip = ph.status === 'skipped';
                  return (
                    <div
                      key={ph.name}
                      style={{
                        background: '#fff',
                        border: '1px solid var(--line)',
                        borderRadius: '6px',
                        padding: '6px 12px',
                        fontSize: '12px',
                      }}
                    >
                      <span style={{ fontWeight: 600, textTransform: 'capitalize' }}>{ph.name}:</span>{' '}
                      <span
                        className={`badge ${isDone ? 'good' : isRun ? 'warn' : isSkip ? 'neutral' : 'bad'}`}
                        style={{ fontSize: '10px' }}
                      >
                        {ph.status}
                      </span>
                      {ph.message && (
                        <span style={{ color: 'var(--muted)', marginLeft: '6px' }}>({ph.message})</span>
                      )}
                      {ph.durationMs != null && ph.durationMs > 0 && (
                        <span style={{ color: 'var(--muted)', marginLeft: '4px', fontSize: '11px' }}>
                          [{ph.durationMs}ms]
                        </span>
                      )}
                    </div>
                  );
                })}
              </div>
            </div>
          )}
        </div>
      ) : (
        <div
          className="warning-banner"
          style={{ background: '#f4f9f6', borderColor: '#9bc9b1', color: '#1e3a2b', marginBottom: '20px' }}
        >
          <span>✓</span>
          <span>No background collection is currently running. You can trigger a new collection pass at any time.</span>
        </div>
      )}

      {/* Historical Runs Table */}
      <div className="panel" style={{ padding: 0, overflow: 'hidden' }}>
        <div className="panel-header" style={{ padding: '14px 18px', borderBottom: '1px solid var(--line)' }}>
          <h2 className="panel-title" style={{ fontSize: '16px' }}>
            Collection History {isLoading ? <span className="skeleton skeleton-text" style={{ width: '40px', display: 'inline-block' }} /> : runsError ? '(Error)' : `(${runs.length})`}
          </h2>
          <button
            type="button"
            className="btn-secondary"
            style={{ fontSize: '12px', padding: '4px 10px' }}
            onClick={fetchRunsList}
          >
            Refresh List
          </button>
        </div>

        {isLoading ? (
          <div style={{ padding: '24px' }}>
            <div className="skeleton skeleton-row" style={{ height: '40px', marginBottom: '8px' }} />
            <div className="skeleton skeleton-row" style={{ height: '40px', marginBottom: '8px' }} />
            <div className="skeleton skeleton-row" style={{ height: '40px', marginBottom: '8px' }} />
            <div className="skeleton skeleton-row" style={{ height: '40px', marginBottom: '8px' }} />
          </div>
        ) : (
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '13px' }}>
              <thead style={{ background: '#fafbfc', borderBottom: '1px solid var(--line)' }}>
                <tr>
                  <th style={{ padding: '10px 16px', textAlign: 'left', width: '30px' }} />
                  <th style={{ padding: '10px 16px', textAlign: 'left' }}>Status</th>
                  <th style={{ padding: '10px 16px', textAlign: 'left' }}>Run ID</th>
                  <th style={{ padding: '10px 16px', textAlign: 'left' }}>Trigger</th>
                  <th style={{ padding: '10px 16px', textAlign: 'left' }}>Started At</th>
                  <th style={{ padding: '10px 16px', textAlign: 'left' }}>Duration</th>
                  <th style={{ padding: '10px 16px', textAlign: 'center' }}>Matches</th>
                  <th style={{ padding: '10px 16px', textAlign: 'center' }}>Stores</th>
                  <th style={{ padding: '10px 16px', textAlign: 'right' }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {runs.map((r) => {
                  const status = (r.status || '').toLowerCase();
                  const isCompleted = status === 'completed';
                  const isPartial = status === 'partial';
                  const isFailed = status === 'failed';
                  const isRunning = status === 'running';
                  const isExpanded = expandedRunIds.has(r.id!);

                  let parsedPhases: any[] = [];
                  if (r.phasesJson) {
                    try {
                      const parsed = JSON.parse(r.phasesJson);
                      if (Array.isArray(parsed)) {
                        parsedPhases = parsed;
                      }
                    } catch {}
                  }

                  return (
                    <React.Fragment key={r.id}>
                      <tr
                        style={{
                          borderBottom: isExpanded ? 'none' : '1px solid #edf0f2',
                          cursor: 'pointer',
                          background: isExpanded ? '#f9fafb' : 'transparent',
                        }}
                        onClick={() => toggleExpand(r.id!)}
                      >
                        <td style={{ padding: '12px 10px', textAlign: 'center', color: 'var(--muted)' }}>
                          {isExpanded ? '▼' : '▶'}
                        </td>
                        <td style={{ padding: '12px 16px' }}>
                          <span
                            className={`badge ${
                              isCompleted ? 'good' : isPartial ? 'warn' : isFailed ? 'bad' : isRunning ? 'good' : 'neutral'
                            }`}
                          >
                            {r.status}
                          </span>
                        </td>
                        <td style={{ padding: '12px 16px', fontFamily: 'monospace', fontSize: '12px', color: 'var(--muted)' }}>
                          {r.id?.substring(0, 13)}...
                        </td>
                        <td style={{ padding: '12px 16px' }}>{r.trigger}</td>
                        <td style={{ padding: '12px 16px', color: 'var(--ink)' }}>
                          {r.startedAt ? new Date(r.startedAt).toLocaleString('en-GB') : '—'}
                        </td>
                        <td style={{ padding: '12px 16px', fontVariantNumeric: 'tabular-nums' }}>
                          {formatDuration(r.startedAt, r.finishedAt)}
                        </td>
                        <td style={{ padding: '12px 16px', textAlign: 'center', fontWeight: 600 }}>
                          {r.matchesCount != null ? (
                            <span className="badge good">{r.matchesCount}</span>
                          ) : (
                            <span style={{ color: 'var(--muted)' }}>—</span>
                          )}
                        </td>
                        <td style={{ padding: '12px 16px', textAlign: 'center' }}>
                          <span className="badge" style={{ background: '#f0f3f2' }}>
                            {r.competitors?.length || 6} stores
                          </span>
                        </td>
                        <td style={{ padding: '12px 16px', textAlign: 'right' }} onClick={(e) => e.stopPropagation()}>
                          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                            {isRunning ? (
                              <button
                                type="button"
                                className="btn-xs reject"
                                onClick={() => handleCancelRun(r.id!)}
                              >
                                Cancel
                              </button>
                            ) : (
                              <>
                                <button
                                  type="button"
                                  className="btn-xs ghost"
                                  onClick={() => onNavigateToMatrix(r.id!)}
                                >
                                  View Matrix ↗
                                </button>
                                <a
                                  href={`/api/runs/${r.id}/export-matrix.csv`}
                                  download
                                  className="btn-xs ghost"
                                  style={{ textDecoration: 'none' }}
                                >
                                  CSV
                                </a>
                              </>
                            )}
                          </div>
                        </td>
                      </tr>

                      {/* Expandable Row with Per-Competitor Counts and Matches Details */}
                      {isExpanded && (
                        <tr style={{ borderBottom: '1px solid #edf0f2', background: '#f9fafb' }}>
                          <td colSpan={9} style={{ padding: '14px 20px 20px 36px' }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '10px' }}>
                              <strong style={{ fontSize: '13px' }}>Store Collection Breakdown</strong>
                              <span style={{ fontSize: '12px', color: 'var(--muted)' }}>
                                Auto-matches created: <strong>{r.matchesCount ?? '0'}</strong>
                              </span>
                            </div>

                            <table style={{ width: '100%', background: '#fff', border: '1px solid var(--line)', borderRadius: '6px', fontSize: '12px', borderCollapse: 'collapse', marginBottom: '12px' }}>
                              <thead>
                                <tr style={{ background: '#f0f3f2', textAlign: 'left', color: 'var(--muted)', borderBottom: '1px solid var(--line)' }}>
                                  <th style={{ padding: '6px 12px' }}>Store Name</th>
                                  <th style={{ padding: '6px 12px' }}>Products Collected</th>
                                  <th style={{ padding: '6px 12px' }}>Pages Scraped</th>
                                  <th style={{ padding: '6px 12px' }}>Status</th>
                                  <th style={{ padding: '6px 12px' }}>Errors</th>
                                  <th style={{ padding: '6px 12px' }}>Coverage</th>
                                </tr>
                              </thead>
                              <tbody>
                                {(r.competitors || []).map((c: any) => (
                                  <tr key={c.competitorId} style={{ borderBottom: '1px solid #f0f0f0' }}>
                                    <td style={{ padding: '6px 12px', fontWeight: 600 }}>
                                      {c.competitorName || c.competitorKey}
                                      {c.isReference && (
                                        <span className="badge good" style={{ fontSize: '9px', marginLeft: '6px' }}>
                                          Reference
                                        </span>
                                      )}
                                    </td>
                                    <td style={{ padding: '6px 12px', fontWeight: 600 }}>
                                      {c.products ?? 0} items
                                    </td>
                                    <td style={{ padding: '6px 12px' }}>{c.pages ?? 0} pages</td>
                                    <td style={{ padding: '6px 12px' }}>
                                      <span
                                        className={`badge ${c.status === 'Completed' ? 'good' : c.status === 'Failed' ? 'bad' : 'warn'}`}
                                        style={{ fontSize: '10px' }}
                                      >
                                        {c.status}
                                      </span>
                                    </td>
                                    <td style={{ padding: '6px 12px' }}>
                                      {c.errors > 0 ? (
                                        <span className="badge bad" style={{ fontSize: '10px' }}>
                                          {c.errors} {c.errors === 1 ? 'error' : 'errors'}
                                        </span>
                                      ) : (
                                        <span style={{ color: 'var(--muted)', fontSize: '11px' }}>0</span>
                                      )}
                                    </td>
                                    <td style={{ padding: '6px 12px' }}>
                                      <span className="badge neutral" style={{ fontSize: '10px' }}>
                                        {c.coverage}
                                      </span>
                                    </td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>

                            {parsedPhases.length > 0 && (
                              <div style={{ fontSize: '12px' }}>
                                <strong style={{ color: 'var(--muted)', display: 'block', marginBottom: '4px' }}>
                                  Post-Collection Execution:
                                </strong>
                                <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
                                  {parsedPhases.map((p: any) => (
                                    <span
                                      key={p.name}
                                      className="badge neutral"
                                      style={{ fontSize: '11px', background: '#fff', border: '1px solid var(--line)' }}
                                    >
                                      <strong style={{ textTransform: 'capitalize' }}>{p.name}</strong>: {p.status}
                                      {p.durationMs != null ? ` (${p.durationMs}ms)` : ''} — {p.message}
                                    </span>
                                  ))}
                                </div>
                              </div>
                            )}
                          </td>
                        </tr>
                      )}
                    </React.Fragment>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
};
