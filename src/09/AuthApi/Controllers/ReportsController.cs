using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthApi.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Policy = "ReadReports")]
public class ReportsController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new[] { "月次レポート" });

    [HttpGet("audit")]
    [Authorize(Roles = "Admin,Auditor")]
    public IActionResult Audit() => Ok(new { message = "監査用情報" });
}
