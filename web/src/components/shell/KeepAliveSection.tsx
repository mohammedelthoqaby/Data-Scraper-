import React, { createContext, useContext, useEffect, useLayoutEffect, useRef } from 'react';

// What the app asks of a section: save its inner scroll positions before it is hidden, or reset them to the top.
export interface SectionHandle {
  save: () => void;
  reset: () => void;
}

const SectionActiveContext = createContext(true);

// True while the section is on screen. Timers and polls inside a section check it, pause while it is hidden and
// resume when it is shown again.
export function useSectionActive(): boolean {
  return useContext(SectionActiveContext);
}

interface SavedPosition {
  node: HTMLElement;
  top: number;
  left: number;
}

interface KeepAliveSectionProps {
  id: string;
  active: boolean;
  registry: Map<string, SectionHandle>;
  children: React.ReactNode;
}

// A section that has been opened stays mounted, with its DOM, its React state and its data. When another section is shown,
// this one is display:none. display:none would reset the scroll positions of the scroll containers inside it, so those
// are saved (save) right before the switch and put back (in the layout effect below) when the section is shown again.
// display:none also takes no room, so a hidden section never adds height to the page.
export const KeepAliveSection: React.FC<KeepAliveSectionProps> = ({ id, active, registry, children }) => {
  const ref = useRef<HTMLDivElement>(null);
  const saved = useRef<SavedPosition[] | null>(null);

  useEffect(() => {
    const handle: SectionHandle = {
      save: () => {
        const el = ref.current;
        if (!el) return;
        const list: SavedPosition[] = [];
        el.querySelectorAll<HTMLElement>('*').forEach((node) => {
          if (node.scrollTop !== 0 || node.scrollLeft !== 0) {
            list.push({ node, top: node.scrollTop, left: node.scrollLeft });
          }
        });
        saved.current = list;
      },
      reset: () => {
        const el = ref.current;
        if (!el) return;
        el.querySelectorAll<HTMLElement>('*').forEach((node) => {
          if (node.scrollTop !== 0) node.scrollTop = 0;
          if (node.scrollLeft !== 0) node.scrollLeft = 0;
        });
      },
    };
    registry.set(id, handle);
    return () => {
      registry.delete(id);
    };
  }, [id, registry]);

  // Shown again: the layout is back, so the saved inner positions are set before the browser paints.
  useLayoutEffect(() => {
    if (!active) return;
    const list = saved.current;
    saved.current = null;
    list?.forEach(({ node, top, left }) => {
      node.scrollTop = top;
      node.scrollLeft = left;
    });
  }, [active]);

  return (
    <SectionActiveContext.Provider value={active}>
      <div
        ref={ref}
        className={`keep-section ${active ? 'is-active' : 'is-hidden'}`}
        data-section={id}
        aria-hidden={!active}
      >
        {children}
      </div>
    </SectionActiveContext.Provider>
  );
};
