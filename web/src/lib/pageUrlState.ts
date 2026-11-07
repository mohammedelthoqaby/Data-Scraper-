// Page state (tabs, filters, search, sort) lives in the URL hash, after the view name: #/ads-campaigns?tab=ours&q=espresso.
//
// Changing that state uses history.replaceState, never pushState, so the current history entry is updated in place.
// Back and Forward then move between sections the user visited, not between filter states. replaceState does not fire
// hashchange, so the app's section navigation is not affected. On a fresh load or a return to the page, the state is
// read back from the hash.

// The view named in the hash, for example "ads-campaigns".
export function hashView(): string {
  return window.location.hash.replace(/^#\/?/, '').split('?')[0];
}

// The query part of the hash, as parameters.
export function readHashParams(): URLSearchParams {
  const hash = window.location.hash;
  const index = hash.indexOf('?');
  return new URLSearchParams(index >= 0 ? hash.slice(index + 1) : '');
}

// Replaces the current history entry's view and parameters. Only the keys given are changed: a key set to an empty
// value or undefined is removed, and every other parameter is kept. Several components can share one page this way.
export function writeHashParams(view: string, params: Record<string, string | undefined>): string {
  const search = readHashParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === '') {
      search.delete(key);
    } else {
      search.set(key, value);
    }
  }

  const query = search.toString();
  const hash = `#/${view}${query ? `?${query}` : ''}`;
  if (window.location.hash !== hash) {
    window.history.replaceState(window.history.state, '', `${window.location.pathname}${window.location.search}${hash}`);
  }

  return hash;
}

// Scroll position per URL, kept for the browser session. Returning to the same state restores it.
const SCROLL_PREFIX = 'lazybean.scroll:';

export function saveScrollFor(hash: string): void {
  try {
    window.sessionStorage.setItem(SCROLL_PREFIX + hash, String(Math.round(window.scrollY)));
  } catch {
    // Storage can be unavailable (private windows, blocked storage). The page still works without it.
  }
}

export function savedScrollFor(hash: string): number | null {
  try {
    const value = window.sessionStorage.getItem(SCROLL_PREFIX + hash);
    return value === null ? null : Number(value);
  } catch {
    return null;
  }
}
