using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace PhoneStore.Api.Services.Checkout;
public sealed class CheckoutException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public sealed class CheckoutExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var error = context.Exception switch {
            CheckoutException failure => failure,
            SqlException { Number: 51001 or 51004 or 1205 or 1222 } => new CheckoutException(409, "CHECKOUT_BUSY", "Đang xử lý yêu cầu. Vui lòng thử lại."),
            DbUpdateConcurrencyException => new CheckoutException(409, "CHECKOUT_BUSY", "Dữ liệu đã thay đổi. Thử lại với cùng phiên và nội dung."),
            _ => null
        };
        if (error is null) return;
        var problem = new ProblemDetails { Status = error.Status, Title = error.Message, Type = "about:blank" };
        problem.Extensions["code"] = error.Code; problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        context.Result = new ObjectResult(problem) { StatusCode = error.Status, ContentTypes = { "application/problem+json" } }; context.ExceptionHandled = true;
    }
}
