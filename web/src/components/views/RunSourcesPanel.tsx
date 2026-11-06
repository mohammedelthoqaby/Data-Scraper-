import React, { useEffect, useState } from 'react';
import { fetchRunJobs, type CollectorJobSummary } from '../../api/collector';

interface RunSourcesPanelProps {
  runId: string;
}

const SOURCE_LABELS: Record<string, string> = {
  StoreCollection: 'Store catalog',
  MetaAds: 'Meta ads',
  GoogleAds: 'Google ads',
};

const STATUS_LABELS: Record<string, { text: string; tone: string }> = {
  Queued: { text: 'Waiting for the desktop app', tone: 'warn' },
  Claimed: { text: 'Running', tone: 'warn' },
  Completed: { text: 'Completed', tone: 'good' },
  Failed: { text: 'Failed', tone: 'bad' },
  Skipped: { text: 'Skipped', tone: 'warn' },
};

// Per-source status for a run: which jobs ran, whether the desktop app or the server ran them, and how each ended.
export const RunSourcesPanel: React.FC<RunSourcesPanelProps> = ({ runId }) => {
  const [jobs, setJobs] = useState<CollectorJobSummary[]>([]);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      try {
        const next = await fetchRunJobs(runId);
        if (!cancelled) setJobs(next);
      } catch {
        // Keep the last loaded state.
      }
    };
    load();
    const timer = window.setInterval(load, 10000);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [runId]);

  if (jobs.length === 0) {
    return null;
  }

  return (
    <div className="panel" style={{ marginBottom: '12px', padding: '12px 16px' }}>
      <div style={{ fontWeight: 600, fontSize: '13px', marginBottom: '8px' }}>Sources for this run</div>
      <table style={{ width: '100%', fontSize: '12px', borderCollapse: 'collapse' }}>
        <thead>
          <tr style={{ textAlign: 'left', color: 'var(--muted)' }}>
            <th style={{ padding: '4px 8px' }}>Source</th>
            <th style={{ padding: '4px 8px' }}>Ran on</th>
            <th style={{ padding: '4px 8px' }}>Status</th>
            <th style={{ padding: '4px 8px' }}>Details</th>
          </tr>
        </thead>
        <tbody>
          {jobs.map((job) => {
            const label = SOURCE_LABELS[job.kind] ?? job.kind;
            const status = STATUS_LABELS[job.status] ?? { text: job.status, tone: 'warn' };
            return (
              <tr key={job.jobId} style={{ borderTop: '1px solid #f0f0f0' }}>
                <td style={{ padding: '6px 8px', fontWeight: 600 }}>
                  {label}
                  {job.competitorSlug ? ` (${job.competitorSlug})` : ''}
                </td>
                <td style={{ padding: '6px 8px' }}>{job.source === 'server' ? 'Server' : 'Desktop app'}</td>
                <td style={{ padding: '6px 8px' }}>
                  <span className={`badge ${status.tone}`} style={{ fontSize: '10px' }}>
                    {status.text}
                  </span>
                </td>
                <td style={{ padding: '6px 8px', color: 'var(--muted)' }}>{job.summary ?? '—'}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
};
