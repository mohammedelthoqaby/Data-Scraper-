// Formatting for the ads gallery. Numbers are shown the way the price list shows them (Western digits, EGP).

export function formatMoney(value: number): string {
  return `${value.toLocaleString('en-US', { maximumFractionDigits: 2 })} EGP`;
}

export function formatDay(iso: string | null | undefined): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' });
}

export function formatDayYear(iso: string | null | undefined): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
}

// Our price against what the ad advertises. Negative: we are cheaper, so the chip is green.
export function gapChip(gap: number | null): { className: string; text: string } | null {
  if (gap === null || Number.isNaN(gap)) return null;
  if (Math.abs(gap) < 0.5) return { className: 'gap-chip--same', text: 'Same price' };
  if (gap < 0) return { className: 'gap-chip--cheaper', text: `We are ${Math.abs(gap).toFixed(1)}% cheaper` };
  return { className: 'gap-chip--pricier', text: `We are ${gap.toFixed(1)}% more` };
}

// The same comparison in full for the ad window: the EGP difference per unit and the percentage. The baseline is the
// advertised unit price, or the store price when the ad shows none. Null when there is nothing to compare.
export function gapBadge(
  ourPrice: number | null,
  baseline: number | null,
  gapPercent: number | null
): { className: string; text: string } | null {
  if (ourPrice === null || baseline === null || gapPercent === null || Number.isNaN(gapPercent)) return null;
  if (Math.abs(gapPercent) < 0.5) return { className: 'gap-chip--same', text: 'Same price' };
  const egp = Math.abs(ourPrice - baseline).toLocaleString('en-US', { maximumFractionDigits: 2 });
  const pct = Math.abs(gapPercent).toFixed(1);
  if (gapPercent < 0) {
    return { className: 'gap-chip--cheaper', text: `We are ${egp} EGP cheaper (${pct}%)` };
  }
  return { className: 'gap-chip--pricier', text: `We are ${egp} EGP more expensive (${pct}%)` };
}

// Why an unlinked ad has no product of ours, in the words the ad window shows. The codes are AdLinkReasons on the server.
const LINK_REASON_TEXT: Record<string, string> = {
  NoProductInfo: 'The ad has no text and no landing link, so there is nothing to match a product by.',
  LinksToHomepage: 'Its landing link goes to the store homepage, not to a product page.',
  NoMatch: 'It names a product, but none of ours matches it.',
  Pending: 'The reason has not been worked out yet.',
};

export function linkReasonText(code: string | null | undefined): string {
  return (code && LINK_REASON_TEXT[code]) || 'No product of ours matches this ad.';
}

// Days from the start to the end (now, while the ad runs), counting the first day. At least one.
export function daysRunning(startIso: string, endIso: string | null, now: number = Date.now()): number {
  const start = new Date(startIso).getTime();
  const end = endIso ? new Date(endIso).getTime() : now;
  if (Number.isNaN(start) || Number.isNaN(end)) return 0;
  return Math.max(1, Math.ceil((end - start) / 86_400_000));
}
