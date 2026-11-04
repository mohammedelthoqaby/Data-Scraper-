import React, { useState, useEffect, useMemo } from 'react';
import { api, type ReviewMatchItem } from '../../api/client';

interface ReviewQueueViewProps {
  onRefreshReviewsCount?: () => void;
  onOpenProductDetail?: (productId: string, title?: string) => void;
}

export const ReviewQueueView: React.FC<ReviewQueueViewProps> = ({
  onRefreshReviewsCount,
  onOpenProductDetail,
}) => {
  const [reviews, setReviews] = useState<ReviewMatchItem[]>([]);
  const [totalCount, setTotalCount] = useState<number>(0);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [notesMap, setNotesMap] = useState<Record<string, string>>({});
  const [isLoading, setIsLoading] = useState(true);
  const [actionMessage, setActionMessage] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Filters
  const [search, setSearch] = useState('');
  const [selectedCompetitor, setSelectedCompetitor] = useState('all');

  const fetchReviews = async () => {
    setIsLoading(true);
    try {
      const res = await api.GET('/api/reviews', {
        params: { query: { status: 0, page: 1, pageSize: 50 } },
      });
      if (res.data) {
        setReviews(res.data.matches || []);
        setTotalCount(res.data.totalCount ?? 0);
        setSelectedIds(new Set());
      }
    } catch (err: any) {
      console.error('Failed to fetch reviews:', err);
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    fetchReviews();
  }, []);

  // Distinct competitor names
  const competitorNames = useMemo(() => {
    const names = new Set<string>();
    reviews.forEach((r) => {
      if (r.competitorProduct?.vendor) names.add(r.competitorProduct.vendor);
    });
    return Array.from(names).sort();
  }, [reviews]);

  // Filtered reviews
  const filteredReviews = useMemo(() => {
    return reviews.filter((r) => {
      if (search.trim()) {
        const q = search.toLowerCase();
        const refTitle = (r.referenceProduct?.title || '').toLowerCase();
        const compTitle = (r.competitorProduct?.title || '').toLowerCase();
        const basis = (r.basis || '').toLowerCase();
        if (!refTitle.includes(q) && !compTitle.includes(q) && !basis.includes(q)) {
          return false;
        }
      }
      return true;
    });
  }, [reviews, search]);

  // Selection handlers
  const handleToggleSelect = (id: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const handleSelectAll = () => {
    if (selectedIds.size === filteredReviews.length) {
      setSelectedIds(new Set());
    } else {
      setSelectedIds(new Set(filteredReviews.map((r) => r.id!).filter(Boolean)));
    }
  };

  // Single decision
  const handleSingleDecision = async (id: string, decision: 'approve' | 'reject') => {
    setIsSubmitting(true);
    setActionMessage(null);
    try {
      const notes = notesMap[id] || (decision === 'approve' ? 'Manually verified equivalence' : 'Different product or specification');
      await api.POST('/api/matches/{id}/decision', {
        params: { path: { id } },
        body: { decision, notes },
      });

      // Remove from list
      setReviews((prev) => prev.filter((r) => r.id !== id));
      setTotalCount((prev) => Math.max(0, prev - 1));
      setActionMessage(`Match successfully ${decision === 'approve' ? 'approved' : 'rejected'}.`);
      if (onRefreshReviewsCount) onRefreshReviewsCount();
    } catch (err: any) {
      alert(err?.message || 'Failed to submit decision');
    } finally {
      setIsSubmitting(false);
    }
  };

  // Bulk decision
  const handleBulkDecision = async (decision: 'approve' | 'reject') => {
    if (selectedIds.size === 0) return;
    const confirmed = window.confirm(
      `Are you sure you want to ${decision} ${selectedIds.size} selected matches?`
    );
    if (!confirmed) return;

    setIsSubmitting(true);
    setActionMessage(null);
    try {
      const decisions = Array.from(selectedIds).map((id) => ({
        id,
        decision,
        notes: notesMap[id] || `Bulk ${decision} verified by operator`,
      }));

      await api.POST('/api/matches/decisions', {
        body: { decisions },
      });

      // Remove processed items
      const selectedSet = new Set(selectedIds);
      setReviews((prev) => prev.filter((r) => !selectedSet.has(r.id!)));
      setTotalCount((prev) => Math.max(0, prev - selectedSet.size));
      setSelectedIds(new Set());
      setActionMessage(`Successfully bulk ${decision === 'approve' ? 'approved' : 'rejected'} ${decisions.length} matches.`);
      if (onRefreshReviewsCount) onRefreshReviewsCount();
    } catch (err: any) {
      alert(err?.message || 'Failed to submit bulk decisions');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="review-queue-view">
      {/* Page Heading */}
      <div className="page-heading">
        <div>
          <div className="eyebrow">OPERATOR VERIFICATION</div>
          <h1 className="view-title">Reviews Needing Attention</h1>
          <p className="subtitle">
            Confirm product matches and equivalent coffee items across monitored stores. Compare pack size, weight, and capsule format.
          </p>
        </div>
      </div>

      {actionMessage && (
        <div className="warning-banner" style={{ background: '#e7f3ec', borderColor: '#9bc9b1', color: '#1b5238', marginBottom: '16px' }}>
          ✓ {actionMessage}
        </div>
      )}

      {/* Bulk Action Bar */}
      <div className="review-bulk-bar">
        <div className="review-bulk-left">
          <input
            type="checkbox"
            id="selectAllReviews"
            checked={filteredReviews.length > 0 && selectedIds.size === filteredReviews.length}
            onChange={handleSelectAll}
            style={{ width: '18px', height: '18px', cursor: 'pointer' }}
          />
          <label htmlFor="selectAllReviews" style={{ fontWeight: 600, fontSize: '14px', cursor: 'pointer' }}>
            Select All ({filteredReviews.length})
          </label>
          {selectedIds.size > 0 && (
            <span className="badge good" style={{ marginLeft: '6px' }}>
              {selectedIds.size} selected
            </span>
          )}
        </div>

        <div className="review-bulk-actions">
          <button
            type="button"
            className="btn-xs approve"
            style={{ padding: '6px 14px', fontSize: '13px' }}
            disabled={selectedIds.size === 0 || isSubmitting}
            onClick={() => handleBulkDecision('approve')}
          >
            ✓ Bulk Approve Selected ({selectedIds.size})
          </button>
          <button
            type="button"
            className="btn-xs reject"
            style={{ padding: '6px 14px', fontSize: '13px' }}
            disabled={selectedIds.size === 0 || isSubmitting}
            onClick={() => handleBulkDecision('reject')}
          >
            ✕ Bulk Reject Selected ({selectedIds.size})
          </button>
        </div>
      </div>

      {/* Filters Bar */}
      <div className="matrix-filters-bar" style={{ marginBottom: '16px', borderRadius: '8px', border: '1px solid var(--line)' }}>
        <input
          type="search"
          placeholder="Filter candidate matches by product title, brand, or keywords..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          style={{ padding: '8px 12px', border: '1px solid var(--line)', borderRadius: '6px' }}
        />
        <div style={{ fontSize: '13px', color: 'var(--muted)', display: 'flex', alignItems: 'center', justifyContent: 'flex-end' }}>
          Showing <strong>{filteredReviews.length}</strong> of <strong>{totalCount}</strong> pending reviews
        </div>
      </div>

      {/* Reviews List */}
      {isLoading ? (
        <div className="panel" style={{ padding: '40px', textAlign: 'center', color: 'var(--muted)' }}>
          Loading review queue...
        </div>
      ) : filteredReviews.length === 0 ? (
        <div className="panel" style={{ padding: '40px', textAlign: 'center', color: 'var(--muted)' }}>
          All caught up! No candidate product matches needing review at this time.
        </div>
      ) : (
        <div className="reviews-list">
          {filteredReviews.map((item) => {
            const isSelected = selectedIds.has(item.id!);
            const scorePct = Math.round((item.score ?? 0.95) * 100);

            return (
              <div key={item.id} className={`review-card ${isSelected ? 'selected' : ''}`}>
                {/* Header */}
                <div className="review-card-header">
                  <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                    <input
                      type="checkbox"
                      checked={isSelected}
                      onChange={() => handleToggleSelect(item.id!)}
                      style={{ width: '18px', height: '18px', cursor: 'pointer' }}
                    />
                    <span className="review-score-badge">{scorePct}% Match Confidence</span>
                  </div>
                  <span style={{ fontSize: '12px', color: 'var(--muted)' }}>
                    Candidate ID: {item.id?.substring(0, 8)}...
                  </span>
                </div>

                {/* Match Basis */}
                <div className="review-basis-box">
                  <strong>Match Basis:</strong> {item.basis || 'Candidate equivalence matched by brand, packaging and weight.'}
                </div>

                {/* Side-by-side Attribute Comparison */}
                <div className="review-comparison-grid">
                  {/* Lazy Bean Reference Product */}
                  <div className="review-side">
                    <span className="review-side-label" style={{ color: 'var(--action)' }}>
                      Lazy Bean Product (Reference)
                    </span>
                    <a
                      href={item.referenceProduct?.canonicalUrl || '#'}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="review-side-title"
                      style={{ color: 'var(--action)', textDecoration: 'none' }}
                    >
                      {item.referenceProduct?.title} ↗
                    </a>
                    <div className="review-side-meta">
                      <span>Brand: <strong>{item.referenceProduct?.brand || '—'}</strong></span>
                      <span>Category: {item.referenceProduct?.category || 'Coffee'}</span>
                      <span>Weight: {item.referenceProduct?.weightGrams ? `${item.referenceProduct.weightGrams}g` : '—'}</span>
                    </div>
                    <div className="review-side-price">
                      {item.referenceProduct?.price?.toFixed(2)} EGP
                    </div>
                    {item.referenceProduct?.productId && onOpenProductDetail && (
                      <button
                        type="button"
                        className="btn-xs ghost"
                        style={{ alignSelf: 'flex-start', marginTop: '6px' }}
                        onClick={() => onOpenProductDetail(item.referenceProduct!.productId!, item.referenceProduct?.title ?? undefined)}
                      >
                        Inspect Price History
                      </button>
                    )}
                  </div>

                  {/* Competitor Product */}
                  <div className="review-side">
                    <span className="review-side-label">
                      Competitor Product (Candidate)
                    </span>
                    <a
                      href={item.competitorProduct?.canonicalUrl || '#'}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="review-side-title"
                    >
                      {item.competitorProduct?.title} ↗
                    </a>
                    <div className="review-side-meta">
                      <span>Vendor: <strong>{item.competitorProduct?.vendor || 'Competitor Store'}</strong></span>
                      <span>Brand: <strong>{item.competitorProduct?.brand || '—'}</strong></span>
                      <span>Category: {item.competitorProduct?.category || 'Coffee'}</span>
                      <span>Weight: {item.competitorProduct?.weightGrams ? `${item.competitorProduct.weightGrams}g` : '—'}</span>
                    </div>
                    <div className="review-side-price">
                      {item.competitorProduct?.price?.toFixed(2)} EGP
                    </div>
                  </div>
                </div>

                {/* Action Row */}
                <div className="review-actions-row">
                  <input
                    type="text"
                    className="review-notes-input"
                    placeholder="Verification notes (e.g. Verified identical 1kg whole beans)..."
                    value={notesMap[item.id!] || ''}
                    onChange={(e) =>
                      setNotesMap((prev) => ({ ...prev, [item.id!]: e.target.value }))
                    }
                  />

                  <div className="review-decision-btns">
                    <button
                      type="button"
                      className="btn-xs approve"
                      style={{ padding: '6px 14px', fontSize: '13px' }}
                      disabled={isSubmitting}
                      onClick={() => handleSingleDecision(item.id!, 'approve')}
                    >
                      ✓ Confirm Same Product
                    </button>
                    <button
                      type="button"
                      className="btn-xs reject"
                      style={{ padding: '6px 14px', fontSize: '13px' }}
                      disabled={isSubmitting}
                      onClick={() => handleSingleDecision(item.id!, 'reject')}
                    >
                      ✕ Not The Same
                    </button>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};
