import React, { useState, useEffect } from 'react';
import { api, type RunDto, type AuthenticatedUser } from '../../api/client';

interface RunsViewProps {
  user: AuthenticatedUser | null;
  onOpenLogin: () => void;
  onNavigateToMatrix: (runId: string) => void;
  onRefreshRuns: () => void;
}

export const RunsView: React.FC<RunsViewProps> = ({
  user,
  onOpenLogin,
  onNavigateToMatrix,
  onRefreshRuns,
}) => {
  const [runs, setRuns] = useState<RunDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isStarting, setIsStarting] = useState(false);
  const [isCancelling, setIsCancelling] = useState(false);
  const [activeRun, setActiveRun] = useState<RunDto | null>(null);

  const fetchRunsList = async () => {
    setIsLoading(true);
    try {
      const res = await api.GET('/api/runs');
      if (res.data) {
        setRuns(res.data);
        const running = res.data.find((r) => r.status === 'Running');
        setActiveRun(running || null);
      }
    } catch (err: any) {
      console.error('Failed to fetch runs:', err);
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    fetchRunsList();
  }, []);

  const handleStartRun = async () => {
    if (!user) {
      onOpenLogin();
      return;
    }
    setIsStarting(true);
    try {
      await api.POST('/api/runs', { body: {} });
      await fetchRunsList();
      onRefreshRuns();
    } catch (err: any) {
      alert(err?.message || 'Failed to start collection run');
    } finally {
      setIsStarting(false);
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
      await api.POST('/api/runs/{id}/cancel', {
        params: { path: { id: runId } },
      });
      await fetchRunsList();
      onRefreshRuns();
    } catch (err: any) {
      alert(err?.message || 'Failed to cancel collection run');
    } finally {
      setIsCancelling(false);
    }
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

  return (
    <div className="runs-view">
      {/* Page Heading */}
      <div className="page-heading">
        <div>
          <div className="eyebrow">SCRAPER PIPELINE & ORCHESTRATION</div>
          <h1 className="view-title">Collection Runs</h1>
          <p className="subtitle">
            Trigger new collection passes across the 5 competitor stores, monitor real-time scraping progress, or cancel active tasks.
          </p>
        </div>
        <button
          type="button"
          className="btn-primary"
          disabled={isStarting || activeRun != null}
          onClick={handleStartRun}
        >
          {isStarting ? 'Initiating Run...' : '▶ Start Collection Run'}
        </button>
      </div>

      {/* Active Run Progress Banner */}
      {activeRun ? (
        <div className="run-live-banner">
          <div className="run-live-header">
            <div>
              <span className="badge good" style={{ marginRight: '8px' }}>
                ● Active Scraping in Progress
              </span>
              <strong>Run ID:</strong> {activeRun.id}
            </div>
            <button
              type="button"
              className="btn-xs reject"
              style={{ padding: '6px 14px', fontSize: '13px' }}
              disabled={isCancelling}
              onClick={() => handleCancelRun(activeRun.id!)}
            >
              {isCancelling ? 'Stopping...' : '⏹ Cancel Run'}
            </button>
          </div>

          <div className="run-progress-track">
            <div className="run-progress-bar" style={{ width: '65%' }} />
          </div>

          <div style={{ fontSize: '12px', color: 'var(--muted)', display: 'flex', gap: '20px' }}>
            <span>Started: {activeRun.startedAt ? new Date(activeRun.startedAt).toLocaleTimeString('en-GB') : '—'}</span>
            <span>Trigger: {activeRun.trigger}</span>
            <span>Elapsed: {formatDuration(activeRun.startedAt, null)}</span>
          </div>

          {activeRun.competitors && activeRun.competitors.length > 0 && (
            <div className="run-sources-grid">
              {activeRun.competitors.map((c) => (
                <div key={c.competitorId} className="run-source-card">
                  <div style={{ fontWeight: 600, fontSize: '13px' }}>{c.competitorName}</div>
                  <span className={`badge ${c.status === 'Completed' ? 'good' : 'warn'}`} style={{ fontSize: '10px' }}>
                    {c.status}
                  </span>
                  <div style={{ fontSize: '11px', color: 'var(--muted)', marginTop: '2px' }}>
                    {c.productsDiscovered ?? 0} items extracted
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      ) : (
        <div className="warning-banner" style={{ background: '#f4f9f6', borderColor: '#9bc9b1', color: '#1e3a2b', marginBottom: '20px' }}>
          <span>✓</span>
          <span>No background collection is currently running. You can trigger a new collection pass at any time.</span>
        </div>
      )}

      {/* Historical Runs Table */}
      <div className="panel" style={{ padding: 0, overflow: 'hidden' }}>
        <div className="panel-header" style={{ padding: '14px 18px', borderBottom: '1px solid var(--line)' }}>
          <h2 className="panel-title" style={{ fontSize: '16px' }}>Collection History ({runs.length})</h2>
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
          <div style={{ padding: '40px', textAlign: 'center', color: 'var(--muted)' }}>
            Loading runs history...
          </div>
        ) : (
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '13px' }}>
              <thead style={{ background: '#fafbfc', borderBottom: '1px solid var(--line)' }}>
                <tr>
                  <th style={{ padding: '10px 16px', textAlign: 'left' }}>Status</th>
                  <th style={{ padding: '10px 16px', textAlign: 'left' }}>Run ID</th>
                  <th style={{ padding: '10px 16px', textAlign: 'left' }}>Trigger</th>
                  <th style={{ padding: '10px 16px', textAlign: 'left' }}>Started At</th>
                  <th style={{ padding: '10px 16px', textAlign: 'left' }}>Duration</th>
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

                  return (
                    <tr key={r.id} style={{ borderBottom: '1px solid #edf0f2' }}>
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
                      <td style={{ padding: '12px 16px', textAlign: 'center' }}>
                        <span className="badge" style={{ background: '#f0f3f2' }}>
                          {r.competitors?.length || 6} stores
                        </span>
                      </td>
                      <td style={{ padding: '12px 16px', textAlign: 'right' }}>
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
