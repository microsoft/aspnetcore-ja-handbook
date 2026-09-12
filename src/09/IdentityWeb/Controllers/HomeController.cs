using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using IdentityWeb.Models;

namespace IdentityWeb.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    [Microsoft.AspNetCore.Authorization.Authorize]
    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
