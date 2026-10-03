using System.Diagnostics;
using System.Runtime.CompilerServices;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Instrumentation;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Tankstat.Api.Auth;
using Tankstat.Application.Auth;
using Tankstat.Domain;

namespace Tankstat.Api.GraphQL;

/// <summary>
/// Writes what goes wrong in a GraphQL request to the log. HotChocolate does not log by itself, and <see cref="BusinessErrorFilter"/> only
/// shapes errors for the client: without this, an exception nobody expected (a mail server that is down, a bug) reaches the client as
/// "Unexpected Execution Error" and leaves no trace anywhere. By what the error is:
/// a refusal (<see cref="ForbiddenException"/>) is a Warning; any other business error (a validation failure, something missing, a
/// wrong password) is the client's doing and only a Debug line; everything else is an Error with its exception, once.
/// Never logged: variables, the document, and the text of request errors, which can quote what the client sent (a password).
/// </summary>
internal sealed class GraphQLLoggingListener(ILoggerFactory factory, IHttpContextAccessor http) : ExecutionDiagnosticEventListener
{
    private static readonly object Marker = new();

    private readonly ILogger _logger = factory.CreateLogger<GraphQLLoggingListener>();

    // One exception can come back through several hooks (a DataLoader's failure reaches every field that waited for it, and the result is
    // checked once more when the request ends): it is logged the first time and then only remembered, for as long as it lives.
    private readonly ConditionalWeakTable<Exception, object> _reported = new();

    public override IDisposable ExecuteRequest(RequestContext context) => new RequestScope(this, context, Stopwatch.GetTimestamp());

    public override void ResolverError(IMiddlewareContext context, IError error) =>
        Report(error, Describe(context.Operation.RootType.Name, context.Operation.Name), context.Path.ToString());

    public override void ResolverError(RequestContext context, ISelection selection, IError error)
    {
        // A value that must not be null was null: no exception, and always a bug on the server (a client cannot cause it).
        if (error.Exception is null) _logger.LogWarning("GraphQL {Operation} produced error {Code} at {Path} for user {UserId}", Operation(context), error.Code, error.Path?.ToString() ?? selection.ResponseName, UserId());
        else Report(error, Operation(context), error.Path?.ToString() ?? selection.ResponseName);
    }

    public override void TaskError(IExecutionTask task, IError error) => Report(error, "task", error.Path?.ToString());

    public override void RequestError(RequestContext context, IError error) =>
        _logger.LogDebug("GraphQL request was rejected: {Code}", error.Code); // the code only: the message of a request error can quote the request

    public override void RequestError(RequestContext context, Exception exception) => ReportException(exception, Operation(context), "the request");

    public override void ValidationErrors(RequestContext context, IReadOnlyList<IError> errors) =>
        _logger.LogDebug("GraphQL {Operation} failed validation with {Count} errors ({Codes})", Operation(context), errors.Count, string.Join(", ", errors.Select(e => e.Code).Distinct()));

    private void Report(IError error, string operation, string? path)
    {
        if (error.Exception is { } exception) ReportException(exception, operation, path);
        else _logger.LogDebug("GraphQL {Operation} failed at {Path}: {Code}", operation, path, error.Code); // an argument or a type the client got wrong
    }

    private void ReportException(Exception exception, string operation, string? path)
    {
        if (!_reported.TryAdd(exception, Marker)) return;
        var user = UserId();
        switch (exception)
        {
            case ForbiddenException forbidden:
                _logger.LogWarning("GraphQL {Operation} was refused at {Path} for user {UserId}: {Key}", operation, path, user, forbidden.Key);
                break;
            case KeyedException keyed:
                _logger.LogDebug("GraphQL {Operation} failed at {Path} for user {UserId}: {Key}", operation, path, user, keyed.Key);
                break;
            case OperationCanceledException:
                _logger.LogDebug("GraphQL {Operation} was cancelled at {Path}", operation, path);
                break;
            default:
                _logger.LogError(exception, "GraphQL {Operation} failed at {Path} for user {UserId} with an unexpected error", operation, path, user);
                break;
        }
    }

    private string UserId() => RequestUser.Id(http.HttpContext?.User);

    /// <summary>
    /// The operation a request runs, from its parsed document: the one the client asked for by name, or the only one there is. A name that
    /// matches nothing in the document is the client's text and is treated like any other (see <see cref="Describe"/>).
    /// </summary>
    private static string Operation(RequestContext context)
    {
        var requested = context.Request.OperationName;
        var operations = context.OperationDocumentInfo.Document?.Definitions.OfType<OperationDefinitionNode>().ToList() ?? [];
        var chosen = requested is null ? (operations.Count == 1 ? operations[0] : null) : operations.FirstOrDefault(o => o.Name?.Value == requested);
        return chosen is null ? Describe(null, requested) : Describe(chosen.Operation.ToString(), chosen.Name?.Value);
    }

    /// <summary>
    /// "mutation addVehicle". The name comes from the client, so only what a GraphQL name can contain is passed on: anything else (a
    /// line break would let a client forge log lines) is left out.
    /// </summary>
    private static string Describe(string? rootType, string? name)
    {
        var safe = string.IsNullOrEmpty(name) ? "(unnamed)" : name.Length <= 100 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? name : "(invalid name)";
        return rootType is null ? $"operation {safe}" : $"{rootType.ToLowerInvariant()} {safe}";
    }

    /// <summary>Times the request for the Debug line, and checks its result for errors no hook told about (their exceptions are logged once).</summary>
    private sealed class RequestScope(GraphQLLoggingListener owner, RequestContext context, long started) : IDisposable
    {
        public void Dispose()
        {
            var errors = (context.Result as OperationResult)?.Errors ?? [];
            foreach (var error in errors)
                if (error.Exception is { } exception) owner.ReportException(exception, Operation(context), error.Path?.ToString());
            owner._logger.LogDebug("GraphQL {Operation} finished in {Ms:0} ms with {Errors} errors", Operation(context), Stopwatch.GetElapsedTime(started).TotalMilliseconds, errors.Count);
        }
    }
}
