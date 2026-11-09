using LazyBean.Application.Ads.Common;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

// The three wrong links from the Ads page audit, plus the cases that must not link.
public class AdProductLinkerTests
{
    private static readonly Guid Lavazza = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Katzala = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Davidoff = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid MilkPitcher = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid Saquella = Guid.Parse("00000000-0000-0000-0000-000000000005");

    private static readonly IReadOnlyList<LinkableProduct> Catalog =
    [
        new(Lavazza, "Lavazza IL Mattino", "lavazza-il-mattino", "https://cafelax.com/products/lavazza-il-mattino"),
        new(Katzala, "Katzala Uganda", "katzala-uganda", "https://cafelax.com/products/katzala-uganda"),
        new(Davidoff, "Davidoff Rich Aroma", "davidoff-rich-aroma", "https://cafelax.com/products/davidoff-rich-aroma"),
        new(MilkPitcher, "Milk Pitcher", "milk-pitcher", "https://cafelax.com/products/milk-pitcher"),
        new(Saquella, "Saquella Crema Dolce", "saquella-crema-dolce", "https://cafelax.com/products/saquella-crema-dolce"),
    ];

    [Fact]
    public void LandingLinkWithAHandle_LinksTheNamedProduct()
    {
        var link = AdProductLinker.Link(
            new AdLinkInput("https://cafelax.com/products/katzala-uganda?variant=1", "Coffee offer", null),
            Catalog);

        Assert.Equal(Katzala, link.ProductId);
        Assert.Equal("handle", link.Method);
    }

    [Fact]
    public void LavazzaIlMattinoAd_LinksToLavazzaNotKatzala()
    {
        var link = AdProductLinker.Link(new AdLinkInput(null, "Lavazza IL Mattino", null), Catalog);

        Assert.Equal(Lavazza, link.ProductId);
        Assert.Equal("title", link.Method);
    }

    [Fact]
    public void DavidoffRichAromaAd_LinksToDavidoffNotTheMilkPitcher()
    {
        var link = AdProductLinker.Link(new AdLinkInput(null, "Davidoff Rich Aroma", "عرض خاص 300 جنيه"), Catalog);

        Assert.Equal(Davidoff, link.ProductId);
    }

    [Fact]
    public void SaquellaCremaDolceAd_LinksToSaquellaNotDavidoff()
    {
        var link = AdProductLinker.Link(new AdLinkInput(null, "Saquella Crema Dolce", null), Catalog);

        Assert.Equal(Saquella, link.ProductId);
    }

    [Fact]
    public void StoreRootLink_IsNotLinked()
    {
        var link = AdProductLinker.Link(new AdLinkInput("https://cafelax.com/", "Espresso machine deal", null), Catalog);

        Assert.Null(link.ProductId);
        Assert.Equal("none", link.Method);
    }

    [Fact]
    public void AdWithNoProductName_IsNotLinked()
    {
        var link = AdProductLinker.Link(new AdLinkInput(null, "Summer offer for everyone", null), Catalog);

        Assert.Null(link.ProductId);
    }

    [Fact]
    public void TwoProductsNamedEqually_AreNotLinked()
    {
        var duplicates = new List<LinkableProduct>
        {
            new(Lavazza, "Lavazza Gold", null, null),
            new(Katzala, "Lavazza Gold", null, null),
        };

        var link = AdProductLinker.Link(new AdLinkInput(null, "Lavazza Gold", null), duplicates);

        Assert.Null(link.ProductId);
    }

    [Fact]
    public void OnlyOneWordOfAProductName_IsNotEnough()
    {
        var link = AdProductLinker.Link(new AdLinkInput(null, "Davidoff", null), Catalog);

        Assert.Null(link.ProductId);
    }
}
