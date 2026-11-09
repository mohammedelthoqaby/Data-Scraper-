using LazyBean.Application.Collector;
using LazyBean.Domain.Entities;
using Microsoft.AspNetCore.Authorization;

namespace LazyBean.Api.Auth;

public static class CollectorPolicies
{
    public const string AdsIngest = "collector:ads-ingest";
    public const string StoreIngest = "collector:store-ingest";
    public const string AnyDevice = "collector:any-device";

    public static void Register(AuthorizationOptions options)
    {
        options.AddPolicy(AdsIngest, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(CollectorClaims.Scope, CollectorDevice.AdsIngestScope));

        options.AddPolicy(StoreIngest, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(CollectorClaims.Scope, CollectorDevice.StoreIngestScope));

        options.AddPolicy(AnyDevice, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.HasClaim(claim => claim.Type == CollectorClaims.Scope)));
    }
}
