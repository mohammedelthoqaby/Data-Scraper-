import React, { useEffect, useState } from 'react';
import { fetchCollectorStatus, type CollectorStatus } from '../../api/collector';

// Header status: whether a LazyBean Collector app is connected, and the progress of the job it is working on.
export const CollectorPill: React.FC = () => {
  const [status, setStatus] = useState<CollectorStatus | null>(null);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      try {
        const next = await fetchCollectorStatus();
        if (!cancelled) setStatus(next);
      } catch {
        // Keep the last known state; a signed-out session simply shows the placeholder.
      }
    };
    load();
    const timer = window.setInterval(load, 10000);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, []);

  let label = 'Collector: …';
  let color = '#64748b';
  let background = '#f1f5f9';
  let border = '#e2e8f0';

  if (status?.activeJob) {
    label = `Collector: Working ${status.activeGroupCompleted}/${status.activeGroupTotal}`;
    color = '#1d4ed8';
    background = '#eff6ff';
    border = '#bfdbfe';
  } else if (status?.connected) {
    label = 'Collector: Connected';
    color = '#15803d';
    background = '#f0fdf4';
    border = '#bbf7d0';
  } else if (status) {
    label = 'Collector: Not running';
    color = '#b45309';
    background = '#fffbeb';
    border = '#fde68a';
  }

  return (
    <a
      href="#/settings-collector"
      title="LazyBean Collector desktop app status"
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '6px',
        padding: '4px 10px',
        borderRadius: '9999px',
        background,
        color,
        border: `1px solid ${border}`,
        fontSize: '12px',
        fontWeight: 600,
        textDecoration: 'none',
        lineHeight: 1,
      }}
    >
      <span
        style={{
          width: '8px',
          height: '8px',
          borderRadius: '50%',
          background: color,
          display: 'inline-block',
        }}
      />
      {label}
    </a>
  );
};
