using LazyBean.Application.Ads.Common;
using LazyBean.Application.Ads.Dtos;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

// Normalization decides what reaches the ads table for a Google creative, so each storage rule is pinned here
// without a database. The integration tests then check the same rules end to end.
public class GoogleAdNormalizerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Emoji = char.ConvertFromUtf32(0x1F600);

    [Fact]
    public void ValidItem_IsAccepted_WithItsFieldsKept()
    {
        var first = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var item = new GoogleAdPayloadItem("CR1", "text", "CAFELAX", "Body", "https://snap", "https://link", first, Now, "{\"2\":\"CR1\"}");

        var result = GoogleAdNormalizer.Normalize(item, Now);

        Assert.Null(result.SkipReason);
        Assert.NotNull(result.Ad);
        Assert.Equal("CR1", result.Ad.ExternalAdId);
        Assert.Equal("CAFELAX", result.Ad.Title);
        Assert.Equal("Body", result.Ad.Body);
        Assert.Equal("https://link", result.Ad.LinkUrl);
        Assert.Equal("https://snap", result.Ad.SnapshotUrl);
        Assert.Equal(first, result.Ad.FirstSeen);
        Assert.Equal(Now, result.Ad.LastSeen);
        Assert.Equal("{\"2\":\"CR1\"}", result.Ad.RawJson);
    }

    [Fact]
    public void NullItem_IsSkipped()
    {
        var result = GoogleAdNormalizer.Normalize(null, Now);

        Assert.Null(result.Ad);
        Assert.Equal("The creative was empty.", result.SkipReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingId_IsSkipped(string? id)
    {
        var result = GoogleAdNormalizer.Normalize(Item(id!), Now);

        Assert.Null(result.Ad);
        Assert.Equal("The creative has no ID.", result.SkipReason);
    }

    [Fact]
    public void IdLongerThan200_IsSkipped()
    {
        var result = GoogleAdNormalizer.Normalize(Item(new string('9', 201)), Now);

        Assert.Null(result.Ad);
        Assert.Equal("The creative ID is longer than 200 characters.", result.SkipReason);
    }

    [Fact]
    public void Title_IsTrimmedTo500_WithoutSplittingASurrogatePair()
    {
        // 499 letters then one emoji (two UTF-16 units): the pair straddles the 500 limit.
        var title = new string('a', 499) + Emoji;

        var result = GoogleAdNormalizer.Normalize(Item("CR1", title: title), Now);

        Assert.Equal(new string('a', 499), result.Ad!.Title);
    }

    [Fact]
    public void Body_LongerThan4000_IsTrimmed()
    {
        var result = GoogleAdNormalizer.Normalize(Item("CR1", body: new string('ع', 5000)), Now);

        Assert.Equal(4000, result.Ad!.Body!.Length);
    }

    [Fact]
    public void Link_LongerThan2000_IsDropped_ButTheCreativeIsKept()
    {
        var item = new GoogleAdPayloadItem("CR1", "image", "T", null, new string('a', 2001), "https://ok.example/", null, null, "{}");

        var result = GoogleAdNormalizer.Normalize(item, Now);

        Assert.NotNull(result.Ad);
        Assert.Null(result.Ad.SnapshotUrl);
        Assert.Equal("https://ok.example/", result.Ad.LinkUrl);
    }

    [Fact]
    public void Dates_OutsideThePlausibleRange_AreDropped()
    {
        var item = new GoogleAdPayloadItem(
            "CR1", "text", "T", null, null, null,
            new DateTimeOffset(1999, 12, 31, 23, 59, 59, TimeSpan.Zero),
            Now.AddDays(2),
            "{}");

        var result = GoogleAdNormalizer.Normalize(item, Now);

        Assert.NotNull(result.Ad);
        Assert.Null(result.Ad.FirstSeen);
        Assert.Null(result.Ad.LastSeen);
    }

    [Fact]
    public void Clean_RemovesNulAndUnpairedSurrogates_AndKeepsValidPairs()
    {
        var value = "a\0b" + "\uD800" + "c" + Emoji;

        Assert.Equal("abc" + Emoji, GoogleAdNormalizer.Clean(value));
    }

    [Fact]
    public void Clean_OfBlankText_IsNull()
    {
        Assert.Null(GoogleAdNormalizer.Clean("  \t\n "));
    }

    [Theory]
    [InlineData("{\"a\":1}")]
    [InlineData("{\"a\":\"\\ud83d\\ude00\"}")] // a valid surrogate pair written as escapes
    public void RawJson_ThatParsesAndIsStorable_IsKept(string raw)
    {
        Assert.Equal(raw, GoogleAdNormalizer.CleanRawJson(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("{\"a\":\"\\u0000\"}")] // NUL escape: jsonb rejects it
    [InlineData("{\"a\":\"\\ud800\"}")] // unpaired high surrogate escape
    [InlineData("{\"a\":\"\\udc00\"}")] // unpaired low surrogate escape
    public void RawJson_ThatJsonbWouldReject_IsDropped(string? raw)
    {
        Assert.Null(GoogleAdNormalizer.CleanRawJson(raw));
    }

    [Fact]
    public void RawJson_WithARawNulCharacter_IsDropped()
    {
        Assert.Null(GoogleAdNormalizer.CleanRawJson("{\"a\":\"x\0y\"}"));
    }

    [Fact]
    public void Truncate_NeverLeavesAHalfSurrogatePair()
    {
        Assert.Equal("a", GoogleAdNormalizer.Truncate("a" + Emoji, 2));
        Assert.Equal("a" + Emoji, GoogleAdNormalizer.Truncate("a" + Emoji, 3));
    }

    private static GoogleAdPayloadItem Item(string id, string? title = "Title", string? body = null) =>
        new(id, "text", title, body, null, null, null, null, "{}");
}
