// Ads page data: the per-competitor summary (what the app sent, what is stored, what the page shows), paged campaign
// cards with their creatives, and the reference store's own ads. Typed by hand to match the Ads endpoints.
import { apiErrorMessage } from './errors';

export interface AdVariant {
  id: string;
  competitorId: string;
  competitorKey: string;
  competitorName: string;
  identityId: string;
  identityName: string;
  campaignId: string | null;
  externalAdId: string;
  platform: string;
  title: string | null;
  body: string | null;
  linkUrl: string | null;
  snapshotUrl: string | null;
  deliveryStartDate: string | null;
  deliveryStopDate: string | null;
  firstSeenAt: string;
  lastSeenAt: string;
  status: string;
  creativeHash: string;
  productId: string | null;
  productTitle: string | null;
  productUrl: string | null;
  lazyBeanProductId: string | null;
  lazyBeanProductTitle: string | null;
  lazyBeanProductUrl: string | null;
  competitorPrice: number | null;
  lazyBeanPrice: number | null;
  pricePosition: string | null;
  priceDiffPercent: number | null;
  advertisedPrice: number | null;
  hasPriceDiscrepancy: boolean;
  previewUrl: string | null;
  creativeFormat: string | null;
  advertisedQuantity: number | null;
  unitPrice: number | null;
  linkMethod: string | null;
}

export interface AdCampaignCard {
  id: string;
  competitorId: string;
  competitorKey: string;
  competitorName: string;
  identityId: string;
  identityName: string;
  creativeHash: string;
  platform: string;
  body: string | null;
  firstSeenAt: string;
  lastSeenAt: string;
  status: string;
  adCount: number;
  activeAdCount: number;
  inactiveAdCount: number;
  ads: AdVariant[];
}

export interface CampaignPage {
  totalCount: number;
  page: number;
  pageSize: number;
  items: AdCampaignCard[];
}

export interface AdsSummaryRow {
  competitorId: string;
  competitorName: string;
  isReference: boolean;
  platform: string;
  identities: number;
  ingested: number;
  stored: number;
  displayed: number;
  lastSyncedAt: string | null;
  lastError: string | null;
}

export interface AdsSummary {
  rows: AdsSummaryRow[];
  generatedAt: string;
}

export interface AdsPage {
  totalCount: number;
  ads: AdVariant[];
  page: number;
  pageSize: number;
}

async function call<T>(url: string): Promise<T> {
  const res = await fetch(url, {
    credentials: 'include',
    headers: { Accept: 'application/json' },
  });
  if (!res.ok) {
    const data = await res.json().catch(() => null);
    throw new Error(apiErrorMessage(data, `HTTP ${res.status}`));
  }
  return res.json() as Promise<T>;
}

export function fetchAdsSummary(): Promise<AdsSummary> {
  return call<AdsSummary>('/api/ads/summary');
}

export interface CampaignQuery {
  competitorId?: string;
  platform?: string;
  status?: string;
  page: number;
  pageSize: number;
}

export function fetchCampaignPage(query: CampaignQuery): Promise<CampaignPage> {
  const params = new URLSearchParams();
  if (query.competitorId) params.set('competitorId', query.competitorId);
  if (query.platform) params.set('platform', query.platform);
  if (query.status) params.set('status', query.status);
  params.set('page', String(query.page));
  params.set('pageSize', String(query.pageSize));
  return call<CampaignPage>(`/api/ads/campaigns?${params.toString()}`);
}

// The reference store's own ads (Google and Meta), newest first.
export function fetchOurAds(referenceCompetitorId: string): Promise<AdsPage> {
  const params = new URLSearchParams({ competitorId: referenceCompetitorId, page: '1', pageSize: '100' });
  return call<AdsPage>(`/api/ads?${params.toString()}`);
}
