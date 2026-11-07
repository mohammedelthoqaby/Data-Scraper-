import React, { useEffect, useRef, useState } from 'react';
import { api } from '../../api/client';
import { fetchAdGalleryDetail, thumbnailUrl, type AdGalleryCard, type AdGalleryDetail } from '../../api/adGallery';
import { formatDay, formatDayYear, formatMoney, gapChip } from './adsFormat';

interface AdSidePanelProps {
  card: AdGalleryCard;
  onClose: () => void;
  onOpenProduct?: (productId: string, title?: string) => void;
}

interface PriceRow {
  observedAt: string;
  price: number;
  competitorName: string | null;
  inStock: boolean | null;
}

const LINK_STYLE = { color: '#0b3d2e', fontWeight: 600 } as const;
const MUTED = { margin: 0, color: 'var(--muted)' } as const;

// The side panel of one card: the larger image or images, the full text, every variation of the card, the price history of
// the linked product (ours), and the links to the store.
export const AdSidePanel: React.FC<AdSidePanelProps> = ({ card, onClose, onOpenProduct }) => {
  const [detail, setDetail] = useState<AdGalleryDetail | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [imageIndex, setImageIndex] = useState(0);
  const [history, setHistory] = useState<PriceRow[] | null>(null);
  const [storeSite, setStoreSite] = useState<string | null>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const onCloseRef = useRef(onClose);

  const isMeta = card.platform === 'Meta';
  const chip = gapChip(card.ourPriceGapPercent);
  const quantity = card.advertisedQuantity ?? 1;

  useEffect(() => {
    onCloseRef.current = onClose;
  });

  // Focus on open, and Escape closes. The listener reads the latest onClose, so a parent re-render does not move focus.
  useEffect(() => {
    closeRef.current?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onCloseRef.current();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  useEffect(() => {
    let alive = true;
    fetchAdGalleryDetail(card.id)
      .then((data) => {
        if (alive) setDetail(data);
      })
      .catch((err: Error) => {
        if (alive) setDetailError(err.message);
      });
    return () => {
      alive = false;
    };
  }, [card.id]);

  useEffect(() => {
    if (!card.ourProductId) {
      setHistory([]);
      return;
    }

    let alive = true;
    fetch(`/api/history/prices?productId=${encodeURIComponent(card.ourProductId)}&pageSize=10`, {
      credentials: 'include',
      headers: { Accept: 'application/json' },
    })
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (alive) setHistory(((data?.items ?? []) as PriceRow[]).slice(0, 10));
      })
      .catch(() => {
        if (alive) setHistory([]);
      });
    return () => {
      alive = false;
    };
  }, [card.ourProductId]);

  useEffect(() => {
    let alive = true;
    api.GET('/api/sources').then((res) => {
      const store = (res.data ?? []).find((s) => s.id === card.competitorId);
      if (alive) setStoreSite(store?.baseUrl || null);
    });
    return () => {
      alive = false;
    };
  }, [card.competitorId]);

  const variations = detail?.variations ?? [];

  return (
    <>
      <div className="ad-panel-overlay" onClick={onClose} aria-hidden="true" />
      <aside className="ad-panel" role="dialog" aria-modal="true" aria-labelledby="ad-panel-title">
        <div className="ad-panel__head">
          <h2 id="ad-panel-title" dir="auto" style={{ flex: 1, fontSize: 16, margin: 0 }}>
            {card.isOurs ? 'Our ad' : card.competitorName}
          </h2>
          <button ref={closeRef} type="button" className="btn btn-secondary" onClick={onClose} aria-label="Close">
            ✕
          </button>
        </div>

        <div className="ad-panel__body">
          {card.thumbnailCount > 0 && (
            <div>
              <div className="ad-panel__image">
                <img src={thumbnailUrl(card, imageIndex)} alt="" />
                {card.thumbnailCount > 1 && (
                  <>
                    <button
                      type="button"
                      className="ad-panel__nav ad-panel__nav--prev"
                      aria-label="Previous image"
                      disabled={imageIndex === 0}
                      onClick={() => setImageIndex((i) => Math.max(0, i - 1))}
                    >
                      ‹
                    </button>
                    <button
                      type="button"
                      className="ad-panel__nav ad-panel__nav--next"
                      aria-label="Next image"
                      disabled={imageIndex >= card.thumbnailCount - 1}
                      onClick={() => setImageIndex((i) => Math.min(card.thumbnailCount - 1, i + 1))}
                    >
                      ›
                    </button>
                  </>
                )}
              </div>
              {card.thumbnailCount > 1 && (
                <div style={{ fontSize: 12, color: 'var(--muted)', marginTop: 4 }}>
                  Image {imageIndex + 1} of {card.thumbnailCount}
                </div>
              )}
            </div>
          )}

          <section className="ad-panel__section">
            <h3>Ad text</h3>
            {card.headline && <div className="ad-card__headline" dir="auto">{card.headline}</div>}
            {card.body && <p dir="auto" style={{ margin: '6px 0 0', lineHeight: 1.5 }}>{card.body}</p>}
            {!card.headline && !card.body && <p style={MUTED}>No text in this ad.</p>}
            <div style={{ fontSize: 12, color: 'var(--muted)', marginTop: 6 }}>
              {card.creativeFormat ?? 'Format not known'} · {formatDayYear(card.firstSeenAt)} to {formatDayYear(card.lastSeenAt)}
            </div>
          </section>

          {(card.advertisedPrice !== null || card.ourPrice !== null || card.competitorPrice !== null) && (
            <section className="ad-panel__section">
              <h3>Prices</h3>
              {card.advertisedPrice !== null && (
                <div>
                  Advertised {quantity > 1 ? `${quantity} for ` : ''}
                  {formatMoney(card.advertisedPrice)}
                  {quantity > 1 && card.unitPrice !== null ? ` (${formatMoney(card.unitPrice)} each)` : ''}
                </div>
              )}
              {card.competitorPrice !== null && <div>Store price {formatMoney(card.competitorPrice)}</div>}
              {card.ourPrice !== null && (
                <div>
                  Our price {formatMoney(card.ourPrice)}
                  {chip && <span className={`gap-chip ${chip.className}`}>{chip.text}</span>}
                </div>
              )}
            </section>
          )}

          {card.ourProductTitle && (
            <section className="ad-panel__section">
              <h3>Linked product</h3>
              {onOpenProduct && card.ourProductId ? (
                <button
                  type="button"
                  className="link-button"
                  onClick={() => onOpenProduct(card.ourProductId as string, card.ourProductTitle ?? undefined)}
                >
                  {card.ourProductTitle}
                </button>
              ) : (
                card.ourProductTitle
              )}
            </section>
          )}

          <section className="ad-panel__section">
            <h3>Price history</h3>
            {!card.ourProductId && <p style={MUTED}>No linked product, so no price history.</p>}
            {card.ourProductId && history === null && <p style={MUTED}>Loading…</p>}
            {card.ourProductId && history !== null && history.length === 0 && <p style={MUTED}>No prices recorded yet.</p>}
            {history && history.length > 0 && (
              <div className="ad-panel__history">
                {history.map((row, index) => (
                  <div className="ad-panel__history-row" key={`${row.observedAt}-${index}`}>
                    <span>{formatDay(row.observedAt)}{row.competitorName ? ` · ${row.competitorName}` : ''}</span>
                    <span>
                      {formatMoney(row.price)}
                      {row.inStock === false ? ' (out of stock)' : ''}
                    </span>
                  </div>
                ))}
              </div>
            )}
          </section>

          <section className="ad-panel__section">
            <h3>Variations of this ad ({variations.length || card.variationCount})</h3>
            {detailError && <div className="warning-banner">{detailError}</div>}
            {!detail && !detailError && <p style={MUTED}>Loading…</p>}
            <div style={{ display: 'grid', gap: 8 }}>
              {variations.map((variation) => (
                <div className="ad-panel__variation" key={variation.id}>
                  <div>
                    {formatDay(variation.firstSeenAt)} to {formatDay(variation.lastSeenAt)} · {variation.status}
                    {variation.creativeFormat ? ` · ${variation.creativeFormat}` : ''}
                  </div>
                  {variation.headline && <div dir="auto" style={{ fontWeight: 600 }}>{variation.headline}</div>}
                  {variation.body && <div dir="auto" style={{ color: '#344054' }}>{variation.body}</div>}
                  {variation.storeUrl && (
                    <a href={variation.storeUrl} target="_blank" rel="noopener noreferrer" style={LINK_STYLE}>
                      Open this ad ↗
                    </a>
                  )}
                </div>
              ))}
            </div>
          </section>

          <section className="ad-panel__section">
            <h3>Links</h3>
            <div style={{ display: 'grid', gap: 6 }}>
              {card.storeUrl && (
                <a href={card.storeUrl} target="_blank" rel="noopener noreferrer" style={LINK_STYLE}>
                  {isMeta ? 'View on Meta Ad Library' : 'View on Google Ads Transparency'} ↗
                </a>
              )}
              {storeSite && (
                <a href={storeSite} target="_blank" rel="noopener noreferrer" style={LINK_STYLE}>
                  Store website ↗
                </a>
              )}
            </div>
          </section>
        </div>
      </aside>
    </>
  );
};
