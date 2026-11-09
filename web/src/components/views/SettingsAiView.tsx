import React, { useCallback, useEffect, useState } from 'react';
import {
  aiApi,
  type AiKeyTestResultDto,
  type AiProviderId,
  type AiProviderStatusDto,
  type AiSettingsDto,
  type AiTriageStatsDto,
} from '../../api/ai';

const PROVIDER_ORDER: AiProviderId[] = ['groq', 'gemini'];

const KEY_LINKS: Record<AiProviderId, string> = {
  groq: 'https://console.groq.com/keys',
  gemini: 'https://aistudio.google.com/apikey',
};

const ENV_NAMES: Record<AiProviderId, string> = {
  groq: 'Ai__Groq__ApiKey',
  gemini: 'Ai__Gemini__ApiKey',
};

const TEST_LABELS: Record<AiKeyTestResultDto['status'], string> = {
  ok: 'OK',
  invalid_key: 'Invalid key',
  rate_limited: 'Rate-limited',
  error: 'Error',
  not_configured: 'No key',
};

const TEST_STYLES: Record<AiKeyTestResultDto['status'], { bg: string; border: string; color: string }> = {
  ok: { bg: '#e7f3ec', border: '#9bc9b1', color: '#1b5238' },
  invalid_key: { bg: '#fbeae8', border: '#f2a69b', color: '#8a1f11' },
  rate_limited: { bg: '#fff6e0', border: '#e8c977', color: '#855c0c' },
  error: { bg: '#fbeae8', border: '#f2a69b', color: '#8a1f11' },
  not_configured: { bg: '#f0f3f6', border: '#d0d7de', color: '#4b5563' },
};

const inputStyle: React.CSSProperties = {
  width: '100%',
  padding: '10px 12px',
  border: '1px solid var(--line)',
  borderRadius: '6px',
};

const labelStyle: React.CSSProperties = {
  display: 'block',
  fontWeight: 600,
  fontSize: '14px',
  marginBottom: '6px',
};

const hintStyle: React.CSSProperties = {
  fontSize: '12px',
  color: 'var(--muted)',
  marginTop: '4px',
};

function formatTime(value: string | null): string {
  return value ? new Date(value).toLocaleString() : '—';
}

interface Notice {
  type: 'success' | 'error';
  text: string;
}

interface ProviderCardProps {
  status: AiProviderStatusDto;
  keyInput: string;
  modelInput: string;
  busy: boolean;
  testResult: AiKeyTestResultDto | undefined;
  onKeyChange: (value: string) => void;
  onModelChange: (value: string) => void;
  onSave: () => void;
  onTest: () => void;
  onRemove: () => void;
}

