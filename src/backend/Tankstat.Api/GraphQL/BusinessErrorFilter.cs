using HotChocolate;
using HotChocolate.Execution;
using Tankstat.Application;
using Tankstat.Application.Auth;
using Tankstat.Domain;

namespace Tankstat.Api.GraphQL;

/// <summary>
/// Surfaces rule violations, auth failures and missing records as GraphQL errors with a coarse <c>code</c>
/// (VALIDATION_FAILED, NOT_FOUND, ...) plus a specific translation <c>key</c> and its <c>args</c>,
/// so clients translate the message themselves.
/// </summary>
public sealed class BusinessErrorFilter : IErrorFilter
{
    public IError OnError(IError error)
    {
        if (error.Exception is not KeyedException e) return error;

        var code = e switch
        {
            DomainException => "VALIDATION_FAILED",
            NotFoundException => "NOT_FOUND",
            UnauthenticatedException => "UNAUTHENTICATED",
            ForbiddenException => "FORBIDDEN",
            InvalidCredentialsException => "INVALID_CREDENTIALS",
            _ => null,
        };
        if (code is null) return error;

        return error.WithMessage(e.Message).WithCode(code).SetExtension("key", e.Key).SetExtension("args", e.Args);
    }
}
