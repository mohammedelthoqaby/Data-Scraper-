import React, { useState, useEffect, useMemo } from 'react';
import { api, type PriceHistoryPoint, type PriceMatrixRow } from '../../api/client';

interface ProductDetailModalProps {
  isOpen: boolean;
  onClose: () => void;
  productId: string | null;
  productTitle?: string;
  matrixRow?: PriceMatrixRow | null;
}

const STORE_COLORS: Record<string, string> = {
  'Lazy Bean': '#164f40',
  'CAFELAX': '#2563eb',
  'Coffee & Cream': '#d97706',
  'Fengany': '#7c3aed',
  'Coffee Zone Misr': '#059669',
  'Strong Coffee': '#dc2626',
};

export const ProductDetailModal: React.FC<ProductDetailModalProps> = ({
  isOpen,
  onClose,
  productId,
  productTitle,
  matrixRow,
}) => {
  const [historyPoints, setHistoryPoints] = useState<PriceHistoryPoint[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [hoveredPoint, setHoveredPoint] = useState<PriceHistoryPoint | null>(null);

  useEffect(() => {
    if (!isOpen || !productId) return;

    let isMounted = true;
    const fetchHistory = async () => {
      setIsLoading(true);
      setError(null);
      try {
        const res = await api.GET('/api/history/prices', {
          params: { query: { productId, pageSize: 100 } },
        });
        if (isMounted && res.data?.items) {
          // Sort chronologically ascending
          const sorted = [...res.data.items].sort(
            (a, b) => new Date(a.observedAt || 0).getTime() - new Date(b.observedAt || 0).getTime()
          );
          setHistoryPoints(sorted);
        }
      } catch (err: any) {
        if (isMounted) setError(err?.message || 'Failed to fetch price history');
      } finally {
        if (isMounted) setIsLoading(false);
      }
    };

    fetchHistory();
    return () => {
      isMounted = false;
    };
  }, [isOpen, productId]);

  // Group history points by competitor
  const seriesByStore = useMemo(() => {
    const map = new Map<string, PriceHistoryPoint[]>();
    historyPoints.forEach((p) => {
      const store = p.competitorName || 'Unknown';
      if (!map.has(store)) map.set(store, []);
      map.get(store)!.push(p);
    });
    return map;
  }, [historyPoints]);

  // Compute SVG chart coordinates
  const chartData = useMemo(() => {
    if (historyPoints.length === 0) return null;

    const prices = historyPoints.map((p) => p.price ?? 0).filter((pr) => pr > 0);
    if (prices.length === 0) return null;

    const minPrice = Math.min(...prices) * 0.9;
    const maxPrice = Math.max(...prices) * 1.1;

    const timestamps = historyPoints.map((p) => new Date(p.observedAt || 0).getTime());
    let minTime = Math.min(...timestamps);
    let maxTime = Math.max(...timestamps);

    // If all points share same timestamp, expand artificially by 1 hour for display
    if (minTime === maxTime) {
      minTime -= 3600000;
      maxTime += 3600000;
    }

    const svgWidth = 760;
    const svgHeight = 220;
    const padding = { top: 20, right: 30, bottom: 35, left: 65 };

    const plotWidth = svgWidth - padding.left - padding.right;
    const plotHeight = svgHeight - padding.top - padding.bottom;

    const getX = (time: number) => padding.left + ((time - minTime) / (maxTime - minTime)) * plotWidth;
    const getY = (price: number) => padding.top + plotHeight - ((price - minPrice) / (maxPrice - minPrice)) * plotHeight;

    const lines: Array<{ store: string; color: string; pathD: string; points: Array<{ x: number; y: number; raw: PriceHistoryPoint }> }> = [];

    seriesByStore.forEach((points, store) => {
      const color = STORE_COLORS[store] || '#626b76';
      const pts = points.map((p) => ({
        x: getX(new Date(p.observedAt || 0).getTime()),
        y: getY(p.price ?? 0),
        raw: p,
      }));

      const pathD = pts.reduce((acc, pt, i) => (i === 0 ? `M ${pt.x} ${pt.y}` : `${acc} L ${pt.x} ${pt.y}`), '');

      lines.push({ store, color, pathD, points: pts });
    });

    // Y-axis ticks (4 ticks)
    const yTicks = [
      minPrice,
      minPrice + (maxPrice - minPrice) * 0.33,
      minPrice + (maxPrice - minPrice) * 0.66,
      maxPrice,
    ].map((val) => ({
      val: Math.round(val),
      y: getY(val),
    }));

    return { svgWidth, svgHeight, padding, lines, yTicks, minTime, maxTime, getX };
  }, [historyPoints, seriesByStore]);

  if (!isOpen) return null;

  const displayTitle = matrixRow?.lazyBeanTitle || productTitle || 'Product Price History';

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div
        className="modal-container product-detail-modal"
        onClick={(e) => e.stopPropagation()}
        style={{ maxHeight: '90vh', overflowY: 'auto' }}
      >
        {/* Modal Header */}
        <div className="modal-header">
          <div>
            <div className="eyebrow">PRODUCT DETAIL & EVIDENCE</div>
            <h2 className="modal-title" style={{ fontSize: '18px', marginTop: '2px' }}>
              {displayTitle}
            </h2>
          </div>
          <button
            type="button"
            className="btn-xs ghost"
            onClick={onClose}
            style={{ fontSize: '16px', padding: '4px 8px' }}
          >
            ✕
          </button>
        </div>

        {/* Modal Body */}
        <div className="modal-body">
          {/* Metadata Grid */}
          <div className="product-meta-summary">
            <div className="meta-item">
              <span className="meta-item-label">Brand</span>
              <span className="meta-item-value">{matrixRow?.brand || 'Lavazza'}</span>
            </div>
            <div className="meta-item">
              <span className="meta-item-label">Category</span>
              <span className="meta-item-value">{matrixRow?.category || matrixRow?.canonicalCategory || 'Coffee'}</span>
            </div>
            <div className="meta-item">
              <span className="meta-item-label">Lazy Bean Price</span>
              <span className="meta-item-value" style={{ color: 'var(--action)' }}>
                {matrixRow?.lazyBeanPrice ? `${matrixRow.lazyBeanPrice.toFixed(2)} EGP` : '—'}
              </span>
            </div>
            <div className="meta-item">
              <span className="meta-item-label">Market Median</span>
              <span className="meta-item-value">
                {matrixRow?.medianCompetitorPrice ? `${matrixRow.medianCompetitorPrice.toFixed(2)} EGP` : '—'}
              </span>
            </div>
          </div>

          {/* Interactive Price History Chart Card */}
          <div className="chart-card">
            <div className="chart-header">
              <div className="chart-title">Price History Timeline (EGP)</div>
              {/* Legend */}
              <div className="chart-legend">
                {Array.from(seriesByStore.keys()).map((store) => (
                  <div key={store} className="legend-item">
                    <span
                      className="legend-color-dot"
                      style={{ background: STORE_COLORS[store] || '#626b76' }}
                    />
                    <span>{store}</span>
                  </div>
                ))}
              </div>
            </div>

            {isLoading ? (
              <div style={{ height: '220px', display: 'grid', placeItems: 'center', color: 'var(--muted)' }}>
                Loading historical observations...
              </div>
            ) : error ? (
              <div style={{ height: '220px', display: 'grid', placeItems: 'center', color: '#962f32' }}>
                {error}
              </div>
            ) : chartData && chartData.lines.length > 0 ? (
              <div className="chart-svg-container">
                <svg
                  viewBox={`0 0 ${chartData.svgWidth} ${chartData.svgHeight}`}
                  style={{ width: '100%', height: '100%', overflow: 'visible' }}
                >
                  {/* Gridlines & Y-ticks */}
                  {chartData.yTicks.map((t) => (
                    <g key={t.val}>
                      <line
                        x1={chartData.padding.left}
                        y1={t.y}
                        x2={chartData.svgWidth - chartData.padding.right}
                        y2={t.y}
                        stroke="#dfe3e8"
                        strokeDasharray="4 4"
                      />
                      <text
                        x={chartData.padding.left - 8}
                        y={t.y + 4}
                        textAnchor="end"
                        fontSize="11"
                        fill="#626b76"
                      >
                        {t.val} EGP
                      </text>
                    </g>
                  ))}

                  {/* Lines & Data Points */}
                  {chartData.lines.map((line) => (
                    <g key={line.store}>
                      <path
                        d={line.pathD}
                        fill="none"
                        stroke={line.color}
                        strokeWidth="2.5"
                        strokeLinecap="round"
                        strokeLinejoin="round"
                      />
                      {line.points.map((pt, i) => (
                        <circle
                          key={i}
                          cx={pt.x}
                          cy={pt.y}
                          r={hoveredPoint === pt.raw ? 6 : 4}
                          fill={line.color}
                          stroke="#ffffff"
                          strokeWidth="2"
                          style={{ cursor: 'pointer', transition: 'r 0.15s ease' }}
                          onMouseEnter={() => setHoveredPoint(pt.raw)}
                          onMouseLeave={() => setHoveredPoint(null)}
                        />
                      ))}
                    </g>
                  ))}
                </svg>

                {/* Hover Tooltip */}
                {hoveredPoint && (
                  <div
                    style={{
                      position: 'absolute',
                      top: '10px',
                      right: '15px',
                      background: 'rgba(22, 27, 34, 0.92)',
                      color: '#ffffff',
                      padding: '6px 12px',
                      borderRadius: '6px',
                      fontSize: '12px',
                      boxShadow: '0 4px 12px rgba(0,0,0,0.15)',
                      pointerEvents: 'none',
                    }}
                  >
                    <strong>{hoveredPoint.competitorName}</strong>: {hoveredPoint.price?.toFixed(2)} EGP
                    <div style={{ fontSize: '10px', color: '#b0b8c1' }}>
                      {hoveredPoint.observedAt
                        ? new Date(hoveredPoint.observedAt).toLocaleString('en-GB')
                        : ''}
                    </div>
                  </div>
                )}
              </div>
            ) : (
              <div style={{ height: '220px', display: 'grid', placeItems: 'center', color: 'var(--muted)' }}>
                No historical price points recorded for this product yet.
              </div>
            )}
          </div>

          {/* Competitor Price Observations Table */}
          <div>
            <h3 style={{ fontSize: '14px', fontWeight: 700, margin: '0 0 10px' }}>
              Historical Price Observations Log ({historyPoints.length})
            </h3>
            <div style={{ maxHeight: '200px', overflowY: 'auto', border: '1px solid var(--line)', borderRadius: '6px' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '13px' }}>
                <thead style={{ background: '#fafbfc', position: 'sticky', top: 0, borderBottom: '1px solid var(--line)' }}>
                  <tr>
                    <th style={{ padding: '8px 12px', textAlign: 'left' }}>Store</th>
                    <th style={{ padding: '8px 12px', textAlign: 'left' }}>Observed Date</th>
                    <th style={{ padding: '8px 12px', textAlign: 'right' }}>Price</th>
                    <th style={{ padding: '8px 12px', textAlign: 'center' }}>Stock</th>
                  </tr>
                </thead>
                <tbody>
                  {historyPoints.map((pt) => (
                    <tr key={pt.observationId} style={{ borderBottom: '1px solid #edf0f2' }}>
                      <td style={{ padding: '8px 12px', fontWeight: 600 }}>{pt.competitorName}</td>
                      <td style={{ padding: '8px 12px', color: 'var(--muted)' }}>
                        {pt.observedAt ? new Date(pt.observedAt).toLocaleString('en-GB') : '—'}
                      </td>
                      <td style={{ padding: '8px 12px', textAlign: 'right', fontWeight: 700 }}>
                        {pt.price?.toFixed(2)} {pt.currency || 'EGP'}
                      </td>
                      <td style={{ padding: '8px 12px', textAlign: 'center' }}>
                        <span className={`badge ${pt.inStock ? 'good' : 'bad'}`} style={{ fontSize: '10px' }}>
                          {pt.inStock ? 'Available' : 'Out of Stock'}
                        </span>
                      </td>
                    </tr>
                  ))}
                  {historyPoints.length === 0 && (
                    <tr>
                      <td colSpan={4} style={{ padding: '20px', textAlign: 'center', color: 'var(--muted)' }}>
                        No observations found.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </div>

        {/* Modal Footer */}
        <div className="modal-footer">
          {matrixRow?.lazyBeanUrl && (
            <a
              href={matrixRow.lazyBeanUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="btn-secondary"
              style={{ textDecoration: 'none', display: 'inline-block' }}
            >
              Open on Lazy Bean Store ↗
            </a>
          )}
          <button type="button" className="btn-primary" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
};
