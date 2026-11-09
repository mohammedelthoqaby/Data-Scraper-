using LazyBean.Application.Ads.Common;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

// The AI-read reply: Gemini's envelope around the JSON the prompt asks for.
public class CreativeImageReplyParserTests
{
    [Fact]
    public void Parse_ReadsTheNameAndPriceFromTheEnvelope()
    {
        const string body = """
            {"candidates":[{"content":{"parts":[{"text":"{\"productName\":\"Lazy Bean Espresso 250g\",\"priceEgp\":245}"}]}}]}
            """;

        var reading = CreativeImageReplyParser.Parse(body);

        Assert.Equal("Lazy Bean Espresso 250g", reading.ProductName);
        Assert.Equal(245m, reading.Price);
    }

    [Fact]
    public void Parse_AcceptsACodeFenceAndAPriceWrittenAsText()
    {
        const string body = """
            {"candidates":[{"content":{"parts":[{"text":"```json\n{\"productName\":\"Brasile 250g\",\"priceEgp\":\"1,250.50\"}\n```"}]}}]}
            """;

        var reading = CreativeImageReplyParser.Parse(body);

        Assert.Equal("Brasile 250g", reading.ProductName);
        Assert.Equal(1250.50m, reading.Price);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("5000000")]
    public void Parse_AnImplausiblePrice_IsDropped(string price)
    {
        var body = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"productName\\\":\\\"Espresso\\\",\\\"priceEgp\\\":" + price + "}\"}]}}]}";

        Assert.Null(CreativeImageReplyParser.Parse(body).Price);
    }

    [Fact]
    public void Parse_ANullNameIsNotAName()
    {
        const string body = """
            {"candidates":[{"content":{"parts":[{"text":"{\"productName\":\"null\",\"priceEgp\":100}"}]}}]}
            """;

        var reading = CreativeImageReplyParser.Parse(body);

        Assert.Null(reading.ProductName);
        Assert.Equal(100m, reading.Price);
    }

    [Fact]
    public void Parse_AnEnvelopeWithNoText_GivesAnEmptyReading()
    {
        var reading = CreativeImageReplyParser.Parse("""{"candidates":[]}""");

        Assert.Null(reading.ProductName);
        Assert.Null(reading.Price);
    }

    [Fact]
    public void Parse_InvalidJson_GivesAnEmptyReadingInsteadOfThrowing()
    {
        var reading = CreativeImageReplyParser.Parse("not json at all");

        Assert.Null(reading.ProductName);
        Assert.Null(reading.Price);
    }
}
