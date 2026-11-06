// Dashboard-side collector API (cookie-authenticated). Typed by hand: these endpoints are not part of
// the generated OpenAPI client yet.

export interface CollectorActiveJob {
  jobId: string;
  kind: string;
  runId: string | null;
  competitorSlug: string | null;
  deviceName: string | null;
  claimedAt: string | null;
  lastHeartbeatAt: string | null;
}

export interface CollectorStatus {
  connected: boolean;
  connectedDeviceCount: number;
  activeJob: CollectorActiveJob | null;
  activeGroupCompleted: number;
  activeGroupTotal: number;
  queuedJobCount: number;
}

export interface CollectorDevice {
  id: string;
  name: string;
  scopes: string[];
  pairedByUsername: string;
  createdAt: string;
  lastSeenAt: string | null;
  revokedAt: string | null;
  isConnected: boolean;
}

export interface CollectorAppInfo {
  available: boolean;
  downloadUrl: string | null;
  sizeBytes: number | null;
  sha256: string | null;
}

export interface CollectorJobSummary {
  jobId: string;
  kind: string;
  source: string;
  status: string;
  competitorSlug: string | null;
  summary: string | null;
  queuedAt: string;
  finishedAt: string | null;
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, {
    credentials: 'include',
    headers: { Accept: 'application/json' },
    ...init,
  });
  if (!res.ok) {
    const data = await res.json().catch(() => null);
    throw new Error(data?.error || data?.title || `HTTP ${res.status}`);
  }
  if (res.status === 204) {
    return undefined as T;
  }
  return res.json() as Promise<T>;
}

export function fetchCollectorStatus(): Promise<CollectorStatus> {
  return request<CollectorStatus>('/api/collector-admin/status');
}

export function fetchCollectorDevices(): Promise<CollectorDevice[]> {
  return request<CollectorDevice[]>('/api/collector-admin/devices');
}

export function revokeCollectorDevice(deviceId: string): Promise<void> {
  return request<void>(`/api/collector-admin/devices/${deviceId}`, { method: 'DELETE' });
}

export function fetchCollectorAppInfo(): Promise<CollectorAppInfo> {
  return request<CollectorAppInfo>('/api/collector-admin/app-info');
}

export function collectAdsNow(): Promise<void> {
  return request<void>('/api/collector-admin/collect-ads-now', { method: 'POST' });
}

export function fetchRunJobs(runId: string): Promise<CollectorJobSummary[]> {
  return request<CollectorJobSummary[]>(`/api/collector-admin/runs/${runId}/jobs`);
}
