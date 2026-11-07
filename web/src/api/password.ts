// Dashboard account and password change (cookie-authenticated). Typed by hand, like collector.ts.
import { apiErrorMessage } from './errors.ts';

export const MIN_PASSWORD_LENGTH = 10;

export interface AccountInfo {
  id: string;
  username: string;
  // Null while the password is still the one set during setup.
  passwordChangedAt: string | null;
}

export interface PasswordChangeResult {
  passwordChangedAt: string;
  devicesRevoked: number;
}

export interface PasswordChangeInput {
  currentPassword: string;
  newPassword: string;
  revokeDevices: boolean;
}

export type PasswordStrengthLevel = 'too-short' | 'weak' | 'fair' | 'good' | 'strong';

export interface PasswordStrength {
  level: PasswordStrengthLevel;
  label: string;
}

// A hint for the person choosing the password. The server enforces the length and that the new password differs.
export function passwordStrength(password: string): PasswordStrength {
  if (password.length < MIN_PASSWORD_LENGTH) {
    return {
      level: 'too-short',
      label: `At least ${MIN_PASSWORD_LENGTH} characters (${password.length} so far).`,
    };
  }

  let variety = 0;
  if (/[a-z]/.test(password)) variety++;
  if (/[A-Z]/.test(password)) variety++;
  if (/\d/.test(password)) variety++;
  if (/[^A-Za-z0-9]/.test(password)) variety++;
  if (password.length >= 14) variety++;

  if (variety <= 2) return { level: 'weak', label: 'Weak: add upper-case letters, digits or symbols, or make it longer.' };
  if (variety === 3) return { level: 'fair', label: 'Fair: a longer mix of character types would be stronger.' };
  if (variety === 4) return { level: 'good', label: 'Good.' };
  return { level: 'strong', label: 'Strong.' };
}

export async function fetchAccount(): Promise<AccountInfo> {
  return request<AccountInfo>('/api/auth/account', { method: 'GET' }, 'Could not load your account.');
}

export async function changePassword(input: PasswordChangeInput): Promise<PasswordChangeResult> {
  return request<PasswordChangeResult>(
    '/api/auth/change-password',
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(input),
    },
    'Could not change the password.',
  );
}

async function request<T>(url: string, init: RequestInit, fallback: string): Promise<T> {
  const response = await fetch(url, {
    credentials: 'include',
    ...init,
    headers: { Accept: 'application/json', ...(init.headers as Record<string, string> | undefined) },
  });

  if (response.status === 401) {
    throw new Error('Your session has ended. Sign in again to continue.');
  }

  if (response.status === 429) {
    throw new Error('Too many attempts. Wait 15 minutes, then try again.');
  }

  const body: unknown = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(apiErrorMessage(body, fallback));
  }

  return body as T;
}
