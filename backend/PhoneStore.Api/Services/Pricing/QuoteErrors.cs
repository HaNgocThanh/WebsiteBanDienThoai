using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PhoneStore.Api.Services.Pricing;
public sealed class QuoteException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public sealed class QuoteExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not QuoteException error) return;
        var problem = new ProblemDetails { Status = error.Status, Title = error.Message, Type = "about:blank" };
        problem.Extensions["code"] = error.Code; problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        // Safe user-facing detail only; no exceptions/SQL/config/PII in this extension.
        problem.Extensions["errors"] = new Dictionary<string, string[]> { ["items"] = [error.Message] };
        context.Result = new ObjectResult(problem) { StatusCode = error.Status, ContentTypes = { "application/problem+json" } };
        context.ExceptionHandled = true;
    }
}
