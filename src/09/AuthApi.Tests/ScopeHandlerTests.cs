using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace AuthApi.Tests;

public sealed class ScopeHandlerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("reports.read", true)]
    [InlineData("reports.write reports.read", true)]
    [InlineData("  reports.read   reports.write ", true)]
    [InlineData("reports.read.all", false)]
    [InlineData("prefix.reports.read", false)]
    [InlineData("Reports.Read", false)]
    [InlineData("reports.read\treports.write", false)]
    [InlineData("reports.read,reports.write", false)]
    public async Task HandleRequirement_ScopeBoundary_UsesOrdinalWholeSpaceSeparatedValues(
        string? value, bool expected)
    {
        var claims = value is null ? Array.Empty<Claim>() : [new Claim("scope", value)];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var requirement = new ScopeRequirement("reports.read");
        var context = new AuthorizationHandlerContext([requirement], principal, null);

        await new ScopeHandler().HandleAsync(context);

        Assert.Equal(expected, context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    [Fact]
    public async Task HandleRequirement_MultipleScopeClaims_AcceptsExactValueInAnyClaim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("scope", "reports.write"), new Claim("scope", "other reports.read")], "test"));
        var requirement = new ScopeRequirement("reports.read");
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        await new ScopeHandler().HandleAsync(context);
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirement_ScpInsteadOfScope_DoesNotGrantRequirement()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("scp", "reports.read")], "test"));
        var context = new AuthorizationHandlerContext([new ScopeRequirement("reports.read")], principal, null);
        await new ScopeHandler().HandleAsync(context);
        Assert.False(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ReadScopedReports_ScopePresent_AlsoRequiresAuthenticatedIdentity(
        bool authenticated, bool expected)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("scope", "reports.read")], authenticated ? "test" : null));
        using var scope = factory.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(principal, null, "ReadScopedReports");

        Assert.Equal(expected, result.Succeeded);
    }
}
