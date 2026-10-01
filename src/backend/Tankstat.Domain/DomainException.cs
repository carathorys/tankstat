namespace Tankstat.Domain;

/// <summary>A business rule was violated; the message is safe to show to API clients.</summary>
public sealed class DomainException(string message) : Exception(message);
