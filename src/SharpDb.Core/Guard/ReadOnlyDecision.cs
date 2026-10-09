namespace SharpDb.Core.Guard;

/// <summary>
/// The Guard's answer about one statement: whether it may run, and when it may not, why.
/// </summary>
/// <remarks>
/// The reason is written for the client to act on, not for a log: a refused statement is
/// ordinary traffic between the server and an AI agent, so the text has to say what went wrong
/// and what would have been accepted instead (ADR 0010).
/// </remarks>
public sealed record ReadOnlyDecision
{
    private ReadOnlyDecision(bool isAllowed, string? reason)
    {
        IsAllowed = isAllowed;
        Reason = reason;
    }

    /// <summary>
    /// Whether the statement is a read-only statement the server may run.
    /// </summary>
    public bool IsAllowed { get; }

    /// <summary>
    /// Why the statement may not run.
    /// </summary>
    /// <value>
    /// A sentence addressed to the client, or <see langword="null"/> when <see cref="IsAllowed"/> is true.
    /// </value>
    public string? Reason { get; }

    /// <summary>
    /// Creates the decision to run the statement.
    /// </summary>
    /// <returns>The permitting decision.</returns>
    public static ReadOnlyDecision Allow() => new(true, null);

    /// <summary>
    /// Creates the decision to refuse the statement.
    /// </summary>
    /// <param name="reason">The sentence the client reads.</param>
    /// <returns>The refusing decision.</returns>
    public static ReadOnlyDecision Refuse(string reason) => new(false, reason);
}
