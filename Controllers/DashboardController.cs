
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CancerCareHub.Controllers;

[Authorize]
public class DashboardController : Controller
{
    [Authorize(Roles = "Patient")]
    public IActionResult Patient()
    {
        return View();
    }

    [Authorize(Roles = "Doctor")]
    public IActionResult Doctor()
    {
        return View();
    }

    [Authorize(Roles = "Admin")]
    public IActionResult Admin()
    {
        return View();
    }
}
