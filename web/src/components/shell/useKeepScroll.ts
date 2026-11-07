import { useLayoutEffect, useRef, type RefObject } from 'react';

// Keeps the page at the same scroll position when a tab, pill or filter changes the content.
// Call the returned function in the click handler, before the state changes. After the next render the container gets
// enough height for that position, so the browser does not clamp the scroll when the new content is shorter, and the
// position is set again. Only the content changes; nothing scrolls to the top and nothing is focused programmatically.
export function useKeepScroll(containerRef: RefObject<HTMLElement | null>, changeKey: string): () => void {
  const savedY = useRef<number | null>(null);

  useLayoutEffect(() => {
    if (savedY.current === null) return;
    const y = savedY.current;
    savedY.current = null;
    const el = containerRef.current;
    if (el) {
      const top = el.getBoundingClientRect().top + window.scrollY;
      const needed = Math.ceil(y + window.innerHeight - top);
      el.style.minHeight = needed > 0 ? `${needed}px` : '';
    }
    window.scrollTo(0, y);
    // Runs only when the key changes; the container and the saved position are read from refs.
  }, [changeKey]); // eslint-disable-line react-hooks/exhaustive-deps

  return () => {
    savedY.current = window.scrollY;
    // A click that changes nothing must not leave a saved position for a later change.
    window.setTimeout(() => {
      savedY.current = null;
    }, 0);
  };
}
