import React from 'react';
import type { CollectionRun } from '../../api/client';

interface ToolbarProps {
  runs: CollectionRun[];
  selectedRunId: string | null;
  onSelectRun: (runId: string) => void;
  onReload: () => void;
  isLoading?: boolean;
}

export const Toolbar: React.FC<ToolbarProps> = ({
  runs,
  selectedRunId,
  onSelectRun,
  onReload,
  isLoading,
}) => {
  const currentRun = runs.find((r) => r.id === selectedRunId) || runs[0];

  const formatRunDate = (dateStr?: string) => {
    if (!dateStr) return 'Latest observation';
    try {
      const d = new Date(dateStr);
      return new Intl.DateTimeFormat('en-GB', {
        timeZone: 'Africa/Cairo',
        dateStyle: 'medium',
        timeStyle: 'short',
      }).format(d);
    } catch {
      return dateStr;
    }
  };

  return (
    <div className="toolbar">
      <div className="toolbar-group">
        <label htmlFor="snapshot-select" style={{ fontWeight: 600, color: 'var(--muted)' }}>
          Report date:
        </label>
        <select
          id="snapshot-select"
          className="toolbar-select"
          value={selectedRunId || ''}
          onChange={(e) => onSelectRun(e.target.value)}
        >
          {runs.map((r) => (
            <option key={r.id} value={r.id}>
              {formatRunDate(r.startedAt)} ({r.status})
            </option>
          ))}
          {runs.length === 0 && <option value="">Latest observations</option>}
        </select>

        {currentRun && (
          <span
            className={`badge ${
              currentRun.status === 'Completed'
                ? 'good'
                : currentRun.status === 'Degraded' || currentRun.status === 'Partial'
                ? 'warn'
                : 'neutral'
            }`}
          >
            {currentRun.status}
          </span>
        )}
      </div>

      <div className="toolbar-group">
        <button
          type="button"
          className="btn-secondary"
          onClick={onReload}
          disabled={isLoading}
        >
          {isLoading ? '⏳ Loading...' : '🔄 Reload'}
        </button>
      </div>
    </div>
  );
};