const ProviderCard: React.FC<ProviderCardProps> = ({
  status,
  keyInput,
  modelInput,
  busy,
  testResult,
  onKeyChange,
  onModelChange,
  onSave,
  onTest,
  onRemove,
}) => {
  const fromEnvironment = status.keySource === 'environment';
  const listId = `ai-models-${status.provider}`;
  const usage = status.usage;

  return (
    <section className="panel" style={{ padding: '24px', marginBottom: '24px' }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', marginBottom: '16px' }}>
        <h2 style={{ fontSize: '18px', fontWeight: 600, margin: 0 }}>
          {status.label} {status.provider === 'groq' ? '(primary)' : '(fallback)'}
        </h2>
        <span className={`badge ${status.isConfigured ? 'good' : ''}`} style={{ fontSize: '12px' }}>
          {status.isConfigured ? (fromEnvironment ? 'Set by environment' : 'Key saved') : 'Not configured'}
        </span>
      </div>

      <div style={{ marginBottom: '20px' }}>
        <label htmlFor={`ai-key-${status.provider}`} style={labelStyle}>
          {status.label} API key
        </label>
        <div style={{ fontSize: '13px', color: 'var(--muted)', marginBottom: '8px' }}>
          Get a free key:{' '}
          <a href={KEY_LINKS[status.provider]} target="_blank" rel="noopener noreferrer">
            {KEY_LINKS[status.provider]}
          </a>
        </div>

        {status.maskedApiKey && (
          <div style={{ marginBottom: '8px', fontSize: '13px', color: 'var(--muted)', display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
            <span>Saved key:</span>
            <code style={{ background: '#f0f3f6', padding: '2px 8px', borderRadius: '4px', fontWeight: 600 }}>
              {status.maskedApiKey}
            </code>
            <span className="badge" style={{ fontSize: '11px' }}>
              {fromEnvironment ? 'Environment' : 'Encrypted'}
            </span>
          </div>
        )}

        {fromEnvironment && (
          <div style={{ ...hintStyle, marginBottom: '8px' }}>
            The environment variable <code>{ENV_NAMES[status.provider]}</code> is set and overrides any saved key.
          </div>
        )}

        <input
          id={`ai-key-${status.provider}`}
          type="password"
          autoComplete="off"
          spellCheck={false}
          value={keyInput}
          onChange={(e) => onKeyChange(e.target.value)}
          placeholder={status.maskedApiKey ? 'Enter a new key to replace the saved one' : 'Paste your API key'}
          style={{ ...inputStyle, fontFamily: 'monospace' }}
        />
        <div style={hintStyle}>Keys are encrypted at rest and are never shown again in full.</div>
      </div>

      <div style={{ marginBottom: '20px' }}>
        <label htmlFor={`ai-model-${status.provider}`} style={labelStyle}>
          Model
        </label>
        <input
          id={`ai-model-${status.provider}`}
          type="text"
          list={listId}
          value={modelInput}
          onChange={(e) => onModelChange(e.target.value)}
          placeholder={status.defaultModel}
          style={inputStyle}
        />
        <datalist id={listId}>
          {status.suggestedModels.map((m) => (
            <option key={m} value={m} />
          ))}
        </datalist>
        <div style={hintStyle}>
          Default: <code>{status.defaultModel}</code>. Any model id can be typed here.
        </div>
      </div>

      {testResult && (
        <div
          role="status"
          style={{
            padding: '10px 14px',
            borderRadius: '6px',
            marginBottom: '16px',
            fontSize: '13px',
            background: TEST_STYLES[testResult.status].bg,
            border: `1px solid ${TEST_STYLES[testResult.status].border}`,
            color: TEST_STYLES[testResult.status].color,
          }}
        >
          <strong>Key test: {TEST_LABELS[testResult.status]}.</strong> {testResult.message}
        </div>
      )}

      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', borderTop: '1px solid var(--line)', paddingTop: '16px', marginBottom: '20px', flexWrap: 'wrap', gap: '10px' }}>
        <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap' }}>
          <button type="button" className="btn-primary" disabled={busy} onClick={onSave} style={{ padding: '8px 18px', fontSize: '14px' }}>
            Save
          </button>
          <button type="button" className="btn-secondary" disabled={busy} onClick={onTest} style={{ padding: '8px 16px', fontSize: '14px' }}>
            Test key
          </button>
        </div>
        <button
          type="button"
          onClick={onRemove}
          disabled={busy || !status.maskedApiKey || fromEnvironment}
          style={{
            background: 'transparent',
            border: 'none',
            color: '#8a1f11',
            fontSize: '13px',
            cursor: 'pointer',
            textDecoration: 'underline',
            opacity: !status.maskedApiKey || fromEnvironment ? 0.4 : 1,
          }}
        >
          Remove
        </button>
      </div>

      <div style={{ background: '#f8fafc', border: '1px solid var(--line)', borderRadius: '6px', padding: '14px 16px' }}>
        <div style={{ fontSize: '12px', fontWeight: 700, color: 'var(--muted)', textTransform: 'uppercase', marginBottom: '10px' }}>
          Usage today (UTC {usage.usageDate})
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))', gap: '10px', fontSize: '13px' }}>
          <div>
            <div style={{ color: 'var(--muted)', fontSize: '11px' }}>Requests</div>
            <strong>
              {usage.requestsToday} / {usage.dailyRequestLimit}
            </strong>
          </div>
          <div>
            <div style={{ color: 'var(--muted)', fontSize: '11px' }}>Tokens</div>
            <strong>{usage.tokensToday.toLocaleString()}</strong>
          </div>
          <div>
            <div style={{ color: 'var(--muted)', fontSize: '11px' }}>Last 429</div>
            <strong>{formatTime(usage.last429At)}</strong>
          </div>
          <div>
            <div style={{ color: 'var(--muted)', fontSize: '11px' }}>Last error</div>
            <strong>{usage.lastError ? `${usage.lastError} (${formatTime(usage.lastErrorAt)})` : '—'}</strong>
          </div>
        </div>
        {status.lastTestedAt && (
          <div style={{ ...hintStyle, marginTop: '10px' }}>
            Last test: {formatTime(status.lastTestedAt)} {status.lastTestStatus ? `(${TEST_LABELS[status.lastTestStatus]})` : ''}
          </div>
        )}
      </div>
    </section>
  );
};

export const SettingsAiView: React.FC = () => {
  const [settings, setSettings] = useState<AiSettingsDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [notice, setNotice] = useState<Notice | null>(null);

  const [isEnabled, setIsEnabled] = useState(false);
  const [primaryProvider, setPrimaryProvider] = useState<AiProviderId>('groq');
  const [dailyCap, setDailyCap] = useState(1000);
  const [triageCap, setTriageCap] = useState(200);
  const [visionEnabled, setVisionEnabled] = useState(false);
  const [visionCap, setVisionCap] = useState(20);

  const [keyInputs, setKeyInputs] = useState<Record<AiProviderId, string>>({ groq: '', gemini: '' });
  const [modelInputs, setModelInputs] = useState<Record<AiProviderId, string>>({ groq: '', gemini: '' });
  const [testResults, setTestResults] = useState<Partial<Record<AiProviderId, AiKeyTestResultDto>>>({});
  const [busy, setBusy] = useState<string | null>(null);

  const [isTriaging, setIsTriaging] = useState(false);
  const [triageStats, setTriageStats] = useState<AiTriageStatsDto | null>(null);

  const applySettings = useCallback((data: AiSettingsDto, resetModelInputs: boolean) => {
    setSettings(data);
    setIsEnabled(data.isEnabled);
    setPrimaryProvider(data.primaryProvider);
    setDailyCap(data.dailyRequestCap);
    setTriageCap(data.triageRunCap);
    setVisionEnabled(data.creativeVisionEnabled ?? false);
    setVisionCap(data.creativeVisionDailyCap || 20);
    if (resetModelInputs) {
      setModelInputs({
        groq: data.providers.find((p) => p.provider === 'groq')?.modelName ?? '',
        gemini: data.providers.find((p) => p.provider === 'gemini')?.modelName ?? '',
      });
    }
  }, []);

  const load = useCallback(async () => {
    setIsLoading(true);
    try {
      applySettings(await aiApi.getSettings(), true);
    } catch (err: unknown) {
      setNotice({ type: 'error', text: err instanceof Error ? err.message : 'Failed to load AI settings' });
    } finally {
      setIsLoading(false);
    }
  }, [applySettings]);

  useEffect(() => {
    load();
  }, [load]);

  const runAction = async (key: string, action: () => Promise<void>) => {
    setBusy(key);
    setNotice(null);
    try {
      await action();
    } catch (err: unknown) {
      setNotice({ type: 'error', text: err instanceof Error ? err.message : 'Request failed' });
    } finally {
      setBusy(null);
    }
  };

  const handleSaveGlobal = () =>
    runAction('global', async () => {
      const updated = await aiApi.updateSettings({
        isEnabled,
        primaryProvider,
        dailyRequestCap: dailyCap,
        triageRunCap: triageCap,
        creativeVisionEnabled: visionEnabled,
        creativeVisionDailyCap: visionCap,
      });
      applySettings(updated, false);
      setNotice({ type: 'success', text: 'AI settings saved.' });
    });

  const handleSaveProvider = (provider: AiProviderId) =>
    runAction(`save:${provider}`, async () => {
      const apiKey = keyInputs[provider].trim();
      const modelName = modelInputs[provider].trim();
      const updated = await aiApi.saveProvider(provider, {
        apiKey: apiKey || undefined,
        modelName: modelName || undefined,
      });
      setKeyInputs((prev) => ({ ...prev, [provider]: '' }));
      setTestResults((prev) => ({ ...prev, [provider]: undefined }));
      applySettings(updated, true);
      setNotice({ type: 'success', text: `${provider === 'groq' ? 'Groq' : 'Gemini'} settings saved. The key is stored encrypted.` });
    });

  const handleTestProvider = (provider: AiProviderId) =>
    runAction(`test:${provider}`, async () => {
      const apiKey = keyInputs[provider].trim();
      const modelName = modelInputs[provider].trim();
      const result = await aiApi.testProviderKey(provider, {
        apiKey: apiKey || undefined,
        modelName: modelName || undefined,
      });
      setTestResults((prev) => ({ ...prev, [provider]: result }));
    });

  const handleRemoveKey = (provider: AiProviderId) => {
    if (!window.confirm(`Remove the saved ${provider === 'groq' ? 'Groq' : 'Gemini'} key?`)) {
      return;
    }
    runAction(`remove:${provider}`, async () => {
      const updated = await aiApi.removeProviderKey(provider);
      setKeyInputs((prev) => ({ ...prev, [provider]: '' }));
      setTestResults((prev) => ({ ...prev, [provider]: undefined }));
      applySettings(updated, false);
      setNotice({ type: 'success', text: 'Saved key removed.' });
    });
  };

  const handleRunTriage = () =>
    runAction('triage', async () => {
      setIsTriaging(true);
      setTriageStats(null);
      try {
        const stats = await aiApi.runTriage(triageCap);
        setTriageStats(stats);
        if (stats.deferred > 0) {
          setNotice({
            type: 'success',
            text: `${stats.stopReason ?? 'AI stopped'}, ${stats.deferred} items left for next run.`,
          });
        } else {
          setNotice({
            type: 'success',
            text: `AI triage completed: ${stats.totalEvaluated} evaluated (${stats.sameCount} same, ${stats.differentCount} different, ${stats.unsureCount} unsure).`,
          });
        }
        applySettings(await aiApi.getSettings(), false);
      } finally {
        setIsTriaging(false);
      }
    });

  if (isLoading) {
    return (
      <div className="panel" style={{ padding: '40px', textAlign: 'center', color: 'var(--muted)' }}>
        Loading AI configuration...
      </div>
    );
  }

  const providers = PROVIDER_ORDER.map((id) => settings?.providers.find((p) => p.provider === id)).filter(
    (p): p is AiProviderStatusDto => Boolean(p),
  );
  const anyConfigured = providers.some((p) => p.isConfigured);

  return (
    <div className="settings-ai-view" style={{ maxWidth: '800px', margin: '0 auto' }}>
      <div className="page-heading">
        <div>
          <div className="eyebrow">SETTINGS &amp; INTELLIGENCE</div>
          <h1 className="view-title">AI providers</h1>
          <p className="subtitle">
            Groq is the primary provider and Google Gemini is the fallback. Keys are encrypted on the server and only their
            masked form is shown. If both providers are unavailable, AI work pauses for the run and resumes on the next one.
          </p>
        </div>
      </div>

      {notice && (
        <div
          role="status"
          className={`warning-banner ${notice.type === 'success' ? 'good' : 'bad'}`}
          style={{
            marginBottom: '16px',
            background: notice.type === 'success' ? '#e7f3ec' : '#fbeae8',
            borderColor: notice.type === 'success' ? '#9bc9b1' : '#f2a69b',
            color: notice.type === 'success' ? '#1b5238' : '#8a1f11',
          }}
        >
          {notice.type === 'success' ? '✓ ' : '✕ '} {notice.text}
        </div>
      )}

      <section className="panel" style={{ padding: '24px', marginBottom: '24px' }}>
        <h2 style={{ fontSize: '18px', fontWeight: 600, marginBottom: '16px' }}>Routing and limits</h2>

        <div style={{ marginBottom: '20px', display: 'flex', alignItems: 'center', gap: '12px', flexWrap: 'wrap' }}>
          <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', fontWeight: 600 }}>
            <input
              type="checkbox"
              checked={isEnabled}
              onChange={(e) => setIsEnabled(e.target.checked)}
              style={{ width: '18px', height: '18px' }}
            />
            Enable AI assistance
          </label>
          <span style={{ fontSize: '13px', color: 'var(--muted)' }}>
            When disabled, the platform runs on deterministic rules only.
          </span>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '16px', marginBottom: '20px' }}>
          <div>
            <label htmlFor="ai-primary" style={labelStyle}>
              Primary provider
            </label>
            <select
              id="ai-primary"
              value={primaryProvider}
              onChange={(e) => setPrimaryProvider(e.target.value as AiProviderId)}
              style={inputStyle}
            >
              <option value="groq">Groq (default)</option>
              <option value="gemini">Google Gemini</option>
            </select>
            <div style={hintStyle}>The other provider is used when the primary hits a quota, rate limit, timeout or server error.</div>
          </div>
          <div>
            <label htmlFor="ai-daily-cap" style={labelStyle}>
              Daily request cap (all providers)
            </label>
            <input
              id="ai-daily-cap"
              type="number"
              min={1}
              max={100000}
              value={dailyCap}
              onChange={(e) => setDailyCap(Number.parseInt(e.target.value, 10) || 1)}
              style={inputStyle}
            />
          </div>
          <div>
            <label htmlFor="ai-triage-cap" style={labelStyle}>
              Triage cap per run
            </label>
            <input
              id="ai-triage-cap"
              type="number"
              min={1}
              max={1000}
              value={triageCap}
              onChange={(e) => setTriageCap(Number.parseInt(e.target.value, 10) || 1)}
              style={inputStyle}
            />
            <div style={hintStyle}>Review pairs evaluated per run (default 200).</div>
          </div>
        </div>

        {/* Creative image reading (AI-read): off by default */}
        <div style={{ marginBottom: '20px', padding: '12px 16px', border: '1px solid var(--line)', borderRadius: '6px' }}>
          <label style={{ display: 'flex', alignItems: 'flex-start', gap: '8px', cursor: 'pointer', fontWeight: 600 }}>
            <input
              type="checkbox"
              checked={visionEnabled}
              onChange={(e) => setVisionEnabled(e.target.checked)}
              style={{ marginTop: '3px' }}
            />
            <span>
              Read Google ad images with Gemini (AI-read)
              <span style={{ display: 'block', fontSize: '12px', fontWeight: 400, color: 'var(--muted)', marginTop: '2px' }}>
                Off by default. For Google image ads that no product matches, Gemini reads the product name and price from the
                stored preview. Results are marked AI-read. Each read counts toward the cap below.
              </span>
            </span>
          </label>
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginTop: '12px', flexWrap: 'wrap' }}>
            <span style={{ fontSize: '13px' }}>Image reads per day</span>
            <input
              type="number"
              min={0}
              max={500}
              value={visionCap}
              disabled={!visionEnabled}
              onChange={(e) => setVisionCap(Math.max(0, Math.min(500, Number.parseInt(e.target.value, 10) || 0)))}
              style={{ width: '100px', padding: '8px 10px', border: '1px solid var(--line)', borderRadius: '6px' }}
            />
            <span style={{ fontSize: '13px', color: 'var(--muted)' }}>
              Used today: <strong>{settings?.creativeVisionUsedToday ?? 0}</strong> / {visionCap}
            </span>
          </div>
        </div>

        <div style={{ display: 'flex', justifyContent: 'flex-end', borderTop: '1px solid var(--line)', paddingTop: '16px' }}>
          <button type="button" className="btn-primary" disabled={busy !== null} onClick={handleSaveGlobal} style={{ padding: '8px 18px', fontSize: '14px' }}>
            Save routing and limits
          </button>
        </div>
      </section>

      {providers.map((status) => (
        <ProviderCard
          key={status.provider}
          status={status}
          keyInput={keyInputs[status.provider]}
          modelInput={modelInputs[status.provider]}
          busy={busy !== null}
          testResult={testResults[status.provider]}
          onKeyChange={(value) => setKeyInputs((prev) => ({ ...prev, [status.provider]: value }))}
          onModelChange={(value) => setModelInputs((prev) => ({ ...prev, [status.provider]: value }))}
          onSave={() => handleSaveProvider(status.provider)}
          onTest={() => handleTestProvider(status.provider)}
          onRemove={() => handleRemoveKey(status.provider)}
        />
      ))}

      <section className="panel" style={{ padding: '24px' }}>
        <h2 style={{ fontSize: '18px', fontWeight: 600, marginBottom: '8px' }}>Review queue AI triage</h2>
        <p style={{ fontSize: '13px', color: 'var(--muted)', marginBottom: '16px' }}>
          Evaluates pending product-match candidates in batches. Results are cached by pair hash, so identical pairs are never
          sent twice. The run is capped at {triageCap} pairs.
        </p>

        <div style={{ display: 'flex', alignItems: 'center', gap: '16px', marginBottom: '16px', flexWrap: 'wrap' }}>
          <button
            type="button"
            className="btn-secondary"
            onClick={handleRunTriage}
            disabled={busy !== null || isTriaging || !isEnabled || !anyConfigured}
            style={{ padding: '8px 16px', fontSize: '14px', fontWeight: 600 }}
          >
            {isTriaging ? 'Triaging pending pairs...' : `Run AI triage on pending pairs (up to ${triageCap})`}
          </button>
          {!isEnabled && <span style={{ fontSize: '12px', color: 'var(--muted)' }}>Enable AI and save first.</span>}
          {isEnabled && !anyConfigured && (
            <span style={{ fontSize: '12px', color: 'var(--muted)' }}>Add a provider key first.</span>
          )}
        </div>

        {triageStats && (
          <div style={{ background: '#f8fafc', border: '1px solid var(--line)', borderRadius: '6px', padding: '16px' }}>
            <h3 style={{ fontSize: '14px', fontWeight: 600, marginBottom: '10px' }}>Last triage run</h3>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(130px, 1fr))', gap: '10px' }}>
              {[
                { label: 'Evaluated', value: triageStats.totalEvaluated, color: undefined },
                { label: 'Cache hits', value: triageStats.cacheHits, color: 'var(--action)' },
                { label: 'API calls', value: triageStats.apiCallsMade, color: undefined },
                { label: 'AI: same', value: triageStats.sameCount, color: '#1b5238' },
                { label: 'AI: different', value: triageStats.differentCount, color: '#8a1f11' },
                { label: 'AI: unsure', value: triageStats.unsureCount, color: '#855c0c' },
                { label: 'Left for next run', value: triageStats.deferred, color: undefined },
              ].map((tile) => (
                <div key={tile.label} style={{ background: '#fff', padding: '10px', borderRadius: '4px', border: '1px solid var(--line)' }}>
                  <div style={{ fontSize: '11px', color: 'var(--muted)' }}>{tile.label}</div>
                  <div style={{ fontSize: '18px', fontWeight: 700, color: tile.color }}>{tile.value}</div>
                </div>
              ))}
            </div>
            {triageStats.stopReason && (
              <div style={{ ...hintStyle, marginTop: '10px' }}>Stopped early: {triageStats.stopReason}.</div>
            )}
          </div>
        )}
      </section>
    </div>
  );
};
