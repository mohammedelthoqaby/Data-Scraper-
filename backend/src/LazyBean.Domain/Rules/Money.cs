using System.Globalization;

namespace LazyBean.Domain.Rules;

// Money as stores send it: an integer count of minor units (for example piastres for EGP) and the number of digits after the
// decimal point. WooCommerce's Store API reports prices this way.
public static class Money
{
    public static decimal FromMinorUnits(string minorUnitsValue, int minorUnitDigits)
    {
        var minorUnits = decimal.Parse(minorUnitsValue, CultureInfo.InvariantCulture);
        return minorUnits / (decimal)Math.Pow(10, minorUnitDigits);
    }
}
