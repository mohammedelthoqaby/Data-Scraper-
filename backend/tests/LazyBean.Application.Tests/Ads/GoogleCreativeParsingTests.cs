using System.Net;
using System.Text;
using LazyBean.Infrastructure.External.Google;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

// Google answers with the fields of a creative in shapes the parser does not expect: a number where an ID is, a string
// where a date object is. One such creative must be skipped, not fail the whole advertiser.
public class GoogleCreativeParsingTests
{
    [Fact]
    public async Task MalformedCreative_IsSkipped_AndTheGoodOnesAreKept()
    {
        const string body = """
            {"1":[
              {"1":"AR01064852753525768193","2":"CRGOOD0001","4":1,"6":{"1":"1782033909"},"7":{"1":"1793950484"},"12":"CAFELAX","3":{"3":{"2":"<img src=\"https://tpc.googlesyndication.com/archive/simgad/1\">"}}},
              {"1":"AR01064852753525768193","2":12345,"4":"oops","6":"not an object","12":7},
              "a bare string where a creative belongs",
              {"1":"AR01064852753525768193","2":"CRGOOD0002","4":"2","6":{"1":12},"7":"x","12":"CAFELAX","3":5},
              {"1":"AR01064852753525768193","2":"CRGOOD0003","4":3,"12":"CAFELAX","3":{"1":{"4":"https://displayads-formats.googleusercontent.com/ads/preview/content.js?creativeId=3"}}}
            ]}
            """;

        var page = await new GoogleAdsTransparencyClient(
            new HttpClient(new FixedResponseHandler(body)),
            NullLogger<GoogleAdsTransparencyClient>.Instance).SearchCreativesAsync("AR01064852753525768193");

        var ids = page.Creatives.Select(c => c.ExternalAdId).ToArray();
        Assert.Equal(new[] { "CRGOOD0001", "CRGOOD0002", "CRGOOD0003" }, ids);
        Assert.Equal("text", page.Creatives[0].FormatName);
        Assert.Equal("video", page.Creatives[2].FormatName);
        Assert.Null(page.Creatives[1].FirstSeen);
    }

    [Fact]
    public async Task VideoCreative_KeepsItsPreviewUrl_WithEscapesDecoded()
    {
        const string body = """
            {"1":[
              {"1":"AR01064852753525768193","2":"CRVIDEO0001","4":3,"12":"CAFELAX","3":{"1":{"4":"https://displayads-formats.googleusercontent.com/ads/preview/content.js?client=ads-integrity&creativeId=1"}}}
            ]}
            """;

        var page = await new GoogleAdsTransparencyClient(
            new HttpClient(new FixedResponseHandler(body)),
            NullLogger<GoogleAdsTransparencyClient>.Instance).SearchCreativesAsync("AR01064852753525768193");

        var video = Assert.Single(page.Creatives);
        Assert.Equal(
            "https://displayads-formats.googleusercontent.com/ads/preview/content.js?client=ads-integrity&creativeId=1",
            video.ImageUrl);
    }

    private sealed class FixedResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
