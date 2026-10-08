using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace PhoneStore.Api.Services.Catalog;

public sealed class CatalogException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public sealed class CatalogExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var failure = context.Exception switch
        {
            CatalogException error => error,
            SqlException { Number: 51002 or 1205 } => new CatalogException(409, "CATALOG_BUSY", "Catalog đang được cập nhật. Vui lòng thử lại."),
            DbUpdateConcurrencyException => new CatalogException(412, "VERSION_MISMATCH", "Dữ liệu đã thay đổi. Vui lòng tải lại."),
            DbUpdateException { InnerException: SqlException sql } when sql.Number is 2601 or 2627 => new CatalogException(409, "DUPLICATE_CATALOG", "Tên, slug, SKU hoặc phiên bản đã tồn tại."),
            DbUpdateException { InnerException: SqlException sql } when sql.Number == 547 => new CatalogException(409, "REFERENCE_CONFLICT", "Dữ liệu liên quan không còn hợp lệ."),
            _ => null
        };
        if (failure is null) return;
        var problem = new ProblemDetails { Status = failure.Status, Title = failure.Message, Type = "about:blank" };
        problem.Extensions["code"] = failure.Code; problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        context.Result = new ObjectResult(problem) { StatusCode = failure.Status, ContentTypes = { "application/problem+json" } };
        context.ExceptionHandled = true;
    }
}
