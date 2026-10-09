using System.Diagnostics;
using System.Runtime.CompilerServices;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Instrumentation;
using HotChocolate.Execution.Processing;
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
/// wrong password) and a request the engine turns down (a missing variable) is the client's doing and only a Debug line; everything else
/// is an Error with its exception, once. A value that must not be null but was (<c>HC0018</c>) is always the server's bug, a Warning:
/// no hook tells about it, so it is found in the result when the request ends.
/// Never logged: variables, the document, and the text of request errors, which can quote what the client sent (a password). A field is
/// named the way the schema names it (<c>Mutation.addVehicle</c>), not by the alias a client gave it in its query.
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
        Report(error, Describe(context.Operation.RootType.Name, context.Operation.Name), Field(context.Selection));

    public override void TaskError(IExecutionTask task, IError error) => Report(error, "task", SafePath(error.Path));

    public override void RequestError(RequestContext context, IError error) =>
        _logger.LogDebug("GraphQL request was rejected: {Code}", error.Code); // the code only: the message of a request error can quote the request

    public override void RequestError(RequestContext context, Exception exception) => ReportException(exception, Operation(context), "(the request)");

    public override void ValidationErrors(RequestContext context, IReadOnlyList<IError> errors) =>
        _logger.LogDebug("GraphQL {Operation} failed validation with {Count} errors ({Codes})", Operation(context), errors.Count, string.Join(", ", errors.Select(e => e.Code).Distinct()));

    private void Report(IError error, string operation, string field)
    {
        if (error.Exception is { } exception) ReportException(exception, operation, field);
        else _logger.LogDebug("GraphQL {Operation} failed at {Field}: {Code}", operation, field, error.Code); // an argument or a type the client got wrong
    }

    private void ReportException(Exception exception, string operation, string field)
    {
        if (!_reported.TryAdd(exception, Marker)) return;
        var user = UserId();
        switch (exception)
        {
            case ForbiddenException forbidden:
                _logger.LogWarning("GraphQL {Operation} was refused at {Field} for user {UserId}: {Key}", operation, field, user, forbidden.Key);
                break;
            case KeyedException keyed:
                _logger.LogDebug("GraphQL {Operation} failed at {Field} for user {UserId}: {Key}", operation, field, user, keyed.Key);
                break;
            case GraphQLException request:
                // The engine turning a request down (a variable that is missing or of the wrong type): the client's doing, and the message
                // of such an error can quote what was sent, so only its codes are kept.
                _logger.LogDebug("GraphQL {Operation} was rejected at {Field}: {Codes}", operation, field, string.Join(", ", request.Errors.Select(e => e.Code).Distinct()));
                break;
            case OperationCanceledException:
                _logger.LogDebug("GraphQL {Operation} was cancelled at {Field}", operation, field);
                break;
            default:
                _logger.LogError(exception, "GraphQL {Operation} failed at {Field} for user {UserId} with an unexpected error", operation, field, user);
                break;
        }
    }

    private string UserId() => RequestUser.Id(http.HttpContext?.User);

    /// <summary>"Mutation.addVehicle": the field the way the schema names it, which is short and constant (an error's path uses the names the client gave).</summary>
    private static string Field(ISelection selection) => $"{selection.Field.DeclaringType.Name}.{selection.Field.Name}";

    /// <summary>
    /// The field at a response path the way the schema names it ("Holder.nullNested"), found through the operation the request ran: the
    /// path holds the names the client gave. Null when it cannot be told; a logging helper never throws.
    /// </summary>
    private static string? SchemaField(RequestContext context, HotChocolate.Path? path)
    {
        try
        {
            if (path is null || !context.TryGetOperation(out var operation)) return null;
            var names = new List<string>();
            for (var segment = path; segment is not null && !segment.IsRoot; segment = segment.Parent)
                if (segment is NamePathSegment name) names.Add(name.Name);
            names.Reverse();

            IEnumerable<SelectionSet> sets = [operation.RootSelectionSet];
            Selection? found = null;
            foreach (var name in names)
            {
                if (found is not null) // the field the last name led to: its selections, for each type it can be (an interface or a union)
                {
                    var parent = found;
                    sets = operation.GetPossibleTypes(parent).Select(type => operation.GetSelectionSet(parent, type));
                }
                found = sets.SelectMany(set => set.Selections.ToArray()).FirstOrDefault(selection => selection.ResponseName == name);
                if (found is null) return null;
            }
            return found is null ? null : Field(found);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A response path (<c>a/0/b</c>) is made of the names a client gave its fields, so it is passed on only when it is short and holds
    /// nothing but the characters of such names.
    /// </summary>
    private static string SafePath(HotChocolate.Path? path)
    {
        var text = path?.ToString();
        if (string.IsNullOrEmpty(text)) return "(unknown)";
        return text.Length <= 200 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '/' or '.' or '[' or ']') ? text : "(path withheld)";
    }

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

    /// <summary>
    /// Times the request for the Debug line, and checks its result for errors no hook told about: their exceptions (logged once), and a
    /// non-null field that was null.
    /// </summary>
    private sealed class RequestScope(GraphQLLoggingListener owner, RequestContext context, long started) : IDisposable
    {
        public void Dispose()
        {
            var errors = (context.Result as OperationResult)?.Errors ?? [];
            foreach (var error in errors)
            {
                if (error.Exception is { } exception) owner.ReportException(exception, Operation(context), SafePath(error.Path));
                else if (error.Code == ErrorCodes.Execution.NonNullViolation && error.Path is { IsRoot: false })
                    // A field that must not be null was null: always a bug on the server (a client cannot cause it), and no hook tells. The
                    // same code without a path is a required variable the client left out (the client's doing, logged as a request error).
                    owner._logger.LogWarning("GraphQL {Operation} returned null for non-null field {Field} ({Code}) for user {UserId}",
                        Operation(context), SchemaField(context, error.Path) ?? SafePath(error.Path), error.Code, owner.UserId());
            }
            if (owner._logger.IsEnabled(LogLevel.Debug))
                owner._logger.LogDebug("GraphQL {Operation} finished in {Ms:0} ms with {Errors} errors", Operation(context), Stopwatch.GetElapsedTime(started).TotalMilliseconds, errors.Count);
        }
    }
}
