// The ads gallery (cookie-authenticated). Typed by hand, like collector.ts.

export interface AdGalleryCard {
  id: string;
  competitorId: string;
  competitorName: string;
  isOurs: boolean;
  platform: 'Meta' | 'Google' | string;
  status: 'Active' | 'Inactive' | string;
  headline: string | null;
  body: string | null;
  creativeFormat: 'Text' | 'Image' | 'Video' | string | null;
  thumbnailCount: number;
  variationCount: number;
  firstSeenAt: string;
  lastSeenAt: string;
  storeUrl: string | null;
  advertisedPrice: number | null;
  advertisedQuantity: number | null;
  unitPrice: number | null;
  linkedProductId: string | null;
  linkedProductTitle: string | null;
  ourProductId: string | null;
  ourProductTitle: string | null;
  ourPrice: number | null;
  competitorPrice: number | null;
  // Our price against what the ad advertises: negative when we are cheaper.
  ourPriceGapPercent: number | null;
  thumbnailSha256: string | null;
}

export interface AdGalleryResponse {
  totalCount: number;
  totalAds: number;
  page: number;
  pageSize: number;
  items: AdGalleryCard[];
}

export interface AdGalleryVariation {
  id: string;
  headline: string | null;
  body: string | null;
  linkUrl: string | null;
  storeUrl: string | null;
  status: string;
  creativeFormat: string | null;
  thumbnailCount: number;
  firstSeenAt: string;
  lastSeenAt: string;
}

export interface AdGalleryDetail {
  card: AdGalleryCard;
  variations: AdGalleryVariation[];
}

export interface GalleryQuery {
  competitorId?: string;
  platform?: string;
  status?: string;
  hasPrice?: boolean;
  linkedToOurs?: boolean;
  search?: string;
  sortByStore?: boolean;
  page?: number;
  pageSize?: number;
}

async function request<T>(url: string): Promise<T> {
  const res = await fetch(url, { credentials: 'include', headers: { Accept: 'application/json' } });
  if (res.status === 401) {
    throw new Error('Your session has ended. Sign in again to continue.');
  }

  const body: any = await res.json().catch(() => null);
  if (!res.ok) {
    throw new Error(body?.title || body?.error || `The ads could not be loaded (HTTP ${res.status}).`);
  }

  return body as T;
}

export async function fetchAdGallery(query: GalleryQuery): Promise<AdGalleryResponse> {
  const params = new URLSearchParams();
  if (query.competitorId) params.set('competitorId', query.competitorId);
  if (query.platform) params.set('platform', query.platform);
  if (query.status) params.set('status', query.status);
  if (query.hasPrice) params.set('hasPrice', 'true');
  if (query.linkedToOurs) params.set('linkedToOurs', 'true');
  if (query.search?.trim()) params.set('search', query.search.trim());
  if (query.sortByStore) params.set('sortByStore', 'true');
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 24));
  return request<AdGalleryResponse>(`/api/ads/gallery?${params.toString()}`);
}

export async function fetchAdGalleryDetail(id: string): Promise<AdGalleryDetail> {
  return request<AdGalleryDetail>(`/api/ads/gallery/${encodeURIComponent(id)}`);
}

// The image of a card. Its fingerprint is in the address, so a changed image is fetched again.
export function thumbnailUrl(card: { id: string; thumbnailSha256: string | null }, position = 0): string {
  const version = card.thumbnailSha256 ? `&v=${card.thumbnailSha256.slice(0, 12)}` : '';
  return `/api/ads/${encodeURIComponent(card.id)}/thumbnail?position=${position}${version}`;
}
