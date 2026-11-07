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
