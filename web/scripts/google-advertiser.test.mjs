// Unit tests for the Google advertiser ID rules and the API error reader. Run with: node --test scripts/google-advertiser.test.mjs
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseGoogleAdvertiserId } from '../src/api/googleAdvertiser.ts';
import { apiErrorMessage } from '../src/api/errors.ts';

test('a bare AR id or an Ads Transparency link reduces to the advertiser id', () => {
  assert.equal(parseGoogleAdvertiserId('AR01064852753525768193'), 'AR01064852753525768193');
  assert.equal(parseGoogleAdvertiserId('  ar12066457051832778753  '), 'AR12066457051832778753');
  assert.equal(
    parseGoogleAdvertiserId('https://adstransparency.google.com/advertiser/AR01064852753525768193?region=EG'),
    'AR01064852753525768193',
  );
  assert.equal(
    parseGoogleAdvertiserId('adstransparency.google.com/advertiser/AR12066457051832778753'),
    'AR12066457051832778753',
  );
});

test('anything else is refused', () => {
  for (const input of [
    '',
    '   ',
    null,
    undefined,
    'cafelax.com',
    'AR123',
    'AR0106485275352576819',
    'AR010648527535257681930',
    'AR0106485275352576819١',
    'https://adstransparency.google.com/advertiser/AR123',
    'https://www.facebook.com/lazybeanonline',
  ]) {
    assert.equal(parseGoogleAdvertiserId(input), null, `expected refusal for ${JSON.stringify(input)}`);
  }
});

test('a validation error shows the server messages', () => {
  const body = {
    status: 400,
    title: 'Validation failed',
    errors: [{ PropertyName: 'ExternalId', ErrorMessage: 'Enter the Google advertiser ID (AR followed by 20 digits).' }],
  };
  assert.equal(apiErrorMessage(body, 'fallback'), 'Enter the Google advertiser ID (AR followed by 20 digits).');
});

test('a server error shows its title and the reference for support', () => {
  const body = { status: 500, title: 'An unexpected error occurred.', correlationId: 'abc123' };
  assert.equal(apiErrorMessage(body, 'fallback'), 'An unexpected error occurred. (reference abc123)');
});

test('an unreadable body falls back to the given text', () => {
  assert.equal(apiErrorMessage(undefined, 'fallback'), 'fallback');
  assert.equal(apiErrorMessage({}, 'fallback'), 'fallback');
  assert.equal(apiErrorMessage(new Error('Network down'), 'fallback'), 'Network down');
});
