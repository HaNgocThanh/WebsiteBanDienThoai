using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace PhoneStore.Api.Services.InventoryManagement;

public sealed class InventoryException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public sealed class InventoryExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var error = context.Exception switch
        {
            InventoryException e => e,
            SqlException { Number: 51003 or 1205 or 1222 } => new InventoryException(409, "INVENTORY_BUSY", "Kho đang được cập nhật. Vui lòng thử lại cùng thao tác."),
            DbUpdateConcurrencyException => new InventoryException(409, "INVENTORY_BUSY", "Kho đã thay đổi. Vui lòng thử lại cùng thao tác."),
            DbUpdateException { InnerException: SqlException sql } when sql.Number is 2601 or 2627 or 1205 or 1222 => new InventoryException(409, "INVENTORY_BUSY", "Kho đang được cập nhật. Vui lòng thử lại cùng thao tác."),
            _ => null
        };
        if (error is null) return;
        var problem = new ProblemDetails { Status = error.Status, Title = error.Message, Type = "about:blank" };
        problem.Extensions["code"] = error.Code; problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        context.Result = new ObjectResult(problem) { StatusCode = error.Status, ContentTypes = { "application/problem+json" } };
        context.ExceptionHandled = true;
    }
}
