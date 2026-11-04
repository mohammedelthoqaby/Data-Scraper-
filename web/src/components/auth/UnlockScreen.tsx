import React, { useState } from 'react';
import { api, type AuthenticatedUser } from '../../api/client';

interface UnlockScreenProps {
  onSuccess: (user: AuthenticatedUser) => void;
}

export const UnlockScreen: React.FC<UnlockScreenProps> = ({ onSuccess }) => {
  const [username, setUsername] = useState('admin');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setIsSubmitting(true);

    try {
      const { data, error: apiError, response } = await api.POST('/api/auth/login', {
        body: { username, password },
      });

      if (apiError || response?.status !== 200 || !data) {
        setError('Invalid username or password.');
      } else {
        onSuccess(data);
      }
    } catch (err: any) {
      setError(err?.message || 'Login failed. Please check backend connection.');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="unlock-screen-wrapper" data-testid="unlock-screen">
      <div className="unlock-card">
        <div className="modal-header">
          <div className="brand" style={{ padding: 0 }}>
            <div className="mark">
              <img src="/assets/logo.png" alt="Lazy Bean logo" width="32" height="32" style={{ objectFit: 'contain' }} />
            </div>
            <div className="brand-text">
              Unlock Operations
              <small>LAZY BEAN MARKET INTELLIGENCE</small>
            </div>
          </div>
        </div>

        <form onSubmit={handleSubmit}>
          <div className="modal-body">
            <p style={{ color: 'var(--muted)', fontSize: '13px', margin: '0 0 16px 0', lineHeight: 1.5 }}>
              Enter your operator credentials to access competitor price intelligence, review candidate matches, and view market movements.
            </p>

            <div className="form-group">
              <label htmlFor="unlock-username">Username</label>
              <input
                id="unlock-username"
                type="text"
                className="form-control"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                required
              />
            </div>

            <div className="form-group">
              <label htmlFor="unlock-password">Password</label>
              <input
                id="unlock-password"
                type="password"
                className="form-control"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Enter operator password"
                required
                autoFocus
              />
              {error && <div className="error-text" style={{ color: '#9c3030', fontSize: '12px', marginTop: '6px' }}>{error}</div>}
            </div>
          </div>

          <div className="modal-footer" style={{ borderTop: '1px solid var(--line)', background: '#fafbfc' }}>
            <button
              type="submit"
              className="btn-primary"
              style={{ width: '100%', justifyContent: 'center' }}
              disabled={isSubmitting}
            >
              {isSubmitting ? 'Unlocking...' : 'Unlock operations'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
