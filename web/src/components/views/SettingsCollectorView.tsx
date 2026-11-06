import React, { useCallback, useEffect, useState } from 'react';
import {
  collectAdsNow,
  fetchCollectorAppInfo,
  fetchCollectorDevices,
  fetchCollectorStatus,
  revokeCollectorDevice,
  type CollectorAppInfo,
  type CollectorDevice,
  type CollectorStatus,
} from '../../api/collector';

const DEFAULT_SERVER_URL = 'https://lazy-bean.runasp.net';

function formatWhen(value: string | null): string {
  if (!value) return 'never';
  const date = new Date(value);
  const seconds = Math.round((Date.now() - date.getTime()) / 1000);
  if (seconds < 60) return 'just now';
  if (seconds < 3600) return `${Math.round(seconds / 60)} min ago`;
  if (seconds < 86400) return `${Math.round(seconds / 3600)} h ago`;
  return date.toLocaleString();
}

function formatMegabytes(bytes: number | null): string {
  if (!bytes) return '';
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export const SettingsCollectorView: React.FC = () => {
  const [status, setStatus] = useState<CollectorStatus | null>(null);
  const [devices, setDevices] = useState<CollectorDevice[]>([]);
  const [appInfo, setAppInfo] = useState<CollectorAppInfo | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const [nextStatus, nextDevices, nextApp] = await Promise.all([
        fetchCollectorStatus(),
        fetchCollectorDevices(),
        fetchCollectorAppInfo(),
      ]);
      setStatus(nextStatus);
      setDevices(nextDevices);
      setAppInfo(nextApp);
      setError(null);
    } catch (err: any) {
      setError(err?.message || 'Could not load collector status.');
    }
  }, []);

  useEffect(() => {
    load();
    const timer = window.setInterval(load, 10000);
    return () => window.clearInterval(timer);
  }, [load]);

  const handleRevoke = async (device: CollectorDevice) => {
    if (!window.confirm(`Revoke "${device.name}"? That app will need to be paired again to collect.`)) return;
    setBusy(true);
    try {
      await revokeCollectorDevice(device.id);
      setNotice(`Revoked ${device.name}.`);
      await load();
    } catch (err: any) {
      setError(err?.message || 'Could not revoke the device.');
    } finally {
      setBusy(false);
    }
  };

  const handleCollectAds = async () => {
    setBusy(true);
    setNotice(null);
    try {
      await collectAdsNow();
      setNotice(
        status?.connected
          ? 'Meta and Google ads collection queued for the desktop app.'
          : 'Collection queued. No desktop app is connected, so Google ads run on the server and Meta ads wait for the app.'
      );
      await load();
    } catch (err: any) {
      setError(err?.message || 'Could not queue ads collection.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="settings-ai-view">
      <div className="page-heading">
        <div className="eyebrow">Settings</div>
        <h2 className="view-title">LazyBean Collector</h2>
        <p className="subtitle">
          The desktop app collects Strong Coffee and Meta ads from your PC, which avoids blocks on server addresses.
          Google ads run on the server when no app is connected.
        </p>
      </div>

      {error && <div className="warning-banner" style={{ marginBottom: '12px' }}>{error}</div>}
      {notice && <div className="success-banner" style={{ marginBottom: '12px' }}>{notice}</div>}

      <section className="panel" style={{ marginBottom: '16px' }}>
        <h3 style={{ marginTop: 0 }}>Status</h3>
        {status ? (
          <ul style={{ margin: 0, paddingLeft: '18px', lineHeight: 1.7 }}>
            <li>{status.connected ? `Connected (${status.connectedDeviceCount} app${status.connectedDeviceCount > 1 ? 's' : ''})` : 'Not running: no paired app has checked in recently.'}</li>
            {status.activeJob && (
              <li>
                Working on {status.activeJob.kind === 'StoreCollection' ? `store ${status.activeJob.competitorSlug ?? ''}` : status.activeJob.kind === 'MetaAds' ? 'Meta ads' : 'Google ads'}
                {status.activeJob.deviceName ? ` on ${status.activeJob.deviceName}` : ''}, last update {formatWhen(status.activeJob.lastHeartbeatAt)}.
              </li>
            )}
            <li>
              Current batch: {status.activeGroupCompleted} of {status.activeGroupTotal} jobs finished
              {status.queuedJobCount > 0 ? `, ${status.queuedJobCount} queued for an app` : ''}.
            </li>
          </ul>
        ) : (
          <div>Loading…</div>
        )}
        <div style={{ marginTop: '12px' }}>
          <button type="button" className="btn-primary" disabled={busy} onClick={handleCollectAds}>
            Collect ads now
          </button>
        </div>
      </section>

      <section className="panel" style={{ marginBottom: '16px' }}>
        <h3 style={{ marginTop: 0 }}>Install the app</h3>
        {appInfo?.available ? (
          <>
            <p style={{ marginTop: 0 }}>
              <a href={appInfo.downloadUrl ?? '#'} download className="btn-secondary" style={{ textDecoration: 'none' }}>
                Download LazyBeanCollector.exe {formatMegabytes(appInfo.sizeBytes)}
              </a>
            </p>
            <div style={{ fontSize: '12px', color: 'var(--muted)', wordBreak: 'break-all' }}>
              SHA-256: <code>{appInfo.sha256}</code>
            </div>
            <p style={{ fontSize: '12px', color: 'var(--muted)' }}>
              Windows SmartScreen may warn that the app is not recognised. Click <em>More info</em>, then <em>Run anyway</em>.
            </p>
          </>
        ) : (
          <p style={{ marginTop: 0 }}>The desktop installer has not been published yet.</p>
        )}
        <p style={{ fontSize: '12px', color: 'var(--muted)', marginBottom: 0 }}>
          To pair, open the app, keep the default server address ({DEFAULT_SERVER_URL}), and sign in with your dashboard
          username and password.
        </p>
      </section>

      <section className="panel">
        <h3 style={{ marginTop: 0 }}>Paired devices</h3>
        {devices.length === 0 ? (
          <div style={{ color: 'var(--muted)' }}>No app has been paired yet.</div>
        ) : (
          <table style={{ width: '100%', fontSize: '13px', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ textAlign: 'left', color: 'var(--muted)' }}>
                <th style={{ padding: '6px 8px' }}>Name</th>
                <th style={{ padding: '6px 8px' }}>Paired by</th>
                <th style={{ padding: '6px 8px' }}>Last seen</th>
                <th style={{ padding: '6px 8px' }}>State</th>
                <th style={{ padding: '6px 8px' }} />
              </tr>
            </thead>
            <tbody>
              {devices.map((device) => {
                const state = device.revokedAt ? 'Revoked' : device.isConnected ? 'Connected' : 'Offline';
                const tone = state === 'Connected' ? 'good' : state === 'Offline' ? 'warn' : 'bad';
                return (
                  <tr key={device.id} style={{ borderTop: '1px solid #f0f0f0' }}>
                    <td style={{ padding: '6px 8px', fontWeight: 600 }}>{device.name}</td>
                    <td style={{ padding: '6px 8px' }}>{device.pairedByUsername}</td>
                    <td style={{ padding: '6px 8px' }}>{formatWhen(device.lastSeenAt)}</td>
                    <td style={{ padding: '6px 8px' }}>
                      <span className={`badge ${tone}`} style={{ fontSize: '10px' }}>{state}</span>
                    </td>
                    <td style={{ padding: '6px 8px', textAlign: 'right' }}>
                      {!device.revokedAt && (
                        <button type="button" className="btn-secondary" disabled={busy} onClick={() => handleRevoke(device)}>
                          Revoke
                        </button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
};
