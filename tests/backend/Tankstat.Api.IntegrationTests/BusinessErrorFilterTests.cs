using HotChocolate;
using Tankstat.Api.GraphQL;
using Tankstat.Application;
using Tankstat.Application.Auth;
using Tankstat.Domain;

namespace Tankstat.Api.IntegrationTests;

/// <summary>Business errors reach the client with a coarse code and the key and arguments it translates; anything else is left as the engine made it.</summary>
public class BusinessErrorFilterTests
{
    private sealed class OtherKeyedException() : KeyedException("other.key", "Something else.");

    private static IError Filtered(Exception exception) =>
        new BusinessErrorFilter().OnError(ErrorBuilder.New().SetMessage("Unexpected Execution Error").SetException(exception).Build());

    public static TheoryData<Exception, string> Known => new()
    {
        { new DomainException("vehicle.nameRequired", "A name is required.", new { Max = 3 }), "VALIDATION_FAILED" },
        { new NotFoundException("vehicle.notFound", "Not found."), "NOT_FOUND" },
        { new UnauthenticatedException(), "UNAUTHENTICATED" },
        { new ForbiddenException(), "FORBIDDEN" },
        { new InvalidCredentialsException(), "INVALID_CREDENTIALS" },
    };

    [Theory]
    [MemberData(nameof(Known))]
    public void AKnownBusinessError_GetsItsCode_ItsKey_AndItsOwnMessage(Exception exception, string code)
    {
        var error = Filtered(exception);
        var keyed = (KeyedException)exception;

        Assert.Equal((code, exception.Message), (error.Code, error.Message));
        Assert.Equal(keyed.Key, error.Extensions!["key"]);
        Assert.Same(keyed.Args, error.Extensions["args"]);
    }

    [Fact]
    public void AnyOtherError_IsLeftAsTheEngineMadeIt()
    {
        foreach (var exception in new Exception[] { new OtherKeyedException(), new InvalidOperationException("smtp is down") })
        {
            var error = Filtered(exception);

            Assert.Equal(("Unexpected Execution Error", (string?)null), (error.Message, error.Code));
            Assert.False(error.Extensions?.ContainsKey("key") ?? false);
        }
    }
}
