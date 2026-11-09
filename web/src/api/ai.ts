export type AiProviderId = 'groq' | 'gemini';

export type AiKeyTestStatus = 'ok' | 'invalid_key' | 'rate_limited' | 'error' | 'not_configured';

export interface AiProviderUsageDto {
  usageDate: string;
  requestsToday: number;
  tokensToday: number;
  dailyRequestLimit: number;
  lastError: string | null;
  lastErrorAt: string | null;
  last429At: string | null;
}

export interface AiProviderStatusDto {
  provider: AiProviderId;
  label: string;
  isConfigured: boolean;
  keySource: 'none' | 'database' | 'environment';
  /** Masked only, for example gsk_••••1234. The server never returns a key. */
  maskedApiKey: string | null;
  modelName: string;
  defaultModel: string;
  suggestedModels: string[];
  lastTestedAt: string | null;
  lastTestStatus: AiKeyTestStatus | null;
  lastTestMessage: string | null;
  usage: AiProviderUsageDto;
}

export interface AiSettingsDto {
  isEnabled: boolean;
  primaryProvider: AiProviderId;
  dailyRequestCap: number;
  triageRunCap: number;
  providers: AiProviderStatusDto[];
}

export interface UpdateAiSettingsRequest {
  isEnabled?: boolean;
  primaryProvider?: AiProviderId;
  dailyRequestCap?: number;
  triageRunCap?: number;
}

export interface SaveAiProviderRequest {
  apiKey?: string;
  modelName?: string;
}

export interface AiKeyTestResultDto {
  status: AiKeyTestStatus;
  message: string;
}

export interface AiTriageStatsDto {
  totalEvaluated: number;
  cacheHits: number;
  apiCallsMade: number;
  sameCount: number;
  differentCount: number;
  unsureCount: number;
  deferred: number;
  stopReason: string | null;
}

export interface RankedAttentionItem {
  id: string;
  type: string;
  headline: string;
  headlineAr: string;
  explanation: string;
  explanationAr: string;
  priority: 'high' | 'medium' | 'low';
  rank: number;
  productUrl: string | null;
  brand: string | null;
  competitorName: string;
  lazyBeanPrice: number;
  competitorPrice: number;
  isAiGenerated: boolean;
}

async function readError(res: Response, fallback: string): Promise<Error> {
  const body = await res.json().catch(() => ({} as { error?: string; message?: string }));
  return new Error(body.error || body.message || `${fallback} (${res.status})`);
}

export const aiApi = {
  async getSettings(): Promise<AiSettingsDto> {
    const res = await fetch('/api/settings/ai', {
      method: 'GET',
      headers: { Accept: 'application/json' },
      credentials: 'include',
    });
    if (!res.ok) throw await readError(res, 'Failed to fetch AI settings');
    return res.json();
  },

  async updateSettings(payload: UpdateAiSettingsRequest): Promise<AiSettingsDto> {
    const res = await fetch('/api/settings/ai', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(payload),
      credentials: 'include',
    });
    if (!res.ok) throw await readError(res, 'Failed to save AI settings');
    return res.json();
  },

  async saveProvider(provider: AiProviderId, payload: SaveAiProviderRequest): Promise<AiSettingsDto> {
    const res = await fetch(`/api/settings/ai/providers/${provider}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(payload),
      credentials: 'include',
    });
    if (!res.ok) throw await readError(res, 'Failed to save provider settings');
    return res.json();
  },

  async removeProviderKey(provider: AiProviderId): Promise<AiSettingsDto> {
    const res = await fetch(`/api/settings/ai/providers/${provider}/key`, {
      method: 'DELETE',
      headers: { Accept: 'application/json' },
      credentials: 'include',
    });
    if (!res.ok) throw await readError(res, 'Failed to remove the saved key');
    return res.json();
  },

  /** Pass apiKey to test a key before saving it; omit it to test the saved or environment key. */
  async testProviderKey(provider: AiProviderId, payload: SaveAiProviderRequest = {}): Promise<AiKeyTestResultDto> {
    const res = await fetch(`/api/settings/ai/providers/${provider}/test`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(payload),
      credentials: 'include',
    });
    if (!res.ok) {
      const err = await readError(res, 'Key test failed');
      return { status: 'error', message: err.message };
    }
    return res.json();
  },

  async runTriage(limit?: number): Promise<AiTriageStatsDto> {
    const query = limit ? `?limit=${limit}` : '';
    const res = await fetch(`/api/settings/ai/triage${query}`, {
      method: 'POST',
      credentials: 'include',
    });
    if (!res.ok) throw await readError(res, 'Failed to trigger AI triage');
    return res.json();
  },

  async getAttentionItems(): Promise<RankedAttentionItem[]> {
    const res = await fetch('/api/overview/attention', {
      method: 'GET',
      headers: { Accept: 'application/json' },
      credentials: 'include',
    });
    if (!res.ok) throw await readError(res, 'Failed to fetch attention items');
    return res.json();
  },
};
