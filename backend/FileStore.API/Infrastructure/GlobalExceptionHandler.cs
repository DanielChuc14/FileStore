using FileStore.Application.Common.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FileStore.API.Infrastructure;

/// <summary>
/// Traduce excepciones a respuestas Problem Details (RFC 7807).
/// El detalle de las excepciones no previstas se registra en el log pero nunca
/// se envia al cliente: stack traces y mensajes internos filtran estructura.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problemDetails = exception switch
        {
            ValidationException validation => BuildValidationProblem(validation, httpContext),
            InvalidCredentialsException => BuildProblem(
                StatusCodes.Status401Unauthorized,
                "Invalid credentials.",
                httpContext),
            _ => BuildUnexpectedProblem(exception, httpContext, logger)
        };

        httpContext.Response.StatusCode = problemDetails.Status!.Value;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private static ProblemDetails BuildValidationProblem(
        ValidationException exception,
        HttpContext httpContext)
    {
        var errors = exception.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        return new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.1",
            Instance = httpContext.Request.Path
        };
    }

    private static ProblemDetails BuildProblem(int status, string title, HttpContext httpContext) =>
        new()
        {
            Status = status,
            Title = title,
            Instance = httpContext.Request.Path
        };

    private static ProblemDetails BuildUnexpectedProblem(
        Exception exception,
        HttpContext httpContext,
        ILogger logger)
    {
        logger.LogError(
            exception,
            "Unhandled exception on {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        return new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1",
            Instance = httpContext.Request.Path
        };
    }
}
