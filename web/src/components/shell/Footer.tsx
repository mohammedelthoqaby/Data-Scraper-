import React, { useEffect, useState } from 'react';

export const Footer: React.FC = () => {
  const [build, setBuild] = useState<string>('…');

  useEffect(() => {
    let cancelled = false;
    fetch('/api/version')
      .then((res) => (res.ok ? res.json() : Promise.reject(new Error(`HTTP ${res.status}`))))
      .then((data: { commitHash?: string }) => {
        if (!cancelled) setBuild(data.commitHash ?? 'unknown');
      })
      .catch(() => {
        if (!cancelled) setBuild('unavailable');
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <footer className="workspace-footer">
      Observed evidence supports review. Ad presence does not establish demand or campaign success. Read-only intelligence.
      <span style={{ marginLeft: '12px', color: 'var(--muted)', fontSize: '11px' }}>build {build}</span>
    </footer>
  );
};
