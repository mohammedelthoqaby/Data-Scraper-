namespace LazyBean.Application.Ads.Exceptions;

public class GoogleAdsBlockedException : Exception
{
    public GoogleAdsBlockedException(string message) : base(message)
    {
    }

    public GoogleAdsBlockedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
