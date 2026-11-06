import { apiErrorMessage } from './errors';

// Our own ads (the reference competitor, Lazy Bean) compared with competitor ads, and adding Meta pages from a
// Facebook link. These endpoints are not in the generated schema, so they are called directly.

export type OwnAdKind = 'Both' | 'CompetitorOnly' | 'OursOnly';

export interface OwnAdComparisonRow {
  ourProductId: string;
  ourProductTitle: string;
  competitorProductId: string | null;
  competitorProductTitle: string | null;
  competitorId: string | null;
  competitorName: string | null;
  kind: OwnAdKind;
  ourAdCount: number;
  competitorAdCount: number;
  ourPrice: number | null;
  competitorPrice: number | null;
  ourAdUrl: string | null;
  competitorAdUrl: string | null;
  ourAdIsMoreExpensive: boolean;
  platform: string | null;
  gapPercent: number | null;
}

export interface AddIdentityFromUrlResult {
  status: 'pending' | 'exists' | 'resolving' | 'waiting_for_app';
  message: string;
  identityId: string | null;
  jobId: string | null;
  pageId: string | null;
}

async function call<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, {
    credentials: 'include',
    headers: { Accept: 'application/json', ...(init?.headers ?? {}) },
    ...init,
  });
  if (!res.ok) {
    const data = await res.json().catch(() => null);
    throw new Error(apiErrorMessage(data, `HTTP ${res.status}`));
  }
  return res.json() as Promise<T>;
}

export function fetchOwnAdComparison(): Promise<OwnAdComparisonRow[]> {
  return call<OwnAdComparisonRow[]>('/api/ads/our-comparison');
}

export function addIdentityFromUrl(competitorId: string, url: string): Promise<AddIdentityFromUrlResult> {
  return call<AddIdentityFromUrlResult>('/api/identities/from-url', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ competitorId, url }),
  });
}
