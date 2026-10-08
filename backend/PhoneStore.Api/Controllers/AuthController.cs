using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PhoneStore.Api.DTOs.Auth;
using PhoneStore.Api.Services.Auth;

namespace PhoneStore.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(AuthService auth, IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf"), AllowAnonymous, EnableRateLimiting("csrf")]
    public IActionResult Csrf() => Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken! });

    [HttpGet("session"), Authorize, DisableRateLimiting]
    public async Task<IActionResult> Session() => Ok(await auth.SessionAsync(User));

    [HttpPost("register"), AllowAnonymous]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken) => Reply(await auth.RegisterAsync(request, cancellationToken), 202);

    [HttpPost("verify-email"), AllowAnonymous]
    public async Task<IActionResult> Verify(VerifyEmailRequest request) => Reply(await auth.VerifyAsync(request));

    [HttpPost("login"), AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken) => Reply(await auth.LoginAsync(request, cancellationToken));

    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout() { await auth.LogoutAsync(User); return NoContent(); }

    [HttpPost("forgot-password"), AllowAnonymous]
    public async Task<IActionResult> Forgot(EmailRequest request, CancellationToken cancellationToken) => Reply(await auth.SendForEmailAsync(request, true, cancellationToken), 202);

    [HttpPost("resend-verification"), AllowAnonymous]
    public async Task<IActionResult> Resend(EmailRequest request, CancellationToken cancellationToken) => Reply(await auth.SendForEmailAsync(request, false, cancellationToken), 202);

    [HttpPost("reset-password"), AllowAnonymous]
    public async Task<IActionResult> Reset(ResetPasswordRequest request) => Reply(await auth.ResetAsync(request));

    private IActionResult Reply(AuthFailure? failure, int success = 204)
    {
        if (failure is null) return StatusCode(success);
        var problem = new ProblemDetails { Status = failure.Status, Title = failure.Message, Type = "about:blank" };
        problem.Extensions["code"] = failure.Code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        if (failure.Errors is not null) problem.Extensions["errors"] = failure.Errors;
        return new ObjectResult(problem) { StatusCode = failure.Status, ContentTypes = { "application/problem+json" } };
    }
}
