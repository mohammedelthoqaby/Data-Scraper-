import React, { useState, useMemo } from 'react';
import type { PriceMatrixResponse, PriceMatrixRow } from '../../api/client';

interface PriceMatrixViewProps {
  matrix: PriceMatrixResponse | null;
  selectedRunId: string | null;
  isLoading?: boolean;
  onOpenProductDetail?: (productId: string, title?: string, row?: PriceMatrixRow) => void;
}

const COMPETITOR_COLUMNS = [
  { key: 'cafelax', name: 'CAFELAX' },
  { key: 'coffee-and-cream', name: 'Coffee & Cream' },
  { key: 'fengany', name: 'Fengany' },
  { key: 'coffee-zone-misr', name: 'Coffee Zone Misr' },
  { key: 'strong-coffee', name: 'Strong Coffee' },
];

export const PriceMatrixView: React.FC<PriceMatrixViewProps> = ({
  matrix,
  selectedRunId,
  isLoading,
  onOpenProductDetail,
}) => {
  const [search, setSearch] = useState('');
  const [selectedCategory, setSelectedCategory] = useState('ALL');
  const [selectedBrand, setSelectedBrand] = useState('ALL');
  const [selectedPosition, setSelectedPosition] = useState('ALL');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);

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

  // Pagination
  const totalFiltered = filteredRows.length;
  const totalPages = Math.max(1, Math.ceil(totalFiltered / pageSize));
  const currentPage = Math.min(page, totalPages);
  const startIndex = (currentPage - 1) * pageSize;
  const pageRows = filteredRows.slice(startIndex, startIndex + pageSize);

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
      {/* Page Heading */}
      <div className="page-heading">
        <div>
          <div className="eyebrow">PRICE COMPARISON INTELLIGENCE</div>
          <h1 className="view-title">Price comparisons</h1>
          <p className="subtitle">
            Compare saved prices and check the source before acting. Real-time parity across 5 stores.
          </p>
        </div>
        <a href={csvUrl} download className="btn-primary" style={{ textDecoration: 'none' }}>
          ⬇ Export Matrix CSV
        </a>
      </div>

      {/* 4 Top Metric Cards */}
      <div className="stats-grid-4">
        <div className="stat-card">
          <div className="stat-number">1,592</div>
          <div className="stat-label">Lazy Bean Catalog</div>
          <div className="stat-sub">Tracked reference items</div>
        </div>

        <div className="stat-card">
          <div className="stat-number">{rows.length}</div>
          <div className="stat-label">Matched Products</div>
          <div className="stat-sub">Verified comparable pairs</div>
        </div>

        <div className="stat-card">
          <div className="stat-number">14,472</div>
          <div className="stat-label">Competitor Products</div>
          <div className="stat-sub">Across 5 monitored stores</div>
        </div>

        <div className="stat-card">
          <div className="stat-number">{brands.length}</div>
          <div className="stat-label">Brands Monitored</div>
          <div className="stat-sub">In coffee & accessories</div>
        </div>
      </div>

      {/* Matrix Container */}
      <div className="matrix-container">
        {/* Filters Bar */}
        <div className="matrix-filters-bar">
          <input
            type="text"
            className="filter-input"
            placeholder="Search product, brand, seller..."
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
          />

          <select
            className="filter-select"
            value={selectedCategory}
            onChange={(e) => {
              setSelectedCategory(e.target.value);
              setPage(1);
            }}
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
            onChange={(e) => {
              setSelectedBrand(e.target.value);
              setPage(1);
            }}
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
            onChange={(e) => {
              setSelectedPosition(e.target.value);
              setPage(1);
            }}
          >
            <option value="ALL">All Positions</option>
            <option value="cheapest">Cheapest (Best Price)</option>
            <option value="within 5%">Within 5%</option>
            <option value="more expensive">More Expensive</option>
            <option value="lazyBeanOutOfStock">Lazy Bean Out of Stock</option>
          </select>

          <select
            className="filter-select"
            value={pageSize}
            onChange={(e) => {
              setPageSize(Number(e.target.value));
              setPage(1);
            }}
          >
            <option value="25">Show 25 / page</option>
            <option value="50">Show 50 / page</option>
            <option value="100">Show 100 / page</option>
          </select>
        </div>

        {/* Pager Bar */}
        <div className="matrix-pager">
          <div>
            Showing <strong>{totalFiltered > 0 ? startIndex + 1 : 0}</strong>–
            <strong>{Math.min(startIndex + pageSize, totalFiltered)}</strong> of{' '}
            <strong>{totalFiltered}</strong> products
          </div>
          <div className="pager-buttons">
            <button
              type="button"
              className="pager-btn"
              disabled={currentPage <= 1}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
            >
              ‹ Previous
            </button>
            <span>
              Page {currentPage} of {totalPages}
            </span>
            <button
              type="button"
              className="pager-btn"
              disabled={currentPage >= totalPages}
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            >
              Next ›
            </button>
          </div>
        </div>

        {/* Matrix Table */}
        <div className="matrix-table-wrap">
          {isLoading ? (
            <div style={{ padding: '40px', textAlign: 'center', color: 'var(--muted)' }}>
              Loading price matrix...
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
                        <a
                          href={row.lazyBeanUrl}
                          target="_blank"
                          rel="noreferrer"
                          className="product-title-link"
                          title={row.lazyBeanTitle}
                        >
                          {row.lazyBeanTitle} ↗
                        </a>
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
                      const cell = row.competitorPrices?.[col.key];
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
                            <a
                              href={cell.competitorUrl}
                              target="_blank"
                              rel="noreferrer"
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
                            <div className="obs-date">{formatObsDate(cell.observedAt)}</div>
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
