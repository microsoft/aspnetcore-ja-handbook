using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OidcWeb.Controllers;

[Authorize]
public class AccountController : Controller
{
    [HttpGet]
    public IActionResult Profile() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout() => SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        CookieAuthenticationDefaults.AuthenticationScheme,
        OpenIdConnectDefaults.AuthenticationScheme);
}
