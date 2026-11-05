import createClient from 'openapi-fetch';
import type { paths, components } from './schema';

export const api = createClient<paths>({
  baseUrl: '',
  credentials: 'include',
  headers: {
    'Accept': 'application/json',
  },
});

type UnauthorizedListener = () => void;
const unauthorizedListeners = new Set<UnauthorizedListener>();

export function onUnauthorized(listener: UnauthorizedListener): () => void {
  unauthorizedListeners.add(listener);
  return () => {
    unauthorizedListeners.delete(listener);
  };
}

export function notifyUnauthorized(): void {
  unauthorizedListeners.forEach((fn) => fn());
}

api.use({
  onResponse({ response, schemaPath }) {
    if (response.status === 401 && schemaPath !== '/api/auth/me' && schemaPath !== '/api/auth/login') {
      notifyUnauthorized();
    }
  },
});

export type Schemas = components['schemas'];
export type PriceMatrixResponse = Schemas['PriceMatrixResponseDto'];
export type PriceMatrixRow = Schemas['PriceMatrixRowDto'];
export type CompetitorPriceCell = Schemas['CompetitorPriceCellDto'];
export type CompetitorOffersResponse = Schemas['CompetitorOffersResponseDto'];
export type CompetitorOffer = Schemas['CompetitorOfferDto'];
export type ReferenceOffersResponse = Schemas['ReferenceOffersResponseDto'];
export type ReferenceOffer = Schemas['ReferenceOfferDto'];
export type AuthenticatedUser = Schemas['AuthenticatedUserDto'];
export type CollectionRun = Schemas['CollectionRunSummaryDto'];
export type Competitor = Schemas['CompetitorDto'];
export type AdDto = Schemas['AdDto'];
export type AdCampaignDto = Schemas['AdCampaignDto'];
export type AdvertiserIdentity = Schemas['AdvertiserIdentityDto'];
export type ReviewQueueResponse = Schemas['ReviewQueueResponseDto'];
export type ReviewMatchItem = Schemas['ReviewMatchItemDto'];
export type RunDto = Schemas['RunDto'];
export type PriceHistoryResponse = Schemas['PriceHistoryResponseDto'];
export type PriceHistoryPoint = Schemas['PriceHistoryPointDto'];

export interface CompetitorProgress {
  competitorId: string;
  competitorKey: string;
  competitorName: string;
  isReference: boolean;
  status: string;
  currentPage: number;
  totalPages: number | null;
  productsSaved: number;
  lastUpdated: string;
  lastMessage: string | null;
  collectionHost?: string;
}

export interface PhaseProgress {
  name: string;
  status: string;
  message: string | null;
  durationMs: number | null;
}

export interface RunProgress {
  runId: string;
  status: string;
  overallPercent: number;
  isStalled: boolean;
  lastLogMessage: string | null;
  lastProgressAt: string;
  startedAt?: string | null;
  competitors: CompetitorProgress[];
  phases: PhaseProgress[];
}

export async function fetchRunProgress(runId: string): Promise<RunProgress | null> {
  const res = await fetch(`/api/runs/${runId}/progress`, { credentials: 'include' });
  if (!res.ok) return null;
  return res.json();
}

export async function fetchActiveRun(): Promise<RunProgress | null> {
  const res = await fetch(`/api/runs/active`, { credentials: 'include' });
  if (res.status === 204 || !res.ok) return null;
  return res.json();
}

export async function fetchActiveRunProgress(): Promise<RunProgress | null> {
  return fetchActiveRun();
}

export async function triggerMatchingNow(): Promise<any> {
  const res = await fetch('/api/matches/run', {
    method: 'POST',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
  });
  if (!res.ok) throw new Error(`Matching failed: ${res.statusText}`);
  return res.json();
}

export async function redispatchGithubCollector(
  runId: string,
  slug: string
): Promise<{ success: boolean; message?: string; actionsUrl?: string }> {
  const res = await fetch(`/api/runs/${runId}/competitors/${slug}/redispatch`, {
    method: 'POST',
    credentials: 'include',
  });
  if (!res.ok) {
    const data = await res.json().catch(() => null);
    throw new Error(data?.message || data?.error || `HTTP ${res.status}: Failed to redispatch.`);
  }
  return res.json();
}
