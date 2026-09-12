using AuthApi;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

var authentication = builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

if (builder.Environment.IsDevelopment())
{
    authentication.AddJwtBearer();
}
else
{
    var authority = builder.Configuration["Authentication:Authority"];
    var audience = builder.Configuration["Authentication:Audience"];
    if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(audience))
    {
        throw new InvalidOperationException(
            "Authentication:Authority と Authentication:Audience が必要です。");
    }

    authentication.AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.Audience = audience;
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "roles";
    });
}

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build())
    .AddPolicy("ReadReports", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permission", "reports.read"))
    .AddPolicy("AdminOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("Admin"));

builder.Services.AddControllers();
builder.Services.AddSingleton<IAuthorizationHandler, ScopeHandler>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ReadScopedReports", policy => policy
        .RequireAuthenticatedUser()
        .AddRequirements(new ScopeRequirement("reports.read")));

var app = builder.Build();

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .AllowAnonymous();
app.MapGet("/me", (ClaimsPrincipal user) =>
    Results.Ok(new { name = user.Identity?.Name }))
    .RequireAuthorization();
app.MapGet("/reports", () => Results.Ok(new[] { "月次レポート" }))
    .RequireAuthorization("ReadReports");
app.MapGet("/admin", () => Results.Ok(new { message = "管理者向け情報" }))
    .RequireAuthorization("AdminOnly");
app.MapControllers();
app.MapGet("/scoped-reports", () => Results.Ok(new[] { "月次レポート" }))
    .RequireAuthorization("ReadScopedReports");

app.Run();

// ドキュメント外: テスト用に追加
public partial class Program { }
