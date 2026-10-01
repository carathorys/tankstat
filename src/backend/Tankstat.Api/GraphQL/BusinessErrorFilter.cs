using HotChocolate;
using HotChocolate.Execution;
using Tankstat.Application;
using Tankstat.Application.Auth;
using Tankstat.Domain;

namespace Tankstat.Api.GraphQL;

/// <summary>Surfaces rule violations, auth failures and missing records as readable GraphQL errors instead of "unexpected error".</summary>
public sealed class BusinessErrorFilter : IErrorFilter
{
    public IError OnError(IError error) => error.Exception switch
    {
        DomainException e => error.WithMessage(e.Message).WithCode("VALIDATION_FAILED"),
        NotFoundException e => error.WithMessage(e.Message).WithCode("NOT_FOUND"),
        UnauthenticatedException e => error.WithMessage(e.Message).WithCode("UNAUTHENTICATED"),
        ForbiddenException e => error.WithMessage(e.Message).WithCode("FORBIDDEN"),
        InvalidCredentialsException e => error.WithMessage(e.Message).WithCode("INVALID_CREDENTIALS"),
        _ => error,
    };
}
