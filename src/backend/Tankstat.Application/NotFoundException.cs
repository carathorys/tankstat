using Tankstat.Domain;

namespace Tankstat.Application;

public sealed class NotFoundException(string key, string message, object? args = null) : KeyedException(key, message, args);
