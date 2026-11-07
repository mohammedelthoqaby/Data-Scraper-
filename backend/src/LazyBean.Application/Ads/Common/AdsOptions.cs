namespace LazyBean.Application.Ads.Common;

// Settings of the Ads page and its summary. Bound from the "Ads" configuration section.
public sealed class AdsOptions
{
    // A Google creative is Active only while it was shown within this many days of its last sighting.
    public int GoogleActiveWindowDays { get; set; } = 30;
}
