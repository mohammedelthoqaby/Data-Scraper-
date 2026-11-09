import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  changePassword,
  fetchAccount,
  MIN_PASSWORD_LENGTH,
  passwordStrength,
  type AccountInfo,
  type PasswordStrengthLevel,
} from '../../api/password';

const STRENGTH_COLOR: Record<PasswordStrengthLevel, string> = {
  'too-short': 'var(--muted)',
  weak: '#b42318',
  fair: '#b54708',
  good: '#067647',
  strong: '#067647',
};

const fieldStyle: React.CSSProperties = {
  width: '100%',
  padding: '10px 12px',
  border: '1px solid var(--line)',
  borderRadius: '6px',
};

const labelStyle: React.CSSProperties = { display: 'block', fontWeight: 600, fontSize: '14px', marginBottom: '6px' };

function formatChangedAt(value: string | null): string {
  if (!value) return 'Not changed on the website yet. This is still the password set during setup.';
  return `Password changed on ${new Date(value).toLocaleString()}`;
}

export const SettingsAccountView: React.FC = () => {
  const [account, setAccount] = useState<AccountInfo | null>(null);
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [revokeDevices, setRevokeDevices] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setAccount(await fetchAccount());
    } catch (err: any) {
      setError(err?.message || 'Could not load your account.');
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const strength = useMemo(() => passwordStrength(newPassword), [newPassword]);
  const confirmMismatch = confirmPassword.length > 0 && confirmPassword !== newPassword;
  const canSubmit =
    !busy && currentPassword.length > 0 && strength.level !== 'too-short' && newPassword === confirmPassword;

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!canSubmit) return;

    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const result = await changePassword({ currentPassword, newPassword, revokeDevices });
      setAccount((previous) => (previous ? { ...previous, passwordChangedAt: result.passwordChangedAt } : previous));
      // The passwords are not kept in the page once they are sent.
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      setRevokeDevices(false);

      const apps = result.devicesRevoked;
      setNotice(
        apps > 0
          ? `Password changed. Other browser sessions were signed out, and ${apps} desktop app${apps === 1 ? ' was' : 's were'} signed out. ${apps === 1 ? 'It needs' : 'They need'} to be paired again.`
          : 'Password changed. Other browser sessions were signed out. This browser stays signed in, and the desktop apps keep working.'
      );
      await load();
    } catch (err: any) {
      setError(err?.message || 'Could not change the password.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="settings-ai-view">
      <div className="page-heading">
        <div>
          <div className="eyebrow">Settings</div>
          <h2 className="view-title">Account & password</h2>
          <p className="subtitle">
            {account ? `Signed in as ${account.username}.` : 'Loading your account…'}
          </p>
        </div>
      </div>

      {error && <div className="warning-banner" style={{ marginBottom: '12px' }}>{error}</div>}
      {notice && <div className="success-banner" style={{ marginBottom: '12px' }}>{notice}</div>}

      <section className="panel" style={{ marginBottom: '16px' }}>
        <h3 style={{ marginTop: 0 }}>Change password</h3>
        <p style={{ marginTop: 0, color: 'var(--muted)', fontSize: '13px' }}>
          {account ? formatChangedAt(account.passwordChangedAt) : ''}
        </p>

        <form onSubmit={handleSubmit} style={{ maxWidth: '520px' }}>
          <div style={{ marginBottom: '16px' }}>
            <label htmlFor="account-current-password" style={labelStyle}>Current password</label>
            <input
              id="account-current-password"
              type="password"
              autoComplete="current-password"
              value={currentPassword}
              onChange={(e) => setCurrentPassword(e.target.value)}
              style={fieldStyle}
            />
          </div>

          <div style={{ marginBottom: '8px' }}>
            <label htmlFor="account-new-password" style={labelStyle}>New password</label>
            <input
              id="account-new-password"
              type="password"
              autoComplete="new-password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              style={fieldStyle}
            />
          </div>
          <div
            aria-live="polite"
            style={{ fontSize: '13px', color: STRENGTH_COLOR[strength.level], marginBottom: '16px' }}
          >
            {newPassword.length > 0 ? strength.label : `At least ${MIN_PASSWORD_LENGTH} characters.`}
          </div>

          <div style={{ marginBottom: '16px' }}>
            <label htmlFor="account-confirm-password" style={labelStyle}>Confirm new password</label>
            <input
              id="account-confirm-password"
              type="password"
              autoComplete="new-password"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              style={fieldStyle}
            />
            {confirmMismatch && (
              <div style={{ fontSize: '13px', color: '#b42318', marginTop: '4px' }}>The two passwords do not match.</div>
            )}
          </div>

          <div style={{ marginBottom: '16px', fontSize: '13px', color: 'var(--muted)' }}>
            Changing the password signs out every other browser session. This browser stays signed in.
          </div>

          <div style={{ marginBottom: '20px', padding: '12px', border: '1px solid var(--line)', borderRadius: '6px' }}>
            <label style={{ display: 'flex', alignItems: 'flex-start', gap: '8px', cursor: 'pointer' }}>
              <input
                type="checkbox"
                checked={revokeDevices}
                onChange={(e) => setRevokeDevices(e.target.checked)}
                style={{ marginTop: '3px' }}
              />
              <span>
                <strong>Also sign out all paired desktop apps</strong>
                <span style={{ display: 'block', fontSize: '13px', color: 'var(--muted)', marginTop: '2px' }}>
                  Desktop apps keep working after a password change, because they use their own device tokens. Tick this
                  to sign them out too. Each app then asks to be paired again.
                </span>
              </span>
            </label>
          </div>

          <button type="submit" className="btn-primary" disabled={!canSubmit}>
            {busy ? 'Changing…' : 'Change password'}
          </button>
        </form>
      </section>
    </div>
  );
};
