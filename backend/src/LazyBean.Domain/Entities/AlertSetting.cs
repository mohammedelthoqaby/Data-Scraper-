using LazyBean.Domain.Common;

namespace LazyBean.Domain.Entities;

public class AlertSetting : BaseEntity, IAggregateRoot
{
    public decimal AdMoreExpensiveGapPercent { get; set; } = 10.0m;
    public decimal PriceDropPercent { get; set; } = 10.0m;
    public decimal LazyBeanAboveCheapestPercent { get; set; } = 15.0m;
    public int OutOfStockCompetitorCount { get; set; } = 2;
    public decimal MarginRoomBelowCheapestPercent { get; set; } = 15.0m;
    public int UncarriedBrandCompetitorCount { get; set; } = 2;
    public int BigCampaignDiscountCount { get; set; } = 10;
    public int ProlongedGapDays { get; set; } = 14;
    public decimal MaterialChangePercentPoints { get; set; } = 5.0m;
}
