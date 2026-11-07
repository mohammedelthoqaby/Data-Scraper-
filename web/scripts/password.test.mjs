// Unit tests for the password change hint and the error text the Account page shows. Run with: node --test scripts/password.test.mjs
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { MIN_PASSWORD_LENGTH, passwordStrength } from '../src/api/password.ts';
import { apiErrorMessage } from '../src/api/errors.ts';

test('the minimum is 10 characters, and a shorter password is too short', () => {
  assert.equal(MIN_PASSWORD_LENGTH, 10);
  assert.equal(passwordStrength('abcdefghi').level, 'too-short');
  assert.match(passwordStrength('abcdefghi').label, /9 so far/);
});

test('a 10-character password with one kind of character is weak', () => {
  assert.equal(passwordStrength('abcdefghij').level, 'weak');
});

test('more kinds of characters and more length raise the hint', () => {
  assert.equal(passwordStrength('Abcdefghij1').level, 'fair');
  assert.equal(passwordStrength('Abcdefghij1!').level, 'good');
  assert.equal(passwordStrength('Abcdefghijklmn1!').level, 'strong');
});

test("the server's messages grouped by field are shown as written", () => {
  const body = {
    title: 'Validation failed',
    errors: { CurrentPassword: ['Current password is not correct.'] },
  };
  assert.equal(apiErrorMessage(body, 'fallback'), 'Current password is not correct.');
  assert.equal(
    apiErrorMessage({ errors: { NewPassword: ['At least 10.', 'Must differ.'] } }, 'fallback'),
    'At least 10. Must differ.',
  );
});

test('the older list form of validation errors still reads', () => {
  const body = { errors: [{ errorMessage: 'Username is required.' }] };
  assert.equal(apiErrorMessage(body, 'fallback'), 'Username is required.');
});
