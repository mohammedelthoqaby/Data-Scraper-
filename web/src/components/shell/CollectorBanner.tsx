import React, { useEffect, useState } from 'react';
import { fetchCollectorStatus, type CollectorStatus } from '../../api/collector';

interface CollectorBannerProps {
  runActive: boolean;
}

// Shown while a run is in progress and no desktop app is connected, because Strong Coffee and Meta
// ads only run on the PC that has the app open.
export const CollectorBanner: React.FC<CollectorBannerProps> = ({ runActive }) => {
  const [status, setStatus] = useState<CollectorStatus | null>(null);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      try {
        const next = await fetchCollectorStatus();
        if (!cancelled) setStatus(next);
      } catch {
        // Unknown state: show nothing rather than a false warning.
      }
    };
    load();
    const timer = window.setInterval(load, 10000);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, []);

  if (!runActive || !status || status.connected) {
    return null;
  }

  return (
    <div
      className="warning-banner"
      role="status"
      style={{
        background: '#fff3cd',
        borderColor: '#ffeeba',
        color: '#856404',
        marginBottom: '12px',
      }}
    >
      <span>⚠️</span>
      <span>
        <strong>No LazyBean Collector is connected.</strong> Open LazyBean Collector on your PC to collect Strong Coffee
        and Meta ads. Google ads run on the server.{' '}
        <a href="#/settings-collector" style={{ color: 'inherit', textDecoration: 'underline' }}>
          Collector settings
        </a>
      </span>
    </div>
  );
};
