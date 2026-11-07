import React, { useState, useMemo, useEffect } from 'react';
import type { PriceMatrixResponse, PriceMatrixRow } from '../../api/client';
import { undoMatch } from '../../api/alerts';
import { PageHeader } from '../shell/PageHeader';
import { hashView, readHashParams, writeHashParams } from '../../lib/pageUrlState';

interface PriceMatrixViewProps {
  matrix: PriceMatrixResponse | null;
  matrixError?: { status?: number; message: string } | null;
  selectedRunId: string | null;
  isLoading?: boolean;
  onOpenProductDetail?: (productId: string, title?: string, row?: PriceMatrixRow) => void;
  onRetry?: () => void;
  // The report date bar, shown in the page header row.
  toolbar?: React.ReactNode;
}

const COMPETITOR_COLUMNS = [
  { key: 'cafelax', name: 'CAFELAX' },
  { key: 'coffee-and-cream', name: 'Coffee & Cream' },
  { key: 'fengany', name: 'Fengany' },
  { key: 'coffee-zone', name: 'Coffee Zone Misr' },
  { key: 'strong-coffee', name: 'Strong Coffee' },
];

export const PriceMatrixView: React.FC<PriceMatrixViewProps> = ({
  matrix,
  matrixError,
  selectedRunId,
  isLoading,
  onOpenProductDetail,
  onRetry,
  toolbar,
}) => {
  // Filters start from the URL hash, so a reload or a return to the page restores them.
  const [initial] = useState(() => readHashParams());
  const [search, setSearch] = useState(() => initial.get('q') ?? '');
  const [selectedCategory, setSelectedCategory] = useState(() => initial.get('category') ?? 'ALL');
  const [selectedBrand, setSelectedBrand] = useState(() => initial.get('brand') ?? 'ALL');
  const [selectedPosition, setSelectedPosition] = useState(() => initial.get('position') ?? 'ALL');

  const rows = matrix?.rows ?? [];

  // Extract distinct categories and brands for filter dropdowns
  const categories = useMemo(() => {
    const set = new Set<string>();
    rows.forEach((r) => {
      if (r.canonicalCategory) set.add(r.canonicalCategory);
      else if (r.category) set.add(r.category);
    });
    return Array.from(set).sort();
  }, [rows]);

  const brands = useMemo(() => {
    const set = new Set<string>();
    rows.forEach((r) => {
      if (r.brand) set.add(r.brand);
    });
    return Array.from(set).sort();
  }, [rows]);

  // Filtered rows
  const filteredRows = useMemo(() => {
    return rows.filter((r) => {
      // Search
      if (search.trim()) {
        const q = search.toLowerCase();
        const matchTitle = r.lazyBeanTitle.toLowerCase().includes(q);
        const matchBrand = r.brand?.toLowerCase().includes(q) ?? false;
        const matchVendor = r.lazyBeanVendor?.toLowerCase().includes(q) ?? false;
        if (!matchTitle && !matchBrand && !matchVendor) return false;
      }

      // Category
      if (selectedCategory !== 'ALL') {
        const cat = r.canonicalCategory || r.category;
        if (cat !== selectedCategory) return false;
      }

      // Brand
      if (selectedBrand !== 'ALL') {
        if (r.brand !== selectedBrand) return false;
      }

      // Position
      if (selectedPosition !== 'ALL') {
        if (r.position !== selectedPosition) return false;
      }

      return true;
    });
  }, [rows, search, selectedCategory, selectedBrand, selectedPosition]);

  // The filters go to the URL with replaceState, so Back returns to the previous section and not to a filter state.
  useEffect(() => {
    writeHashParams(hashView(), {
      q: search.trim() || undefined,
      category: selectedCategory === 'ALL' ? undefined : selectedCategory,
      brand: selectedBrand === 'ALL' ? undefined : selectedBrand,
      position: selectedPosition === 'ALL' ? undefined : selectedPosition,
    });
  }, [search, selectedCategory, selectedBrand, selectedPosition]);

  // All filtered rows displayed on a single page
  const pageRows = filteredRows;

  // CSV download URL
  const csvUrl = selectedRunId
    ? `/api/runs/${selectedRunId}/price-matrix/csv`
    : '/api/price-matrix/latest/csv';

  const formatObsDate = (dateStr?: string) => {
    if (!dateStr) return '';
    try {
      const d = new Date(dateStr);
      return new Intl.DateTimeFormat('en-GB', {
        timeZone: 'Africa/Cairo',
        month: 'short',
        day: 'numeric',
      }).format(d);
    } catch {
      return '';
    }
  };

  return (
    <div className="price-matrix-view">
      <PageHeader
        title="Price comparisons"
        info="Compare saved prices and check the source before acting. Real-time parity across 5 stores."
        actions={
          <>
            {toolbar}
            <a href={csvUrl} download className="btn-primary" style={{ textDecoration: 'none' }}>
              ⬇ Export Matrix CSV
            </a>
          </>
        }
      />

      {/* Slim stats strip, one line of text */}
      <div className="price-stats-strip">
        <span>
          <strong>1,592</strong> Lazy Bean catalog
        </span>
        <span className="sep">·</span>
        <span>
          <strong>{rows.length}</strong> matched
        </span>
        <span className="sep">·</span>
        <span>
          <strong>14,472</strong> competitor products
        </span>
        <span className="sep">·</span>
        <span>
          <strong>{brands.length}</strong> brands
        </span>
      </div>

      {matrixError && (
        <div
          style={{
            padding: '16px 20px',
            background: '#fee2e2',
            border: '1px solid #f87171',
            borderRadius: '8px',
            marginBottom: '20px',
            color: '#991b1b',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: '12px',
          }}
        >
          <div>
            <div style={{ fontWeight: 700, fontSize: '14px', marginBottom: '4px' }}>
              ⚠️ Failed to load Price Comparisons (HTTP {matrixError.status || 500})
            </div>
            <div style={{ fontSize: '13px' }}>{matrixError.message}</div>
          </div>
          {onRetry && (
            <button
              type="button"
              className="btn-secondary"
              style={{
                background: '#fff',
                borderColor: '#dc2626',
                color: '#dc2626',
                fontWeight: 600,
                padding: '6px 16px',
                flexShrink: 0,
              }}
              onClick={onRetry}
            >
              Retry
            </button>
          )}
        </div>
      )}

      {/* Matrix Container */}
      <div className="matrix-container">
        {/* Filters Bar */}
        <div className="matrix-filters-bar">
          <input
            type="text"
            className="filter-input"
            placeholder="Search product, brand, seller..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />

          <select
            className="filter-select"
            value={selectedCategory}
            onChange={(e) => setSelectedCategory(e.target.value)}
          >
            <option value="ALL">All Categories</option>
            {categories.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>

          <select
            className="filter-select"
            value={selectedBrand}
            onChange={(e) => setSelectedBrand(e.target.value)}
          >
            <option value="ALL">All Brands</option>
            {brands.map((b) => (
              <option key={b} value={b}>
                {b}
              </option>
            ))}
          </select>

          <select
            className="filter-select"
            value={selectedPosition}
            onChange={(e) => setSelectedPosition(e.target.value)}
          >
            <option value="ALL">All Positions</option>
            <option value="cheapest">Cheapest (Best Price)</option>
            <option value="within 5%">Within 5%</option>
            <option value="more expensive">More Expensive</option>
            <option value="lazyBeanOutOfStock">Lazy Bean Out of Stock</option>
          </select>

          {/* Count, on the right of the filter row */}
          <span className="matrix-count">
            {isLoading ? (
              <span className="skeleton skeleton-text" style={{ width: '120px', display: 'inline-block' }} />
            ) : (
              <>
                Showing <strong>{filteredRows.length}</strong> {filteredRows.length === 1 ? 'product' : 'products'}
                {filteredRows.length < rows.length && (
                  <span style={{ color: 'var(--muted)', marginLeft: '6px' }}>
                    (filtered from {rows.length} total)
                  </span>
                )}
              </>
            )}
          </span>
        </div>

        {/* Matrix Table */}
        <div className="matrix-table-wrap">
          {isLoading ? (
            <div style={{ padding: '24px' }}>
              <div className="skeleton skeleton-row" style={{ height: '42px', marginBottom: '12px' }} />
              <div className="skeleton skeleton-row" style={{ height: '42px', marginBottom: '12px' }} />
              <div className="skeleton skeleton-row" style={{ height: '42px', marginBottom: '12px' }} />
              <div className="skeleton skeleton-row" style={{ height: '42px', marginBottom: '12px' }} />
              <div className="skeleton skeleton-row" style={{ height: '42px', marginBottom: '12px' }} />
            </div>
          ) : (
            <table className="matrix-table">
              <thead>
                <tr>
                  <th className="sticky-col" style={{ width: '280px' }}>
                    Lazy Bean Product
                  </th>
                  <th className="matrix-own" style={{ width: '130px' }}>
                    Lazy Bean (Own)
                  </th>
                  {COMPETITOR_COLUMNS.map((col) => (
                    <th key={col.key} style={{ width: '140px' }}>
                      {col.name}
                    </th>
                  ))}
                  <th style={{ width: '130px' }}>Cheapest Competitor</th>
                  <th style={{ width: '100px' }}>Position</th>
                </tr>
              </thead>
              <tbody>
                {pageRows.map((row) => (
                  <tr key={row.lazyBeanVariantId}>
                    {/* Sticky Product Cell */}
                    <td className="sticky-col">
                      <div className="product-cell">
                        {row.lazyBeanUrl ? (
                          <a
                            href={row.lazyBeanUrl}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="product-title-link"
                            title={row.lazyBeanTitle}
                          >
                            {row.lazyBeanTitle} ↗
                          </a>
                        ) : (
                          <span
                            className="product-title-link"
                            style={{ textDecoration: 'none', cursor: 'default' }}
                            title={row.lazyBeanTitle}
                          >
                            {row.lazyBeanTitle}
                          </span>
                        )}
                        <div className="product-meta-row">
                          {row.brand && <span className="badge neutral">{row.brand}</span>}
                          {row.lazyBeanVendor && row.lazyBeanVendor !== row.brand && (
                            <span title="Marketplace seller">🏪 {row.lazyBeanVendor}</span>
                          )}
                          {row.weightGrams && <span>{row.weightGrams}g</span>}
                          {row.unitCount && <span>{row.unitCount} caps</span>}
                          <span className="badge blue">{row.canonicalCategory}</span>
                          {onOpenProductDetail && (
                            <button
                              type="button"
                              className="btn-xs ghost"
                              style={{ marginLeft: 'auto', fontSize: '10px', padding: '1px 6px' }}
                              onClick={() => onOpenProductDetail(row.lazyBeanProductId, row.lazyBeanTitle, row)}
                            >
                              Details
                            </button>
                          )}
                        </div>
                      </div>
                    </td>

                    {/* Lazy Bean Own Price */}
                    <td className="matrix-own">
                      <div className="price-cell">
                        <div className="matrix-price">{row.lazyBeanPrice.toFixed(2)} EGP</div>
                        <div>
                          {row.lazyBeanInStock ? (
                            <span className="badge good">In Stock</span>
                          ) : (
                            <span className="badge bad">Out of Stock</span>
                          )}
                        </div>
                      </div>
                    </td>

                    {/* Competitor Columns */}
                    {COMPETITOR_COLUMNS.map((col) => {
                      const cell = row.competitorPrices?.[col.key] || (col.key === 'coffee-zone' ? row.competitorPrices?.['coffee-zone-misr'] : undefined);
                      if (!cell) {
                        return (
                          <td key={col.key} style={{ color: 'var(--muted)', fontSize: '11px' }}>
                            —
                          </td>
                        );
                      }

                      const diffPct = cell.differencePercentageVsLazyBean;
                      const isCheaper = cell.price < row.lazyBeanPrice;
                      const isExpensive = cell.price > row.lazyBeanPrice;

                      return (
                        <td key={col.key}>
                          <div className="price-cell">
                            <div className="matrix-price">{cell.price.toFixed(2)} EGP</div>
                            {diffPct != null && diffPct !== 0 && (
                              <div
                                className={`diff-badge ${
                                  isCheaper ? 'cheaper' : isExpensive ? 'expensive' : ''
                                }`}
                              >
                                {diffPct > 0 ? `+${diffPct.toFixed(1)}%` : `${diffPct.toFixed(1)}%`}
                              </div>
                            )}
                            <div>
                              {cell.inStock ? (
                                <span className="badge good" style={{ fontSize: '10px' }}>
                                  Available
                                </span>
                              ) : (
                                <span className="badge bad" style={{ fontSize: '10px' }}>
                                  Out of stock
                                </span>
                              )}
                            </div>
                            {cell.competitorUrl ? (
                              <a
                                href={cell.competitorUrl}
                                target="_blank"
                                rel="noopener noreferrer"
                                style={{
                                  fontSize: '11px',
                                  color: 'var(--muted)',
                                  textDecoration: 'none',
                                  overflow: 'hidden',
                                  textOverflow: 'ellipsis',
                                  whiteSpace: 'nowrap',
                                  display: 'block',
                                  maxWidth: '130px',
                                }}
                                title={cell.competitorTitle}
                              >
                                {cell.competitorTitle} ↗
                              </a>
                            ) : (
                              <span
                                style={{
                                  fontSize: '11px',
                                  color: 'var(--muted)',
                                  overflow: 'hidden',
                                  textOverflow: 'ellipsis',
                                  whiteSpace: 'nowrap',
                                  display: 'block',
                                  maxWidth: '130px',
                                }}
                                title={cell.competitorTitle}
                              >
                                {cell.competitorTitle}
                              </span>
                            )}
                            <div className="obs-date">{formatObsDate(cell.observedAt)}</div>
                            {cell.matchMethod === 'AI' && cell.matchId && (
                              <div style={{ marginTop: '4px', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '4px' }}>
                                <span style={{ fontSize: '9px', background: '#ecfdf5', color: '#047857', padding: '1px 4px', borderRadius: '3px', fontWeight: 600 }}>
                                  AI
                                </span>
                                <button
                                  type="button"
                                  style={{
                                    border: 'none',
                                    background: 'transparent',
                                    color: '#dc2626',
                                    fontSize: '10px',
                                    cursor: 'pointer',
                                    padding: '0',
                                    textDecoration: 'underline'
                                  }}
                                  onClick={async (e) => {
                                    e.stopPropagation();
                                    if (confirm(`Undo AI match for "${cell.competitorTitle}"?`)) {
                                      await undoMatch(cell.matchId!);
                                      window.location.reload();
                                    }
                                  }}
                                  title="Undo AI match and unlink from matrix"
                                >
                                  Undo match
                                </button>
                              </div>
                            )}
                          </div>
                        </td>
                      );
                    })}

                    {/* Cheapest Competitor */}
                    <td>
                      {row.cheapestCompetitorName ? (
                        <div className="price-cell">
                          <strong>{row.cheapestCompetitorName}</strong>
                          <span className="matrix-price">
                            {row.cheapestCompetitorPrice?.toFixed(2)} EGP
                          </span>
                        </div>
                      ) : (
                        <span style={{ color: 'var(--muted)', fontSize: '12px' }}>—</span>
                      )}
                    </td>

                    {/* Position */}
                    <td>
                      {row.position === 'lazyBeanOutOfStock' ? (
                        <span className="badge bad">LB Out of Stock</span>
                      ) : row.position === 'cheapest' ? (
                        <span className="badge good">Cheapest</span>
                      ) : row.position === 'within 5%' ? (
                        <span className="badge warn">Within 5%</span>
                      ) : (
                        <span className="badge bad">More Expensive</span>
                      )}
                    </td>
                  </tr>
                ))}

                {pageRows.length === 0 && (
                  <tr>
                    <td colSpan={9} style={{ textAlign: 'center', padding: '30px', color: 'var(--muted)' }}>
                      No matching products found for the selected filters.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          )}
        </div>
      </div>
    </div>
  );
};
