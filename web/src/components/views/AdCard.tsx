import React, { useState } from 'react';
import { thumbnailUrl, type AdGalleryCard } from '../../api/adGallery';
import { formatDay, formatMoney, gapChip } from './adsFormat';

interface AdCardProps {
  card: AdGalleryCard;
  // The element is the card itself: the ad window gives focus back to it when it closes.
  onOpen: (card: AdGalleryCard, opener: HTMLElement) => void;
  onOpenProduct?: (productId: string, title?: string) => void;
}

const LONG_BODY = 120;

// One ad in the gallery. The whole card opens the ad window; the links and buttons inside it do their own job.
// "Linked product" means our catalog product that this ad's store product matches; a card without one shows no product row.
export const AdCard: React.FC<AdCardProps> = ({ card, onOpen, onOpenProduct }) => {
  const [imageFailed, setImageFailed] = useState(false);
  const [expanded, setExpanded] = useState(false);

  const isMeta = card.platform === 'Meta';
  const isVideo = card.creativeFormat === 'Video';
  const isActive = card.status === 'Active';
  const hasImage = card.thumbnailCount > 0 && !imageFailed;
  const quantity = card.advertisedQuantity ?? 1;
  const chip = gapChip(card.ourPriceGapPercent);
  const storeLabel = isMeta ? 'View on Meta Ad Library' : 'View on Google Ads Transparency';

  const stop = (event: React.SyntheticEvent) => event.stopPropagation();

  return (
    <article
      className="ad-card"
      role="button"
      tabIndex={0}
      aria-haspopup="dialog"
      aria-label={`${card.competitorName}: ${card.headline ?? 'ad'}`}
      onClick={(event) => onOpen(card, event.currentTarget)}
      onKeyDown={(event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          onOpen(card, event.currentTarget);
        }
      }}
    >
      <header className="ad-card__header">
        <span className={`platform-badge ${isMeta ? 'platform-badge--meta' : 'platform-badge--google'}`}>
          {isMeta ? null : <span className="platform-badge__dot" aria-hidden="true" />}
          {isMeta ? 'Meta' : 'Google'}
        </span>
        <span className="ad-card__store" dir="auto">{card.isOurs ? 'Our ad' : card.competitorName}</span>
        <span
          className={`status-dot ${isActive ? 'status-dot--active' : 'status-dot--inactive'}`}
          role="img"
          aria-label={isActive ? 'Active' : 'Inactive'}
        />
      </header>

      <div className="ad-card__media">
        {hasImage ? (
          <img
            src={thumbnailUrl(card)}
            alt=""
            /* The card's media box is 16:9 and thumbnails are at most 480 px wide, so the box keeps its size while images load. */
            width={480}
            height={270}
            loading="lazy"
            decoding="async"
            onError={() => setImageFailed(true)}
          />
        ) : (
          <div className="ad-card__placeholder" aria-hidden="true">
            {/* A video has no still image: the play button alone marks it, so no text sits under the button. */}
            {!isVideo && (
              <>
                <span>{isMeta ? 'M' : 'G'}</span>
                <span>No image</span>
              </>
            )}
          </div>
        )}
        {isVideo && <span className="ad-card__play" aria-hidden="true">▶</span>}
        {card.thumbnailCount > 1 && <span className="ad-card__count">1/{card.thumbnailCount}</span>}
      </div>

      {(card.headline || card.body) && (
        <div className="ad-card__text">
          {card.headline && <div className="ad-card__headline" dir="auto">{card.headline}</div>}
          {card.body && (
            <>
              <div className={`ad-card__body ${expanded ? '' : 'ad-card__body--clamped'}`} dir="auto">
                {card.body}
              </div>
              {card.body.length > LONG_BODY && (
                <button
                  type="button"
                  className="link-button"
                  aria-expanded={expanded}
                  onClick={(event) => {
                    stop(event);
                    setExpanded((value) => !value);
                  }}
                  onKeyDown={stop}
                >
                  {expanded ? 'less' : 'more'}
                </button>
              )}
            </>
          )}
        </div>
      )}

      {(card.advertisedPrice !== null || card.ourPrice !== null) && (
        <div className="ad-card__prices">
          {card.advertisedPrice !== null && (
            <div>
              Advertised {quantity > 1 ? `${quantity} for ` : ''}
              {formatMoney(card.advertisedPrice)}
              {quantity > 1 && card.unitPrice !== null ? ` (${formatMoney(card.unitPrice)} each)` : ''}
            </div>
          )}
          {card.ourPrice !== null && (
            <div>
              Our price {formatMoney(card.ourPrice)}
              {chip && <span className={`gap-chip ${chip.className}`}>{chip.text}</span>}
            </div>
          )}
        </div>
      )}

      {card.ourProductTitle && (
        <div className="ad-card__product">
          {onOpenProduct && card.ourProductId ? (
            <button
              type="button"
              className="link-button"
              onClick={(event) => {
                stop(event);
                onOpenProduct(card.ourProductId as string, card.ourProductTitle ?? undefined);
              }}
              onKeyDown={stop}
            >
              {card.ourProductTitle}
            </button>
          ) : (
            card.ourProductTitle
          )}
        </div>
      )}

      <footer className="ad-card__footer">
        <div>
          Running since {formatDay(card.firstSeenAt)} · last seen {formatDay(card.lastSeenAt)}
          {card.variationCount > 1 && ` · ×${card.variationCount} variations`}
        </div>
        {card.storeUrl && (
          <a
            className="ad-card__store-link"
            href={card.storeUrl}
            target="_blank"
            rel="noopener noreferrer"
            onClick={stop}
            onKeyDown={stop}
          >
            {storeLabel} ↗
          </a>
        )}
      </footer>
    </article>
  );
};
