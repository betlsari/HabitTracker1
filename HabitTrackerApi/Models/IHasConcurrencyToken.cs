namespace Models;

/// <summary>
/// Marks entities that use an application-managed optimistic concurrency token.
/// PostgreSQL does not provide SQL Server's rowversion type, so the DbContext
/// assigns a new value whenever one of these entities changes.
/// </summary>
public interface IHasConcurrencyToken
{
    Guid ConcurrencyToken { get; set; }
}
