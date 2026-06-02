namespace Intropy.BusinessIncidentService.Filters;

public class ExceptionFilter(ILogger<ExceptionFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in {Method} {Path}",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path);

            return Results.Problem(detail: ex.Message, statusCode: 500);
        }
    }
}
