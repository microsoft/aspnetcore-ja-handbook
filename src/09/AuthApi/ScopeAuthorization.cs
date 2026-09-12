using Microsoft.AspNetCore.Authorization;

namespace AuthApi;

public sealed record ScopeRequirement(string Scope) : IAuthorizationRequirement;

public sealed class ScopeHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeRequirement requirement)
    {
        var scopes = context.User.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries));

        if (scopes.Contains(requirement.Scope, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
