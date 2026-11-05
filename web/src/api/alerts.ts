export interface AlertItem {
  id: string;
  type: string;
  severity: 'Urgent' | 'Opportunity' | 'Watch';
  title: string;
  reason: string;
  titleAr?: string | null;
  reasonAr?: string | null;
  suggestedAction: string;
  suggestedActionAr?: string | null;
  referenceProductId?: string | null;
  referenceProductTitle?: string | null;
  referenceProductUrl?: string | null;
  competitorProductId?: string | null;
  competitorProductTitle?: string | null;
  competitorProductUrl?: string | null;
  competitorName?: string | null;
  adLibraryUrl?: string | null;
  isBestseller: boolean;
  egpImpact: number;
  metricValuesJson: string;
  status: 'Active' | 'Done' | 'Snoozed' | 'Dismissed';
  snoozedUntil?: string | null;
  dismissNote?: string | null;
  resolvedAt?: string | null;
  createdAt: string;
  updatedAt?: string | null;
}

export interface AlertSummary {
  totalActive: number;
  urgentCount: number;
  opportunityCount: number;
  watchCount: number;
  countsByType: Record<string, number>;
}

export interface DataHealthInfo {
  allHealthy: boolean;
  failedRunsCount: number;
  staleCompetitors: string[];
  blockedAdCollectors: string[];
  metaTokenStatus: string;
  metaTokenExpiresAt?: string | null;
}

export interface AlertHistoryEntry {
  id: string;
  alertId: string;
  action: string;
  note?: string | null;
  performedBy: string;
  createdAt: string;
}

export interface AlertSettings {
  adMoreExpensiveGapPercent: number;
  priceDropPercent: number;
  lazyBeanAboveCheapestPercent: number;
  outOfStockCompetitorCount: number;
  marginRoomBelowCheapestPercent: number;
  uncarriedBrandCompetitorCount: number;
  bigCampaignDiscountCount: number;
  prolongedGapDays: number;
  materialChangePercentPoints: number;
}

export interface MatchAuditItem {
  matchId: string;
  competitorName: string;
  referenceTitle: string;
  referenceUrl: string;
  referenceBrand?: string | null;
  referencePrice: number;
  competitorTitle: string;
  competitorUrl: string;
  competitorBrand?: string | null;
  competitorPrice: number;
  score: number;
  method: string;
  approvedAt: string;
  reason?: string | null;
}

export async function fetchAlerts(params?: {
  severity?: string;
  status?: string;
  isBestseller?: boolean;
  search?: string;
}): Promise<AlertItem[]> {
  const query = new URLSearchParams();
  if (params?.severity && params.severity !== 'All') query.set('severity', params.severity);
  if (params?.status && params.status !== 'All') query.set('status', params.status);
  if (params?.isBestseller !== undefined) query.set('isBestseller', String(params.isBestseller));
  if (params?.search) query.set('search', params.search);

  const res = await fetch(`/api/alerts?${query.toString()}`);
  if (!res.ok) throw new Error(`Failed to fetch alerts: ${res.status}`);
  return res.json();
}

export async function fetchAlertSummary(): Promise<AlertSummary> {
  const res = await fetch('/api/alerts/summary');
  if (!res.ok) throw new Error(`Failed to fetch alert summary: ${res.status}`);
  return res.json();
}

export async function fetchDataHealth(): Promise<DataHealthInfo> {
  const res = await fetch('/api/alerts/health');
  if (!res.ok) throw new Error(`Failed to fetch data health: ${res.status}`);
  return res.json();
}

export async function syncAlerts(): Promise<AlertItem[]> {
  const res = await fetch('/api/alerts/sync', { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to sync alerts: ${res.status}`);
  return res.json();
}

export async function performAlertAction(id: string, action: 'done' | 'snooze' | 'dismiss' | 'reopen', note?: string): Promise<AlertItem> {
  const res = await fetch(`/api/alerts/${id}/action`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ action, note }),
  });
  if (!res.ok) throw new Error(`Failed to perform action on alert: ${res.status}`);
  return res.json();
}

export async function fetchAlertHistory(id: string): Promise<AlertHistoryEntry[]> {
  const res = await fetch(`/api/alerts/${id}/history`);
  if (!res.ok) throw new Error(`Failed to fetch alert history: ${res.status}`);
  return res.json();
}

export async function fetchAlertSettings(): Promise<AlertSettings> {
  const res = await fetch('/api/settings/alerts');
  if (!res.ok) throw new Error(`Failed to fetch alert settings: ${res.status}`);
  return res.json();
}

export async function updateAlertSettings(settings: AlertSettings): Promise<AlertSettings> {
  const res = await fetch('/api/settings/alerts', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(settings),
  });
  if (!res.ok) throw new Error(`Failed to update alert settings: ${res.status}`);
  return res.json();
}

export async function fetchAuditMatches(): Promise<MatchAuditItem[]> {
  const res = await fetch('/api/matches/audit');
  if (!res.ok) throw new Error(`Failed to fetch audit matches: ${res.status}`);
  return res.json();
}

export async function undoMatch(matchId: string): Promise<void> {
  const res = await fetch(`/api/matches/${matchId}/decision`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ decision: 'undo' }),
  });
  if (!res.ok) throw new Error(`Failed to undo match: ${res.status}`);
}
