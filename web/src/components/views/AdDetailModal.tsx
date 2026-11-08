import React, { useEffect, useId, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import {
  fetchAdGalleryDetail,
  thumbnailUrl,
  type AdGalleryCard,
  type AdGalleryDetail,
} from '../../api/adGallery';
import { daysRunning, formatDay, formatDayYear, formatMoney, gapBadge, linkReasonText } from './adsFormat';

export interface AdDetailModalProps {
  card: AdGalleryCard;
  // Where the ad sits in the filtered list. Null when it is not in the list (a link to a filter that does not show it).
  position: { index: number; total: number } | null;
  canPrevious: boolean;
  canNext: boolean;
  onPrevious: () => void;
  onNext: () => void;
  onClose: () => void;
  // Opens Price comparisons with the product's title as its search.
  onOpenPriceComparisons?: (title: string) => void;
}

interface PriceRow {
  observedAt: string;
  price: number;
  competitorName: string | null;
  inStock: boolean | null;
}

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

// Keeps Tab and Shift+Tab inside the dialog: past the last item it wraps to the first, and before the first it wraps to the last.
function trapFocus(event: KeyboardEvent, root: HTMLElement) {
  const items = Array.from(root.querySelectorAll<HTMLElement>(FOCUSABLE));
  if (items.length === 0) {
    event.preventDefault();
    return;
  }

  const first = items[0];
  const last = items[items.length - 1];
  const active = document.activeElement;
  const inside = active !== null && root.contains(active);
  if (event.shiftKey) {
    if (!inside || active === first || active === root) {
      event.preventDefault();
      last.focus();
    }
  } else if (!inside || active === last) {
    event.preventDefault();
    first.focus();
  }
}

// The ad window: a dialog over the page with the ad's images and every detail the gallery has for it. It is shown on top of
// the page (portal), locks the page scroll while open, and does not add a history entry. Previous, Next and the arrow keys move
// through the cards of the filtered list; Escape, the close button and a click on the backdrop close it.
export const AdDetailModal: React.FC<AdDetailModalProps> = ({
  card,
  position,
  canPrevious,
  canNext,
  onPrevious,
  onNext,
  onClose,
  onOpenPriceComparisons,
}) => {
  const titleId = useId();
  const dialogRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const bodyRef = useRef<HTMLDivElement>(null);
  const pressedBackdrop = useRef(false);

  // The window's keys read the latest props, so the key listener is bound once for this window.
  const latest = useRef({ canPrevious, canNext, onPrevious, onNext, onClose });
  useLayoutEffect(() => {
    latest.current = { canPrevious, canNext, onPrevious, onNext, onClose };
  });

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const handlers = latest.current;
      if (event.key === 'Escape') {
        event.preventDefault();
        handlers.onClose();
      } else if (event.key === 'ArrowLeft' && handlers.canPrevious) {
        event.preventDefault();
        handlers.onPrevious();
      } else if (event.key === 'ArrowRight' && handlers.canNext) {
        event.preventDefault();
        handlers.onNext();
      } else if (event.key === 'Tab' && dialogRef.current) {
        trapFocus(event, dialogRef.current);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  // The page behind does not scroll while the window is open. The width of the scrollbar it hides is kept as padding, so the
  // page does not move sideways.
  useEffect(() => {
    const { overflow, paddingRight } = document.body.style;
    const scrollbar = window.innerWidth - document.documentElement.clientWidth;
    document.body.style.overflow = 'hidden';
    if (scrollbar > 0) document.body.style.paddingRight = `${scrollbar}px`;
    return () => {
      document.body.style.overflow = overflow;
      document.body.style.paddingRight = paddingRight;
    };
  }, []);

  // Focus starts on the close button. After a step to another ad, focus stays in the window: if the button that was used has
  // become disabled, focus moves to the close button instead of being lost. The body starts again at the top of the ad.
  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.contains(document.activeElement)) {
      closeRef.current?.focus({ preventScroll: true });
    }
    bodyRef.current?.scrollTo({ top: 0 });
  }, [card.id]);

  const isMeta = card.platform === 'Meta';
  const isActive = card.status === 'Active';

  const modal = (
    <div
      className="ad-modal-backdrop"
      onMouseDown={(event) => {
        pressedBackdrop.current = event.target === event.currentTarget;
      }}
      onClick={(event) => {
        // A drag that starts inside the window and ends on the backdrop does not close it.
        if (pressedBackdrop.current && event.target === event.currentTarget) onClose();
      }}
    >
      <div
        ref={dialogRef}
        className="ad-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
      >
        <header className="ad-modal__header">
          <span className={`platform-badge ${isMeta ? 'platform-badge--meta' : 'platform-badge--google'}`}>
            {isMeta ? null : <span className="platform-badge__dot" aria-hidden="true" />}
            {isMeta ? 'Meta' : 'Google'}
          </span>
          <span className="ad-modal__store" dir="auto">
            {card.isOurs ? 'Our ad' : card.competitorName}
          </span>
          <span className="ad-modal__status">
            <span className={`status-dot ${isActive ? 'status-dot--active' : 'status-dot--inactive'}`} aria-hidden="true" />
            {isActive ? 'Active' : 'Inactive'}
          </span>
          {position && (
            <span className="ad-modal__position">
              {position.index + 1} of {position.total}
            </span>
          )}
          <button ref={closeRef} type="button" className="ad-modal__close" aria-label="Close" onClick={onClose}>
            ✕
          </button>
        </header>

        <div ref={bodyRef} className="ad-modal__body">
          <AdWindowBody
            key={card.id}
            card={card}
            titleId={titleId}
            onOpenPriceComparisons={onOpenPriceComparisons}
          />
        </div>

        <footer className="ad-modal__nav">
          <button type="button" className="btn btn-secondary" disabled={!canPrevious} onClick={onPrevious}>
            ‹ Previous
          </button>
          <span className="ad-modal__hint">← → move between ads</span>
          <button type="button" className="btn btn-secondary" disabled={!canNext} onClick={onNext}>
            Next ›
          </button>
        </footer>
      </div>
    </div>
  );

  return createPortal(modal, document.body);
};

