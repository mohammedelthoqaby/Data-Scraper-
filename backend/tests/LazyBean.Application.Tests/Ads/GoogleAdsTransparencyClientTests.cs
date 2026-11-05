using System.Net;
using LazyBean.Application.Ads.Exceptions;
using LazyBean.Infrastructure.Ads;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

public class GoogleAdsTransparencyClientTests
{
    private class TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handlerFunc(request));
        }
    }

    [Fact]
    public async Task SearchAdvertiserIdAsync_ReturnsAdvertiserId_WhenSuggestionPresent()
    {
        // Recorded response fixture from Google Ads Transparency SearchSuggestions RPC
        const string fixtureJson = """
        {
          "1": [
            {
              "1": {
                "1": "CAFELAX",
                "2": "AR01064852753525768193",
                "3": "EG",
                "4": {
                  "2": {
                    "2": 52
                  }
                }
              }
            },
            {
              "2": {
                "1": "cafelax.com"
              }
            }
          ]
        }
        """;

        var handler = new TestHttpMessageHandler(req =>
        {
            Assert.Contains("/anji/_/rpc/SearchService/SearchSuggestions", req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fixtureJson)
            };
        });

        var client = new HttpClient(handler);
        var transparencyClient = new GoogleAdsTransparencyClient(client, NullLogger<GoogleAdsTransparencyClient>.Instance);

        var advId = await transparencyClient.SearchAdvertiserIdAsync("cafelax");

        Assert.Equal("AR01064852753525768193", advId);
    }

    [Fact]
    public async Task SearchAdvertiserIdAsync_ThrowsGoogleAdsBlockedException_On429()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("Too many requests. Rate limit exceeded.")
        });

        var client = new HttpClient(handler);
        var transparencyClient = new GoogleAdsTransparencyClient(client, NullLogger<GoogleAdsTransparencyClient>.Instance);

        await Assert.ThrowsAsync<GoogleAdsBlockedException>(() =>
            transparencyClient.SearchAdvertiserIdAsync("cafelax"));
    }

    [Fact]
    public async Task SearchAdvertiserIdAsync_ThrowsGoogleAdsBlockedException_OnCaptcha()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body><div id='recaptcha'>Please solve the captcha</div></body></html>")
        });

        var client = new HttpClient(handler);
        var transparencyClient = new GoogleAdsTransparencyClient(client, NullLogger<GoogleAdsTransparencyClient>.Instance);

        await Assert.ThrowsAsync<GoogleAdsBlockedException>(() =>
            transparencyClient.SearchAdvertiserIdAsync("cafelax"));
    }

    [Fact]
    public async Task SearchCreativesAsync_ParsesCreatives_Dates_AndImages()
    {
        // Recorded response fixture from Google Ads Transparency SearchCreatives RPC
        const string fixtureJson = """
        {
          "1": [
            {
              "1": "AR01064852753525768193",
              "2": "CR11098911077365186561",
              "3": {
                "3": {
                  "2": "<img src=\"https://tpc.googlesyndication.com/archive/simgad/12684947687542842870\" height=\"448\" width=\"348\">"
                },
                "5": true
              },
              "4": 2,
              "6": {
                "1": "1782033909",
                "2": 369323000
              },
              "7": {
                "1": "1793765949",
                "2": 27070000
              },
              "12": "CAFELAX",
              "13": 135
            }
          ],
          "2": "next_page_token_abc_123"
        }
        """;

        var handler = new TestHttpMessageHandler(req =>
        {
            Assert.Contains("/anji/_/rpc/SearchService/SearchCreatives", req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fixtureJson)
            };
        });

        var client = new HttpClient(handler);
        var transparencyClient = new GoogleAdsTransparencyClient(client, NullLogger<GoogleAdsTransparencyClient>.Instance);

        var result = await transparencyClient.SearchCreativesAsync("AR01064852753525768193");

        Assert.NotNull(result);
        Assert.Single(result.Creatives);
        Assert.Equal("next_page_token_abc_123", result.NextPageToken);

        var creative = result.Creatives[0];
        Assert.Equal("CR11098911077365186561", creative.ExternalAdId);
        Assert.Equal("AR01064852753525768193", creative.AdvertiserId);
        Assert.Equal("CAFELAX", creative.AdvertiserName);
        Assert.Equal("image", creative.FormatName);
        Assert.Equal("https://tpc.googlesyndication.com/archive/simgad/12684947687542842870", creative.ImageUrl);
        Assert.Equal("https://adstransparency.google.com/advertiser/AR01064852753525768193/creative/CR11098911077365186561?region=EG", creative.SnapshotUrl);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1782033909), creative.FirstSeen);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793765949), creative.LastSeen);
    }

    [Fact]
    public async Task SearchCandidatesAsync_ParsesCandidatesCorrectly_WhenSuggestionsPresent()
    {
        const string fixtureJson = """
        {
          "1": [
            {
              "1": {
                "1": "CAFELAX",
                "2": "AR01064852753525768193",
                "3": "EG",
                "4": {
                  "2": {
                    "2": 52
                  }
                }
              }
            },
            {
              "2": {
                "1": "cafelax.com"
              }
            }
          ]
        }
        """;

        var handler = new TestHttpMessageHandler(req =>
        {
            Assert.Contains("/anji/_/rpc/SearchService/SearchSuggestions", req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fixtureJson)
            };
        });

        var client = new HttpClient(handler);
        var transparencyClient = new GoogleAdsTransparencyClient(client, NullLogger<GoogleAdsTransparencyClient>.Instance);

        var candidates = await transparencyClient.SearchCandidatesAsync("cafelax.com");

        Assert.NotNull(candidates);
        Assert.Single(candidates);
        var candidate = candidates[0];
        Assert.Equal("AR01064852753525768193", candidate.AdvertiserId);
        Assert.Equal("CAFELAX", candidate.Name);
        Assert.Equal("EG", candidate.Region);
        Assert.Equal(52, candidate.ApproxAdCount);
    }
}
