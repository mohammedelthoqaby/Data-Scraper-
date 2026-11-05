export interface AiSettingsDto {
  isEnabled: boolean;
  modelName: string;
  maskedApiKey: string | null;
  dailyRequestCap: number;
  requestsUsedToday: number;
  lastTestedAt: string | null;
  lastTestSuccess: boolean | null;
  lastTestMessage: string | null;
  lastCallAt?: string | null;
  lastCallStatus?: string | null;
}

export interface UpdateAiSettingsRequest {
  isEnabled?: boolean;
  apiKey?: string;
  modelName?: string;
  dailyRequestCap?: number;
}

export interface AiTestResultDto {
  success: boolean;
  message: string;
}

export interface AiTriageStatsDto {
  totalEvaluated: number;
  cacheHits: number;
  apiCallsMade: number;
  sameCount: number;
  differentCount: number;
  unsureCount: number;
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

export const aiApi = {
  async getSettings(): Promise<AiSettingsDto> {
    const res = await fetch('/api/settings/ai', {
      method: 'GET',
      headers: { Accept: 'application/json' },
      credentials: 'include',
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      throw new Error(err.message || `Failed to fetch AI settings (${res.status})`);
    }
    return res.json();
  },

  async updateSettings(payload: UpdateAiSettingsRequest): Promise<AiSettingsDto> {
    const res = await fetch('/api/settings/ai', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(payload),
      credentials: 'include',
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      throw new Error(err.message || `Failed to save AI settings (${res.status})`);
    }
    return res.json();
  },

  async deleteKey(): Promise<void> {
    const res = await fetch('/api/settings/ai', {
      method: 'DELETE',
      credentials: 'include',
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      throw new Error(err.message || `Failed to delete AI key (${res.status})`);
    }
  },

  async testConnection(apiKey?: string, modelName?: string): Promise<AiTestResultDto> {
    const res = await fetch('/api/settings/ai/test', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ apiKey, modelName }),
      credentials: 'include',
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      return { success: false, message: err.message || `Server error (${res.status})` };
    }
    return res.json();
  },

  async runTriage(limit: number = 100): Promise<AiTriageStatsDto> {
    const res = await fetch(`/api/settings/ai/triage?limit=${limit}`, {
      method: 'POST',
      credentials: 'include',
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      throw new Error(err.message || `Failed to trigger AI triage (${res.status})`);
    }
    return res.json();
  },

  async getAttentionItems(): Promise<RankedAttentionItem[]> {
    const res = await fetch('/api/overview/attention', {
      method: 'GET',
      headers: { Accept: 'application/json' },
      credentials: 'include',
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      throw new Error(err.message || `Failed to fetch attention items (${res.status})`);
    }
    return res.json();
  },
};