interface AdWindowBodyProps {
  card: AdGalleryCard;
  titleId: string;
  onOpenPriceComparisons?: (title: string) => void;
}

// Everything about one ad. It is keyed by the card, so a step to another ad starts from its own data.
const AdWindowBody: React.FC<AdWindowBodyProps> = ({ card, titleId, onOpenPriceComparisons }) => {
  const [detail, setDetail] = useState<AdGalleryDetail | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  // The image on show: the card's own ad, or one of its variations (identical ads from the same store).
  const [shown, setShown] = useState({ id: card.id, sha: card.thumbnailSha256, count: card.thumbnailCount });
  const [position, setPosition] = useState(0);
  const [failedSrc, setFailedSrc] = useState<string | null>(null);
  const [history, setHistory] = useState<PriceRow[] | null>(null);
  const [copied, setCopied] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    fetchAdGalleryDetail(card.id)
      .then((data) => {
        if (alive) setDetail(data);
      })
      .catch((err: Error) => {
        if (alive) setDetailError(err.message || 'The ad details could not be loaded.');
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

  const facts = detail?.facts ?? null;
  const variations = detail?.variations ?? [];
  const landingUrl = variations.find((v) => v.id === card.id)?.linkUrl ?? null;
  const isMeta = card.platform === 'Meta';
  const isVideo = card.creativeFormat === 'Video';
  const isActive = card.status === 'Active';
  const quantity = card.advertisedQuantity ?? 1;
  const unitPrice = card.unitPrice;
  const runningSince = facts?.deliveryStartAt ?? card.firstSeenAt;
  const days = daysRunning(runningSince, isActive ? null : (facts?.deliveryStopAt ?? card.lastSeenAt));
  const badge = gapBadge(card.ourPrice, card.unitPrice ?? card.competitorPrice, card.ourPriceGapPercent);
  const copyLabel = (key: string) => (copied === key ? 'Copied' : 'Copy');

  const imageSrc = shown.count > 0 ? thumbnailUrl({ id: shown.id, thumbnailSha256: shown.sha }, position) : null;
  const imageFailed = imageSrc !== null && failedSrc === imageSrc;
  const title = card.headline || card.competitorName;

  const showVariation = (id: string, sha: string | null, count: number) => {
    setShown({ id, sha, count });
    setPosition(0);
  };

  const copyValue = (value: string, key: string) => {
    if (!navigator.clipboard) return;
    navigator.clipboard
      .writeText(value)
      .then(() => {
        setCopied(key);
        window.setTimeout(() => setCopied((current) => (current === key ? null : current)), 1500);
      })
      .catch(() => undefined);
  };

  const advertisedText =
    card.advertisedPrice === null
      ? null
      : quantity > 1 && unitPrice !== null
        ? `${quantity} for ${formatMoney(card.advertisedPrice)} = ${formatMoney(unitPrice)} each`
        : formatMoney(card.advertisedPrice);

  return (
    <div className="ad-modal__grid">
      <div className="ad-modal__media">
        <figure className="ad-modal__figure">
          <div className="ad-modal__frame">
            {imageSrc && !imageFailed ? (
              <img
                src={imageSrc}
                alt={`${card.competitorName} ad, image ${position + 1} of ${shown.count}`}
                onError={() => setFailedSrc(imageSrc)}
              />
            ) : (
              <div className="ad-modal__placeholder" aria-hidden="true">
                <span>{isMeta ? 'M' : 'G'}</span>
                <span>{isVideo ? 'Video' : 'No image'}</span>
              </div>
            )}
            {isVideo && <span className="ad-modal__video-badge">Video</span>}
            {shown.count > 1 && (
              <>
                <button
                  type="button"
                  className="ad-modal__step ad-modal__step--prev"
                  aria-label="Previous image"
                  disabled={position === 0}
                  onClick={() => setPosition((p) => Math.max(0, p - 1))}
                >
                  ‹
                </button>
                <button
                  type="button"
                  className="ad-modal__step ad-modal__step--next"
                  aria-label="Next image"
                  disabled={position >= shown.count - 1}
                  onClick={() => setPosition((p) => Math.min(shown.count - 1, p + 1))}
                >
                  ›
                </button>
              </>
            )}
          </div>
          {shown.count > 1 && (
            <figcaption className="ad-modal__caption">
              Image {position + 1} of {shown.count}
            </figcaption>
          )}
        </figure>

        {isVideo && card.storeUrl && (
          <a className="ad-modal__original" href={card.storeUrl} target="_blank" rel="noopener noreferrer">
            Open the original video ↗
          </a>
        )}

        {variations.length > 1 && (
          <div className="ad-modal__strip" role="group" aria-label={`The ${variations.length} variations of this ad`}>
            {variations.map((variation, index) => {
              const isOnShow = variation.id === shown.id;
              const sha = variation.id === card.id ? card.thumbnailSha256 : null;
              return (
                <button
                  key={variation.id}
                  type="button"
                  className={`ad-modal__thumb${isOnShow ? ' is-active' : ''}`}
                  aria-pressed={isOnShow}
                  aria-label={`Variation ${index + 1}, ${formatDay(variation.firstSeenAt)} to ${formatDay(variation.lastSeenAt)}, ${variation.status}`}
                  onClick={() => showVariation(variation.id, sha, variation.thumbnailCount)}
                >
                  {variation.thumbnailCount > 0 ? (
                    <img src={thumbnailUrl({ id: variation.id, thumbnailSha256: sha }, 0)} alt="" loading="lazy" />
                  ) : (
                    <span>No image</span>
                  )}
                </button>
              );
            })}
          </div>
        )}
      </div>

      <div className="ad-modal__info">
        <section className="ad-modal__section">
          <h2 id={titleId} className="ad-modal__headline" dir="auto">
            {title}
          </h2>
          {card.body ? (
            <p className="ad-modal__text" dir="auto">
              {card.body}
            </p>
          ) : (
            <p className="ad-modal__muted">No text in this ad.</p>
          )}
          {landingUrl && (
            <div className="ad-modal__row">
              <span className="ad-modal__label">Landing page</span>
              <a href={landingUrl} target="_blank" rel="noopener noreferrer" className="ad-modal__url" dir="ltr">
                {shortUrl(landingUrl)}
              </a>
            </div>
          )}
        </section>

        <section className="ad-modal__section">
          <h3>Price</h3>
          {advertisedText ? (
            <div className="ad-modal__row">
              <span className="ad-modal__label">Advertised</span>
              <span>{advertisedText}</span>
            </div>
          ) : (
            <p className="ad-modal__muted">The ad shows no price.</p>
          )}
          {card.competitorPrice !== null && (
            <div className="ad-modal__row">
              <span className="ad-modal__label">Store price</span>
              <span>{formatMoney(card.competitorPrice)}</span>
            </div>
          )}
          {card.ourProductTitle ? (
            <>
              <div className="ad-modal__row">
                <span className="ad-modal__label">Our product</span>
                <span dir="auto">{card.ourProductTitle}</span>
              </div>
              {card.ourPrice !== null && (
                <div className="ad-modal__row">
                  <span className="ad-modal__label">Our price</span>
                  <span>{formatMoney(card.ourPrice)}</span>
                </div>
              )}
              {badge && <span className={`gap-chip ${badge.className}`}>{badge.text}</span>}
            </>
          ) : (
            <p className="ad-modal__warn">
              Not linked to a product of ours.{' '}
              {detail ? linkReasonText(facts?.linkReason) : detailError ? '' : 'Checking the reason…'}
            </p>
          )}
          {card.ourProductId && history !== null && history.length > 0 && (
            <div className="ad-modal__history" aria-label="Price history of our product">
              {history.map((row, index) => (
                <div className="ad-modal__history-row" key={`${row.observedAt}-${index}`}>
                  <span>
                    {formatDay(row.observedAt)}
                    {row.competitorName ? ` · ${row.competitorName}` : ''}
                  </span>
                  <span>
                    {formatMoney(row.price)}
                    {row.inStock === false ? ' (out of stock)' : ''}
                  </span>
                </div>
              ))}
            </div>
          )}
        </section>

        <section className="ad-modal__section">
          <h3>Dates</h3>
          <div className="ad-modal__row">
            <span className="ad-modal__label">Running since</span>
            <span>{formatDayYear(runningSince)}</span>
          </div>
          <div className="ad-modal__row">
            <span className="ad-modal__label">First seen</span>
            <span>{formatDayYear(card.firstSeenAt)}</span>
          </div>
          <div className="ad-modal__row">
            <span className="ad-modal__label">Last seen</span>
            <span>{formatDayYear(card.lastSeenAt)}</span>
          </div>
          <div className="ad-modal__row">
            <span className="ad-modal__label">Days running</span>
            <span>
              {days}
              {isActive ? ' so far' : ''}
            </span>
          </div>
        </section>

        <section className="ad-modal__section">
          <h3>Where it runs</h3>
          <div className="ad-modal__row">
            <span className="ad-modal__label">Platform</span>
            <span>{isMeta ? 'Meta (Facebook and Instagram)' : 'Google'}</span>
          </div>
          <div className="ad-modal__row">
            <span className="ad-modal__label">Placements</span>
            <span className="ad-modal__muted">Not recorded for this ad</span>
          </div>
          <div className="ad-modal__row">
            <span className="ad-modal__label">Region</span>
            <span>Egypt (every search covers Egypt only)</span>
          </div>
        </section>

        <section className="ad-modal__section">
          <h3>IDs</h3>
          {facts ? (
            <>
              <div className="ad-modal__id">
                <span className="ad-modal__label">Ad ID</span>
                <code>{facts.externalAdId}</code>
                <button type="button" className="link-button" onClick={() => copyValue(facts.externalAdId, 'ad')}>
                  {copyLabel('ad')}
                </button>
              </div>
              {facts.advertiserId && (
                <div className="ad-modal__id">
                  <span className="ad-modal__label">{isMeta ? 'Page ID' : 'Advertiser ID'}</span>
                  <code>{facts.advertiserId}</code>
                  <button
                    type="button"
                    className="link-button"
                    onClick={() => copyValue(facts.advertiserId as string, 'advertiser')}
                  >
                    {copyLabel('advertiser')}
                  </button>
                </div>
              )}
            </>
          ) : (
            <p className="ad-modal__muted">{detailError ?? 'Loading…'}</p>
          )}
        </section>

        {variations.length > 1 && (
          <section className="ad-modal__section">
            <h3>This card groups {variations.length} identical ads</h3>
            <ul className="ad-modal__variations">
              {variations.map((variation, index) => (
                <li key={variation.id}>
                  <span>
                    {index + 1}. {formatDay(variation.firstSeenAt)} to {formatDay(variation.lastSeenAt)} · {variation.status}
                    {variation.creativeFormat ? ` · ${variation.creativeFormat}` : ''}
                  </span>
                  {variation.storeUrl && (
                    <a href={variation.storeUrl} target="_blank" rel="noopener noreferrer">
                      Open ↗
                    </a>
                  )}
                </li>
              ))}
            </ul>
          </section>
        )}

        <div className="ad-modal__actions">
          {card.storeUrl && (
            <a className="btn btn-primary" href={card.storeUrl} target="_blank" rel="noopener noreferrer">
              {isMeta ? 'View on Meta Ad Library' : 'View on Google Ads Transparency'} ↗
            </a>
          )}
          {landingUrl ? (
            <a className="btn btn-secondary" href={landingUrl} target="_blank" rel="noopener noreferrer">
              Open landing page ↗
            </a>
          ) : (
            <span className="ad-modal__muted">No landing page</span>
          )}
          {card.ourProductId && onOpenPriceComparisons ? (
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => onOpenPriceComparisons(card.ourProductTitle ?? '')}
            >
              Open product in Price comparisons
            </button>
          ) : (
            <button type="button" className="btn btn-secondary" disabled title="This ad is not linked to a product of ours">
              Open product in Price comparisons
            </button>
          )}
        </div>
      </div>
    </div>
  );
};

// The landing page as host and path, for reading. The full address is in the link.
function shortUrl(url: string): string {
  try {
    const parsed = new URL(url);
    return `${parsed.host}${parsed.pathname === '/' ? '' : parsed.pathname}`;
  } catch {
    return url;
  }
}
