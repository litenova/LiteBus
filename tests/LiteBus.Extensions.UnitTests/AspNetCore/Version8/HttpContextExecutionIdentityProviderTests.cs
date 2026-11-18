using System.Security.Claims;
using LiteBus.Extensions.AspNetCore;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Extensions.UnitTests.AspNetCore.Version8;

public sealed class HttpContextExecutionIdentityProviderTests
{
    [Fact]
    public void GetCurrentIdentity_WithTenantClaim_ReturnsPrincipalAndTenant()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                    new Claim("tenant_id", "tenant-a")
                ],
                "Test"));
        var provider = CreateProvider(principal);

        var identity = provider.GetCurrentIdentity();

        Assert.Same(principal, identity.Principal);
        Assert.Equal("tenant-a", identity.TenantId);
        Assert.True(identity.HasTenant);
    }

    [Fact]
    public void GetCurrentIdentity_WithoutTenantClaim_ReturnsPrincipalWithoutTenant()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "Test"));
        var provider = CreateProvider(principal);

        var identity = provider.GetCurrentIdentity();

        Assert.Same(principal, identity.Principal);
        Assert.Null(identity.TenantId);
        Assert.False(identity.HasTenant);
    }

    [Fact]
    public void GetCurrentIdentity_WithCustomTenantClaimType_ReadsNamedClaim()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                    new Claim("tenant_id", "tenant-a"),
                    new Claim("custom_tenant", "tenant-b")
                ],
                "Test"));
        var provider = CreateProvider(principal, "custom_tenant");

        var identity = provider.GetCurrentIdentity();

        Assert.Equal("tenant-b", identity.TenantId);
    }

    [Fact]
    public void GetCurrentIdentity_WithoutHttpContext_ReturnsAnonymous()
    {
        var accessor = new HttpContextAccessor();
        var provider = new HttpContextExecutionIdentityProvider(accessor, new ExecutionIdentityOptions());

        var identity = provider.GetCurrentIdentity();

        Assert.Equal(ExecutionIdentity.Anonymous, identity);
    }

    [Fact]
    public void AddLiteBusExecutionIdentity_UsesTenantIdClaimByDefault()
    {
        var services = new ServiceCollection();
        services.AddLiteBusExecutionIdentity();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<ExecutionIdentityOptions>();

        Assert.Equal("tenant_id", options.TenantClaimType);
    }

    [Fact]
    public void AddLiteBusExecutionIdentity_HonoursConfiguredTenantClaimType()
    {
        var services = new ServiceCollection();
        services.AddLiteBusExecutionIdentity(options => options.TenantClaimType = "tid");
        using var provider = services.BuildServiceProvider();

        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim("tid", "tenant-c")], "Test"))
        };

        using var scope = provider.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IExecutionIdentityProvider>().GetCurrentIdentity();

        Assert.Equal("tenant-c", identity.TenantId);
        Assert.NotNull(identity.Principal);
    }

    private static HttpContextExecutionIdentityProvider CreateProvider(
        ClaimsPrincipal principal,
        string tenantClaimType = "tenant_id")
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        return new HttpContextExecutionIdentityProvider(
            accessor,
            new ExecutionIdentityOptions { TenantClaimType = tenantClaimType });
    }
}
