// A Google Ads Transparency advertiser ID is "AR" followed by exactly 20 digits. Admins may paste the ID or a link
// to the advertiser page. This mirrors GoogleAdvertiserReference.TryParse on the server, so the form can explain a
// mistake before the request is sent.
const BARE_ID = /^AR[0-9]{20}$/i;
const ADVERTISER_LINK = /adstransparency\.google\.[a-z.]+\/advertiser\/(AR[0-9]{20})(?![0-9])/i;

export const GOOGLE_ADVERTISER_ID_ERROR =
  'Enter the Google advertiser ID (AR followed by 20 digits) or paste an Ads Transparency link, ' +
  'for example https://adstransparency.google.com/advertiser/AR01064852753525768193.';

export function parseGoogleAdvertiserId(input: string | null | undefined): string | null {
  const text = (input ?? '').trim();
  if (BARE_ID.test(text)) {
    return text.toUpperCase();
  }

  const link = ADVERTISER_LINK.exec(text);
  return link ? link[1].toUpperCase() : null;
}
