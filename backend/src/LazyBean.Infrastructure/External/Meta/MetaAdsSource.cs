using LazyBean.Application.Ads.Models;
using LazyBean.Application.Ads.Ports;
using LazyBean.Application.Collector.Exceptions;

namespace LazyBean.Infrastructure.External.Meta;

// Meta's ad library behind the application's IMetaAdsSource: one MetaAdsSession per page, the search requests translated to the
// session's own shape, the session's failures translated to the collector exceptions (same messages, so the same classification),
// and the ads read into the application model.
public sealed class MetaAdsSource(MetaAdsOptions options, HttpMessageHandler? handler = null) : IMetaAdsSource
{
    public IMetaSearchSession OpenSession() => new SearchSession(new MetaAdsSession(options, handler));

    private sealed class SearchSession(MetaAdsSession session) : IMetaSearchSession
    {
        public async Task<MetaSearchStepResult> SearchAsync(MetaSearchStepRequest request, CancellationToken ct)
        {
            MetaSearchResponse response;
            try
            {
                response = await session.SearchAdsAsync(
                    new MetaSearchRequest(
                        Country: request.Country,
                        ActiveStatus: request.ActiveStatus,
                        PageIds: request.PageIds,
                        First: request.First,
                        SessionId: request.SessionId,
                        CollationToken: request.CollationToken,
                        Cursor: request.Cursor,
                        SearchType: request.SearchType),
                    ct);
            }
            catch (MetaRateLimitException ex)
            {
                throw new CollectorRateLimitException(ex.Message);
            }
            catch (MetaAuthenticationException ex)
            {
                throw new CollectorAuthenticationException(ex.Message);
            }
            catch (MetaSessionExpiredException ex)
            {
                throw new CollectorSessionExpiredException(ex.Message);
            }
            catch (MetaTransportException ex)
            {
                throw new CollectorTransportException(ex.Message, ex);
            }
            catch (MetaAdsException ex)
            {
                throw new CollectorSourceException(ex.Message, ex);
            }

            var ads = response.Ads.Select(MetaAdParser.Parse).ToList();
            return new MetaSearchStepResult(ads, response.NextCursor, response.RateLimited, response.SessionExpired, response.Error);
        }

        public void Dispose() => session.Dispose();
    }
}
