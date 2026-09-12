using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ApiProgram = global::Program;

namespace AuthApi.Tests;

public sealed class ApiFactory : WebApplicationFactory<ApiProgram>
{
    public const string Issuer = "https://identity.example.com";
    public const string Audience = "reports-api";
    private readonly RSA signingRsa = RSA.Create(2048);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureTestServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(NewlyAddedController).Assembly);
            services.PostConfigure<JwtBearerOptions>("Bearer", options =>
            {
                var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
                metadata.SigningKeys.Add(new RsaSecurityKey(signingRsa.ExportParameters(false)) { KeyId = "ephemeral" });
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                options.Backchannel = new HttpClient(new RejectNetworkHandler());
            });
        });
    }

    public string Token(IEnumerable<Claim>? claims = null, string? invalid = null)
    {
        using var otherRsa = RSA.Create(2048);
        var key = new RsaSecurityKey(invalid == "signature" ? otherRsa : signingRsa) { KeyId = "ephemeral" };
        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken(
            issuer: invalid == "issuer" ? "https://untrusted.example.test" : Issuer,
            audience: invalid == "audience" ? "other-api" : Audience,
            claims: new[] { new Claim("sub", "reader-id"), new Claim("name", "Test Reader") }
                .Concat(claims ?? []),
            notBefore: invalid == "future" ? now.AddHours(1) : now.AddHours(-2),
            expires: invalid == "expired" ? now.AddHours(-1) : now.AddHours(2),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    public HttpClient NewClient(string? token = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        signingRsa.Dispose();
    }

    private sealed class RejectNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("External JWT metadata calls are prohibited in these tests.");
    }
}

// ドキュメント外: テスト用に追加（本体に認可属性を追加せず FallbackPolicy を検証）
[Route("/new-endpoint")]
public sealed class NewlyAddedController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { message = "new endpoint" });
}
