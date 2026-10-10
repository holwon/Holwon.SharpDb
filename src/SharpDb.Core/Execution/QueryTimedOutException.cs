namespace SharpDb.Core.Execution;

/// <summary>
/// Raised when a query outlived the timeout the server applies to it.
/// </summary>
/// <remarks>
/// The seconds are carried rather than the wording, because the sentence the client reads is built
/// by <see cref="QueryErrors"/> and belongs with the other messages. The exception itself never
/// leaves Core: the Tool turns it into a failed result (ADR 0010).
/// </remarks>
/// <param name="seconds">The configured timeout, in seconds.</param>
internal sealed class QueryTimedOutException(int seconds)
    : Exception($"The query did not finish within {seconds} seconds.")
{
    /// <summary>
    /// The configured timeout, in seconds.
    /// </summary>
    /// <value>The number of seconds the query was allowed to run.</value>
    internal int Seconds { get; } = seconds;
}
