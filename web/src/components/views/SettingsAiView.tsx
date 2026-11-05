import React, { useState, useEffect } from 'react';
import { aiApi, type AiSettingsDto, type AiTriageStatsDto } from '../../api/ai';

export const SettingsAiView: React.FC = () => {
  const [settings, setSettings] = useState<AiSettingsDto | null>(null);
  const [apiKeyInput, setApiKeyInput] = useState('');
  const [modelName, setModelName] = useState('gemini-3.5-flash');
  const [isEnabled, setIsEnabled] = useState(false);
  const [dailyCap, setDailyCap] = useState(500);

  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [isTesting, setIsTesting] = useState(false);
  const [isTriaging, setIsTriaging] = useState(false);

  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);
  const [statusMessage, setStatusMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [triageStats, setTriageStats] = useState<AiTriageStatsDto | null>(null);

  const fetchSettings = async () => {
    setIsLoading(true);
    try {
      const data = await aiApi.getSettings();
      setSettings(data);
      setIsEnabled(data.isEnabled);
      setModelName(data.modelName || 'gemini-3.8-flash');
      setDailyCap(data.dailyRequestCap || 500);
      if (data.lastTestSuccess !== null && data.lastTestMessage) {
        setTestResult({ success: data.lastTestSuccess, message: data.lastTestMessage });
      }
    } catch (err: any) {
      console.error('Failed to load AI settings:', err);
      setStatusMessage({ type: 'error', text: err?.message || 'Failed to load AI settings' });
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    fetchSettings();
  }, []);

  const handleTestConnection = async () => {
    setIsTesting(true);
    setTestResult(null);
    try {
      const keyToTest = apiKeyInput.trim() ? apiKeyInput.trim() : undefined;
      const res = await aiApi.testConnection(keyToTest, modelName);
      setTestResult(res);
    } catch (err: any) {
      setTestResult({ success: false, message: err?.message || 'Test request failed' });
    } finally {
      setIsTesting(false);
    }
  };

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSaving(true);
    setStatusMessage(null);
    try {
      const updated = await aiApi.updateSettings({
        isEnabled,
        modelName: modelName.trim(),
        dailyRequestCap: dailyCap,
        apiKey: apiKeyInput.trim() ? apiKeyInput.trim() : undefined,
      });

      setSettings(updated);
      setApiKeyInput(''); // Clear plain text input once saved
      setStatusMessage({ type: 'success', text: 'AI configuration successfully saved and encrypted.' });
    } catch (err: any) {
      setStatusMessage({ type: 'error', text: err?.message || 'Failed to save AI configuration' });
    } finally {
      setIsSaving(false);
    }
  };

  const handleDeleteKey = async () => {
    if (!window.confirm('Are you sure you want to remove the stored Gemini API key and disable AI features?')) {
      return;
    }
    setIsSaving(true);
    setStatusMessage(null);
    try {
      await aiApi.deleteKey();
      await fetchSettings();
      setApiKeyInput('');
      setStatusMessage({ type: 'success', text: 'API key successfully removed and AI features disabled.' });
    } catch (err: any) {
      setStatusMessage({ type: 'error', text: err?.message || 'Failed to remove API key' });
    } finally {
      setIsSaving(false);
    }
  };

  const handleRunTriage = async () => {
    setIsTriaging(true);
    setStatusMessage(null);
    setTriageStats(null);
    try {
      const stats = await aiApi.runTriage(100);
      setTriageStats(stats);
      setStatusMessage({
        type: 'success',
        text: `AI Triage completed: ${stats.totalEvaluated} items evaluated (${stats.sameCount} same, ${stats.differentCount} different, ${stats.unsureCount} unsure).`,
      });
      // Refresh settings to update requestsUsedToday
      const updated = await aiApi.getSettings();
      setSettings(updated);
    } catch (err: any) {
      setStatusMessage({ type: 'error', text: err?.message || 'Failed to run AI triage' });
    } finally {
      setIsTriaging(false);
    }
  };

  if (isLoading) {
    return (
      <div className="panel" style={{ padding: '40px', textAlign: 'center', color: 'var(--muted)' }}>
        Loading AI configuration...
      </div>
    );
  }

  return (
    <div className="settings-ai-view" style={{ maxWidth: '800px', margin: '0 auto' }}>
      <div className="page-heading">
        <div>
          <div className="eyebrow">SETTINGS &amp; INTELLIGENCE</div>
          <h1 className="view-title">Google AI Studio (Gemini)</h1>
          <p className="subtitle">
            Configure Gemini AI assistance for candidate review triage and automated attention ranking.
            API keys are encrypted with ASP.NET Data Protection and stored securely in the database.
          </p>
        </div>
      </div>

      {statusMessage && (
        <div
          className={`warning-banner ${statusMessage.type === 'success' ? 'good' : 'bad'}`}
          style={{
            marginBottom: '16px',
            background: statusMessage.type === 'success' ? '#e7f3ec' : '#fbeae8',
            borderColor: statusMessage.type === 'success' ? '#9bc9b1' : '#f2a69b',
            color: statusMessage.type === 'success' ? '#1b5238' : '#8a1f11',
          }}
        >
          {statusMessage.type === 'success' ? '✓ ' : '✕ '} {statusMessage.text}
        </div>
      )}

      {/* Main Settings Form */}
      <form onSubmit={handleSave} className="panel" style={{ padding: '24px', marginBottom: '24px' }}>
        <h2 style={{ fontSize: '18px', fontWeight: 600, marginBottom: '16px' }}>API &amp; Model Credentials</h2>

        {/* Enable / Disable Switch */}
        <div style={{ marginBottom: '20px', display: 'flex', alignItems: 'center', gap: '12px' }}>
          <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', fontWeight: 600 }}>
            <input
              type="checkbox"
              checked={isEnabled}
              onChange={(e) => setIsEnabled(e.target.checked)}
              style={{ width: '18px', height: '18px' }}
            />
            Enable AI Assistance
          </label>
          <span style={{ fontSize: '13px', color: 'var(--muted)' }}>
            (When disabled, the platform operates purely on deterministic rules)
          </span>
        </div>

        {/* API Key */}
        <div style={{ marginBottom: '20px' }}>
          <label style={{ display: 'block', fontWeight: 600, fontSize: '14px', marginBottom: '6px' }}>
            Google AI Studio API Key
          </label>
          {settings?.maskedApiKey && (
            <div style={{ marginBottom: '8px', fontSize: '13px', color: 'var(--muted)', display: 'flex', alignItems: 'center', gap: '8px' }}>
              <span>Currently saved:</span>
              <code style={{ background: '#f0f3f6', padding: '2px 8px', borderRadius: '4px', fontWeight: 600 }}>
                {settings.maskedApiKey}
              </code>
              <span className="badge good" style={{ fontSize: '11px' }}>Encrypted</span>
            </div>
          )}
          <input
            type="password"
            placeholder={settings?.maskedApiKey ? 'Enter new key to replace existing...' : 'Enter Gemini API key (AIzaSy...)'}
            value={apiKeyInput}
            onChange={(e) => setApiKeyInput(e.target.value)}
            style={{
              width: '100%',
              padding: '10px 12px',
              border: '1px solid var(--line)',
              borderRadius: '6px',
              fontFamily: 'monospace',
            }}
          />
          <div style={{ fontSize: '12px', color: 'var(--muted)', marginTop: '4px' }}>
            Keys are never logged or returned unmasked to the frontend.
          </div>
        </div>

        {/* Model Name */}
        <div style={{ marginBottom: '20px' }}>
          <label style={{ display: 'block', fontWeight: 600, fontSize: '14px', marginBottom: '6px' }}>
            Gemini Model Name
          </label>
          <input
            type="text"
            value={modelName}
            onChange={(e) => setModelName(e.target.value)}
            placeholder="gemini-3.5-flash"
            style={{
              width: '100%',
              padding: '10px 12px',
              border: '1px solid var(--line)',
              borderRadius: '6px',
            }}
          />
          <div style={{ fontSize: '12px', color: 'var(--muted)', marginTop: '4px' }}>
            Default: <code>gemini-3.5-flash</code> (Fast, low-latency Flash model).
          </div>
        </div>

        {/* Daily Request Cap */}
        <div style={{ marginBottom: '24px' }}>
          <label style={{ display: 'block', fontWeight: 600, fontSize: '14px', marginBottom: '6px' }}>
            Daily Request Cap (Free-tier safety)
          </label>
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
            <input
              type="number"
              min="10"
              max="10000"
              value={dailyCap}
              onChange={(e) => setDailyCap(parseInt(e.target.value, 10) || 500)}
              style={{
                width: '140px',
                padding: '10px 12px',
                border: '1px solid var(--line)',
                borderRadius: '6px',
              }}
            />
            <span style={{ fontSize: '13px', color: 'var(--muted)' }}>
              Usage today: <strong>{settings?.requestsUsedToday ?? 0}</strong> / {dailyCap} requests
            </span>
          </div>
        </div>

        {/* Last Call Telemetry & Status */}
        {(settings?.lastCallStatus || settings?.lastCallAt) && (
          <div style={{ marginBottom: '20px', padding: '12px 16px', background: '#f8fafc', border: '1px solid var(--line)', borderRadius: '6px' }}>
            <div style={{ fontSize: '12px', fontWeight: 700, color: 'var(--muted)', textTransform: 'uppercase', marginBottom: '6px' }}>
              Last Gemini API Call
            </div>
            <div style={{ display: 'flex', gap: '16px', alignItems: 'center', fontSize: '13px' }}>
              <div>
                Status:{' '}
                <span className={`badge ${settings.lastCallStatus === '200 OK' ? 'good' : 'bad'}`} style={{ fontWeight: 600 }}>
                  {settings.lastCallStatus || 'Unknown'}
                </span>
              </div>
              {settings.lastCallAt && (
                <div style={{ color: 'var(--muted)' }}>
                  Time: {new Date(settings.lastCallAt).toLocaleString()}
                </div>
              )}
            </div>
          </div>
        )}

        {/* Test Connection Result Alert */}
        {testResult && (
          <div
            style={{
              padding: '12px 16px',
              borderRadius: '6px',
              marginBottom: '20px',
              fontSize: '13px',
              background: testResult.success ? '#e7f3ec' : '#fbeae8',
              border: `1px solid ${testResult.success ? '#9bc9b1' : '#f2a69b'}`,
              color: testResult.success ? '#1b5238' : '#8a1f11',
            }}
          >
            <strong>{testResult.success ? '✓ Connection Verified:' : '✕ Connection Failed:'}</strong> {testResult.message}
          </div>
        )}

        {/* Form Action Buttons */}
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', borderTop: '1px solid var(--line)', paddingTop: '16px' }}>
          <div style={{ display: 'flex', gap: '10px' }}>
            <button
              type="submit"
              className="btn-primary"
              disabled={isSaving}
              style={{ padding: '8px 18px', fontSize: '14px' }}
            >
              {isSaving ? 'Saving...' : 'Save Configuration'}
            </button>
            <button
              type="button"
              className="btn-secondary"
              disabled={isTesting}
              onClick={handleTestConnection}
              style={{ padding: '8px 16px', fontSize: '14px' }}
            >
              {isTesting ? 'Testing...' : 'Test Key'}
            </button>
          </div>

          {settings?.maskedApiKey && (
            <button
              type="button"
              onClick={handleDeleteKey}
              disabled={isSaving}
              style={{
                background: 'transparent',
                border: 'none',
                color: '#8a1f11',
                fontSize: '13px',
                cursor: 'pointer',
                textDecoration: 'underline',
              }}
            >
              Remove Stored Key
            </button>
          )}
        </div>
      </form>

      {/* Manual AI Triage Orchestration Panel */}
      <div className="panel" style={{ padding: '24px' }}>
        <h2 style={{ fontSize: '18px', fontWeight: 600, marginBottom: '8px' }}>Review Queue AI Triage</h2>
        <p style={{ fontSize: '13px', color: 'var(--muted)', marginBottom: '16px' }}>
          Evaluate pending product match candidates in batches of 20 with Gemini. Results are cached by pair hash
          so identical pairs are never resent.
        </p>

        <div style={{ display: 'flex', alignItems: 'center', gap: '16px', marginBottom: '16px' }}>
          <button
            type="button"
            className="btn-secondary"
            onClick={handleRunTriage}
            disabled={isTriaging || !isEnabled || (!apiKeyInput && !settings?.maskedApiKey)}
            style={{ padding: '8px 16px', fontSize: '14px', fontWeight: 600 }}
          >
            {isTriaging ? 'Triaging 100 Pending Pairs...' : '⚡ Run AI Triage on Pending Pairs (Up to 100)'}
          </button>
          {!isEnabled && (
            <span style={{ fontSize: '12px', color: 'var(--muted)' }}>
              (Enable AI and save configuration first)
            </span>
          )}
        </div>

        {triageStats && (
          <div style={{ background: '#f8fafc', border: '1px solid var(--line)', borderRadius: '6px', padding: '16px' }}>
            <h3 style={{ fontSize: '14px', fontWeight: 600, marginBottom: '10px' }}>Last Triage Results</h3>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(130px, 1fr))', gap: '10px' }}>
              <div style={{ background: '#fff', padding: '10px', borderRadius: '4px', border: '1px solid var(--line)' }}>
                <div style={{ fontSize: '11px', color: 'var(--muted)' }}>Evaluated</div>
                <div style={{ fontSize: '18px', fontWeight: 700 }}>{triageStats.totalEvaluated}</div>
              </div>
              <div style={{ background: '#fff', padding: '10px', borderRadius: '4px', border: '1px solid var(--line)' }}>
                <div style={{ fontSize: '11px', color: 'var(--muted)' }}>Cache Hits</div>
                <div style={{ fontSize: '18px', fontWeight: 700, color: 'var(--action)' }}>{triageStats.cacheHits}</div>
              </div>
              <div style={{ background: '#fff', padding: '10px', borderRadius: '4px', border: '1px solid var(--line)' }}>
                <div style={{ fontSize: '11px', color: 'var(--muted)' }}>API Calls</div>
                <div style={{ fontSize: '18px', fontWeight: 700 }}>{triageStats.apiCallsMade}</div>
              </div>
              <div style={{ background: '#fff', padding: '10px', borderRadius: '4px', border: '1px solid var(--line)' }}>
                <div style={{ fontSize: '11px', color: 'var(--muted)' }}>AI: Same</div>
                <div style={{ fontSize: '18px', fontWeight: 700, color: '#1b5238' }}>{triageStats.sameCount}</div>
              </div>
              <div style={{ background: '#fff', padding: '10px', borderRadius: '4px', border: '1px solid var(--line)' }}>
                <div style={{ fontSize: '11px', color: 'var(--muted)' }}>AI: Different</div>
                <div style={{ fontSize: '18px', fontWeight: 700, color: '#8a1f11' }}>{triageStats.differentCount}</div>
              </div>
              <div style={{ background: '#fff', padding: '10px', borderRadius: '4px', border: '1px solid var(--line)' }}>
                <div style={{ fontSize: '11px', color: 'var(--muted)' }}>AI: Unsure</div>
                <div style={{ fontSize: '18px', fontWeight: 700, color: '#855c0c' }}>{triageStats.unsureCount}</div>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
