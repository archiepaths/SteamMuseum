using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SteamMuseum.Application;
using SteamMuseum.Infrastructure;

namespace SteamMuseum.Api;

public sealed class MobileLoginModel
{
    [Required, EmailAddress, MaxLength(256)] public string Email { get; set; } = "";
    [Required, MaxLength(256)] public string Password { get; set; } = "";
    public string ReturnUrl { get; set; } = "";
}
public sealed class MobilePasswordModel
{
    [Required, MaxLength(256)] public string CurrentPassword { get; set; } = "";
    [Required, MinLength(12), MaxLength(256)] public string NewPassword { get; set; } = "";
    [Compare(nameof(NewPassword))] public string ConfirmPassword { get; set; } = "";
    public string ReturnUrl { get; set; } = "";
}

// Small system-browser UI for the OAuth flow; no passwords are entered into the native app.
public sealed class MobileController(AccountService accounts, IConfiguration configuration) : Controller
{
    private bool ValidReturn(string? url) => MobileAuthentication.Enabled(configuration) &&
        url is not null && Url.IsLocalUrl(url) && url.StartsWith("/connect/authorize?", StringComparison.Ordinal);

    [AllowAnonymous, HttpGet("/mobile/login")]
    public IActionResult Login(string? returnUrl)
    {
        if (!ValidReturn(returnUrl)) return BadRequest("Start sign-in from the museum app.");
        return View(new MobileLoginModel { ReturnUrl = returnUrl! });
    }

    [AllowAnonymous, HttpPost("/mobile/login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromForm] MobileLoginModel model, CancellationToken ct)
    {
        if (!ValidReturn(model.ReturnUrl)) return BadRequest("Invalid sign-in return address.");
        if (ModelState.IsValid && await accounts.Login(model.Email, model.Password, ct)) return LocalRedirect(model.ReturnUrl);
        ModelState.AddModelError("", "Unable to sign in. Check your details or contact your administrator.");
        model.Password = "";
        ModelState.Remove(nameof(model.Password));
        return View(model);
    }

    [Authorize(Policy = "BrowserSession"), HttpGet("/mobile/password")]
    public IActionResult Password(string? returnUrl)
    {
        if (!ValidReturn(returnUrl)) return BadRequest("Start sign-in from the museum app.");
        return View(new MobilePasswordModel { ReturnUrl = returnUrl! });
    }

    [Authorize(Policy = "BrowserSession"), HttpPost("/mobile/password"), EnableRateLimiting("login")]
    public async Task<IActionResult> Password([FromForm] MobilePasswordModel model)
    {
        if (!ValidReturn(model.ReturnUrl)) return BadRequest("Invalid sign-in return address.");
        if (ModelState.IsValid)
        {
            try
            {
                await accounts.ChangePassword(User, model.CurrentPassword, model.NewPassword);
                return LocalRedirect(model.ReturnUrl);
            }
            catch (BusinessException e) { ModelState.AddModelError("", e.Message); }
        }
        model.CurrentPassword = model.NewPassword = model.ConfirmPassword = "";
        ModelState.Remove(nameof(model.CurrentPassword));
        ModelState.Remove(nameof(model.NewPassword));
        ModelState.Remove(nameof(model.ConfirmPassword));
        return View(model);
    }
}
