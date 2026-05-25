using System.Text.Json;

namespace MadAuthor.Api.Middleware;

/// <summary>
/// Catches unhandled exceptions, logs them, and writes a JSON body so the SPA gets a useful
/// error instead of an empty 500. Also ensures CORS headers are present on error responses
/// (UseCors runs before this in the pipeline, but if we set status codes after UseCors has
/// already added/skipped headers, errors can be invisible to the browser).
/// </summary>
public class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> log)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (UnauthorizedAccessException ex)
        {
            log.LogWarning(ex, "Unauthorized access at {Path}", context.Request.Path);
            await WriteJson(context, StatusCodes.Status401Unauthorized, new
            {
                error = "Unauthorized",
                detail = ex.Message,
            });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Unhandled exception at {Path}", context.Request.Path);
            await WriteJson(context, StatusCodes.Status500InternalServerError, new
            {
                error = ex.GetType().Name,
                detail = ex.Message,
                inner = ex.InnerException?.Message,
                // stack is intentionally surfaced for now - Anton is the only user. Strip
                // before going multi-tenant or production-facing.
                stack = ex.StackTrace,
            });
        }
    }

    private static async Task WriteJson(HttpContext ctx, int status, object body)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";

        // Mirror CORS Allow-Origin onto the error response. UseCors runs earlier but won't
        // re-process a response whose status code we've overwritten here.
        var origin = ctx.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin))
        {
            ctx.Response.Headers["Access-Control-Allow-Origin"] = origin;
            ctx.Response.Headers["Access-Control-Allow-Credentials"] = "true";
            ctx.Response.Headers["Vary"] = "Origin";
        }

        await ctx.Response.WriteAsync(JsonSerializer.Serialize(body,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
