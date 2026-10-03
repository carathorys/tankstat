using System.Security.Claims;

namespace Tankstat.Api.Auth;

/// <summary>Who a request is for, in the form log lines name them: the user's id, or "anonymous". Never a name or an address.</summary>
internal static class RequestUser
{
    public const string Anonymous = "anonymous";

    /// <summary>The id of the signed-in user (the session's name identifier is always the user's id), or <see cref="Anonymous"/>.</summary>
    public static string Id(ClaimsPrincipal? user) => user?.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } id ? id : Anonymous;
}

public static class RequestUserScope
{
    /// <summary>
    /// Wraps everything after authentication in a log scope with the user's id (<c>UserId</c>), so the lines of the framework, the web
    /// server and the endpoints can be attributed when scopes are switched on (<c>Logging:Console:FormatterOptions:IncludeScopes</c>).
    /// Requests without a signed-in user get no scope.
    /// </summary>
    public static IApplicationBuilder UseUserLogScope(this IApplicationBuilder app)
    {
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("Tankstat.Api.Requests");
        return app.Use(async (http, next) =>
        {
            var id = RequestUser.Id(http.User);
            if (id == RequestUser.Anonymous)
            {
                await next(http);
                return;
            }
            using (logger.BeginScope(new Dictionary<string, object> { ["UserId"] = id })) // a key/value state, which the JSON formatter renders as such
                await next(http);
        });
    }
}
