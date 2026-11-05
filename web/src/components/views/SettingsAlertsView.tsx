import React, { useState, useEffect } from 'react';
import { fetchAlertSettings, updateAlertSettings, type AlertSettings } from '../../api/alerts';

export const SettingsAlertsView: React.FC = () => {
  const [settings, setSettings] = useState<AlertSettings>({
    adMoreExpensiveGapPercent: 10,
    priceDropPercent: 10,
    lazyBeanAboveCheapestPercent: 15,
    outOfStockCompetitorCount: 2,
    marginRoomBelowCheapestPercent: 15,
    uncarriedBrandCompetitorCount: 2,
    bigCampaignDiscountCount: 10,
    prolongedGapDays: 14,
    materialChangePercentPoints: 5,
  });

  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [isSaving, setIsSaving] = useState<boolean>(false);
  const [saveSuccess, setSaveSuccess] = useState<boolean>(false);

  useEffect(() => {
    fetchAlertSettings()
      .then((data) => {
        setSettings(data);
        setIsLoading(false);
      })
      .catch((err) => {
        console.error('Failed to fetch alert settings:', err);
        setIsLoading(false);
      });
  }, []);

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSaving(true);
    setSaveSuccess(false);
    try {
      const updated = await updateAlertSettings(settings);
      setSettings(updated);
      setSaveSuccess(true);
      setTimeout(() => setSaveSuccess(false), 3000);
    } catch (err) {
      console.error('Failed to update alert settings:', err);
    } finally {
      setIsSaving(false);
    }
  };

  if (isLoading) {
    return (
      <div className="panel" style={{ padding: '30px', textAlign: 'center' }}>
        <div className="spinner" />
        <p>Loading alert configuration...</p>
      </div>
    );
  }

  return (
    <div className="settings-alerts-view">
      <div className="view-header">
        <div>
          <div className="eyebrow">CONFIGURATION & POLICY</div>
          <h1>Alert Thresholds & Engine Rules</h1>
          <p className="subtitle">
            Configure sensitivity thresholds for the Manager Action Center. Changes immediately govern alert generation and re-activation triggers.
          </p>
        </div>
      </div>

      <div className="panel" style={{ maxWidth: '820px' }}>
        <form onSubmit={handleSave}>
          <div className="form-section">
            <h3 style={{ fontSize: '15px', color: 'var(--brand)', marginBottom: '16px' }}>
              🚨 Urgent Alert Thresholds
            </h3>

            <div className="form-grid">
              <div className="form-group">
                <label>
                  Ad Threat Gap Threshold (%)
                  <span className="form-hint">Lazy Bean % more expensive while competitor runs active ads.</span>
                </label>
                <input
                  type="number"
                  step="1"
                  min="1"
                  max="100"
                  value={settings.adMoreExpensiveGapPercent}
                  onChange={(e) =>
                    setSettings({ ...settings, adMoreExpensiveGapPercent: parseFloat(e.target.value) || 0 })
                  }
                  required
                />
              </div>

              <div className="form-group">
                <label>
                  Competitor Price Drop (%)
                  <span className="form-hint">Minimum competitor price drop within 7 days to trigger alert.</span>
                </label>
                <input
                  type="number"
                  step="1"
                  min="1"
                  max="100"
                  value={settings.priceDropPercent}
                  onChange={(e) =>
                    setSettings({ ...settings, priceDropPercent: parseFloat(e.target.value) || 0 })
                  }
                  required
                />
              </div>

              <div className="form-group">
                <label>
                  Above Cheapest In-Stock Seller (%)
                  <span className="form-hint">Lazy Bean price % above cheapest competitor with verified stock.</span>
                </label>
                <input
                  type="number"
                  step="1"
                  min="1"
                  max="100"
                  value={settings.lazyBeanAboveCheapestPercent}
                  onChange={(e) =>
                    setSettings({ ...settings, lazyBeanAboveCheapestPercent: parseFloat(e.target.value) || 0 })
                  }
                  required
                />
              </div>

              <div className="form-group">
                <label>
                  Out-of-Stock Competitor Count
                  <span className="form-hint">Minimum in-stock competitors when Lazy Bean is out of stock.</span>
                </label>
                <input
                  type="number"
                  step="1"
                  min="1"
                  max="10"
                  value={settings.outOfStockCompetitorCount}
                  onChange={(e) =>
                    setSettings({ ...settings, outOfStockCompetitorCount: parseInt(e.target.value, 10) || 1 })
                  }
                  required
                />
              </div>
            </div>
          </div>

          <hr style={{ margin: '24px 0', borderColor: 'var(--line)' }} />

          <div className="form-section">
            <h3 style={{ fontSize: '15px', color: '#0d9488', marginBottom: '16px' }}>
              💡 Opportunity & Market Expansion Thresholds
            </h3>

            <div className="form-grid">
              <div className="form-group">
                <label>
                  Margin Room Below Cheapest (%)
                  <span className="form-hint">Lazy Bean % below competitor where room exists to raise price.</span>
                </label>
                <input
                  type="number"
                  step="1"
                  min="1"
                  max="100"
                  value={settings.marginRoomBelowCheapestPercent}
                  onChange={(e) =>
                    setSettings({ ...settings, marginRoomBelowCheapestPercent: parseFloat(e.target.value) || 0 })
                  }
                  required
                />
              </div>

              <div className="form-group">
                <label>
                  Uncarried Brand Competitor Count
                  <span className="form-hint">Number of competitors carrying a brand before suggesting onboarding.</span>
                </label>
                <input
                  type="number"
                  step="1"
                  min="1"
                  max="10"
                  value={settings.uncarriedBrandCompetitorCount}
                  onChange={(e) =>
                    setSettings({ ...settings, uncarriedBrandCompetitorCount: parseInt(e.target.value, 10) || 1 })
                  }
                  required
                />
              </div>
            </div>
          </div>

          <hr style={{ margin: '24px 0', borderColor: 'var(--line)' }} />

          <div className="form-section">
            <h3 style={{ fontSize: '15px', color: '#b45309', marginBottom: '16px' }}>
              👁️ Watch & Lifecycle Thresholds
            </h3>

            <div className="form-grid">
              <div className="form-group">
                <label>
                  Prolonged Gap Duration (Days)
                  <span className="form-hint">Consecutive days Lazy Bean has been more expensive.</span>
                </label>
                <input
                  type="number"
                  step="1"
                  min="3"
                  max="365"
                  value={settings.prolongedGapDays}
                  onChange={(e) =>
                    setSettings({ ...settings, prolongedGapDays: parseInt(e.target.value, 10) || 14 })
                  }
                  required
                />
              </div>

              <div className="form-group">
                <label>
                  Material Change Un-snooze Points (%)
                  <span className="form-hint">Point shift in gap % required to automatically wake a snoozed/dismissed alert.</span>
                </label>
                <input
                  type="number"
                  step="0.5"
                  min="1"
                  max="50"
                  value={settings.materialChangePercentPoints}
                  onChange={(e) =>
                    setSettings({ ...settings, materialChangePercentPoints: parseFloat(e.target.value) || 5 })
                  }
                  required
                />
              </div>

              <div className="form-group">
                <label>
                  Big Campaign Category Discounts
                  <span className="form-hint">Number of new discounts in one category within a week.</span>
                </label>
                <input
                  type="number"
                  step="1"
                  min="3"
                  max="100"
                  value={settings.bigCampaignDiscountCount}
                  onChange={(e) =>
                    setSettings({ ...settings, bigCampaignDiscountCount: parseInt(e.target.value, 10) || 10 })
                  }
                  required
                />
              </div>
            </div>
          </div>

          <div style={{ marginTop: '28px', display: 'flex', gap: '12px', alignItems: 'center' }}>
            <button type="submit" className="btn btn-primary" disabled={isSaving}>
              {isSaving ? 'Saving Policy...' : 'Save Alert Thresholds'}
            </button>
            {saveSuccess && (
              <span style={{ color: 'var(--brand)', fontWeight: 600, fontSize: '14px' }}>
                ✓ Threshold settings saved successfully.
              </span>
            )}
          </div>
        </form>
      </div>
    </div>
  );
};
