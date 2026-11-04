import React, { useState } from 'react';
import { api, type AuthenticatedUser } from '../../api/client';

interface LoginModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (user: AuthenticatedUser) => void;
}

export const LoginModal: React.FC<LoginModalProps> = ({ isOpen, onClose, onSuccess }) => {
  const [username, setUsername] = useState('admin');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  if (!isOpen) return null;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setIsSubmitting(true);

    try {
      const { data, error: apiError } = await api.POST('/api/auth/login', {
        body: { username, password },
      });

      if (apiError || !data) {
        setError('Invalid username or password.');
      } else {
        onSuccess(data);
        onClose();
      }
    } catch (err: any) {
      setError(err?.message || 'Login failed. Please check backend connection.');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <div className="brand" style={{ padding: 0 }}>
            <div className="mark">
              <img src="/assets/logo.png" alt="Lazy Bean logo" width="26" height="26" style={{ objectFit: 'contain' }} />
            </div>
            <div className="brand-text">
              Unlock Operations
              <small>LAZY BEAN MARKET INTELLIGENCE</small>
            </div>
          </div>
        </div>

        <form onSubmit={handleSubmit}>
          <div className="modal-body">
            <div className="form-group">
              <label htmlFor="login-username">Username</label>
              <input
                id="login-username"
                type="text"
                className="form-control"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                required
                autoFocus
              />
            </div>

            <div className="form-group">
              <label htmlFor="login-password">Password</label>
              <input
                id="login-password"
                type="password"
                className="form-control"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Enter admin password"
                required
              />
              {error && <div className="error-text">{error}</div>}
            </div>
          </div>

          <div className="modal-footer">
            <button type="button" className="btn-secondary" onClick={onClose}>
              Cancel
            </button>
            <button type="submit" className="btn-primary" disabled={isSubmitting}>
              {isSubmitting ? 'Unlocking...' : 'Unlock operations'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
