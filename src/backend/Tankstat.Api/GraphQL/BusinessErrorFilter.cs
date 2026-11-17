using HotChocolate;
using HotChocolate.Execution;
using Tankstat.Application;
using Tankstat.Application.Auth;
using Tankstat.Application.Runs;
using Tankstat.Domain;

namespace Tankstat.Api.GraphQL;

/// <summary>
/// Surfaces rule violations, auth failures and missing records as GraphQL errors with a coarse <c>code</c>
/// (VALIDATION_FAILED, NOT_FOUND, ...) plus a specific translation <c>key</c> and its <c>args</c>,
/// so clients translate the message themselves.
/// </summary>
public sealed class BusinessErrorFilter(IBusyFailures? busy = null) : IErrorFilter
{
    public IError OnError(IError error)
    {
        // The database itself refused the save because a run holds it (#335): the same answer as the gate's.
        if (error.Exception is { } raw and not KeyedException && busy?.IsBusy(raw) == true) error = error.WithException(new BusyException());
        if (error.Exception is not KeyedException e) return error;

        var code = e switch
        {
            DomainException => "VALIDATION_FAILED",
            NotFoundException => "NOT_FOUND",
            UnauthenticatedException => "UNAUTHENTICATED",
            ForbiddenException => "FORBIDDEN",
            InvalidCredentialsException => "INVALID_CREDENTIALS",
            BusyException => "BUSY", // a run (#335) holds what the save needs: to be tried again, never a rule's refusal
            _ => null,
        };
        if (code is null) return error;

        return error.WithMessage(e.Message).WithCode(code).SetExtension("key", e.Key).SetExtension("args", e.Args);
    }
}
