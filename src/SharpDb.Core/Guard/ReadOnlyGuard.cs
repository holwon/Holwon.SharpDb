using SqlParser;
using SqlParser.Ast;
using SqlParser.Dialects;

namespace SharpDb.Core.Guard;

/// <summary>
/// The Guard: the read-only policy, which reads one statement's text and answers whether the
/// server may run it.
/// </summary>
/// <remarks>
/// This is the layer the client gets its feedback from, so a refusal names what was refused and
/// what would have been accepted (ADR 0002). It is not the only layer — the engine refuses writes
/// on its own — but it is the only one that can explain itself.
/// <para>
/// The policy is a <em>permit list</em>: the statement forms it allows are selected deliberately,
/// and everything else is refused. A refusal is therefore always safe, and a mistake shows up as
/// a read-only query being turned away, never as a write reaching the database.
/// </para>
/// <para>
/// Parsing is done with the generic dialect (ADR 0005). The parser classifies statements by
/// grammar, not by their leading word, which is what makes the disguises below visible: a
/// comment before <c>INSERT</c>, a write keyword carried inside a <c>WITH</c> clause, and a
/// second statement stacked after the first.
/// </para>
/// </remarks>
public static class ReadOnlyGuard
{
    /// <summary>
    /// The sentence every refusal ends with, so the client is told what it may send instead.
    /// </summary>
    private const string Guidance = "Only SELECT, EXPLAIN and SHOW statements are permitted.";

    /// <summary>
    /// The functions that read or affect server state, which a read-only query must not call.
    /// </summary>
    /// <remarks>
    /// These are escape hatches rather than statement kinds: they are written as ordinary calls
    /// inside a query the parser is right to call a <c>SELECT</c>. The list is deliberately short
    /// and holds only functions with no read-only meaning; it is a permit-list net, not a policy
    /// of its own, and the engine layer still stands behind it.
    /// </remarks>
    private static readonly HashSet<string> DeniedFunctions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "LOAD_FILE", // MySQL: reads a file from the server host
            "GET_LOCK",  // MySQL: takes a named advisory lock
            "SLEEP",     // MySQL: blocks the server for a while
            "BENCHMARK", // MySQL: burns server CPU for a while
        };

    /// <summary>
    /// Decides whether a statement may run.
    /// </summary>
    /// <param name="sql">The text of exactly one statement, as the client sent it.</param>
    /// <returns>The decision, carrying the reason when the statement is refused.</returns>
    /// <remarks>
    /// Text that cannot be parsed is refused rather than passed along: a statement the Guard
    /// cannot read is one it cannot vouch for, and the engine layer would only reach it after the
    /// fact.
    /// </remarks>
    public static ReadOnlyDecision Inspect(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return Refuse("the statement is empty");
        }

        Sequence<Statement> statements;
        try
        {
            statements = new SqlQueryParser().Parse(sql, new GenericDialect());
        }
        catch (Exception)
        {
            // Everything the parser can throw means the same thing here — the text is not a
            // statement this Guard can read. Catching broadly is the safe direction: an
            // unforeseen parser failure must never become a permit.
            return Refuse("the statement could not be parsed");
        }

        if (statements.Count != 1)
        {
            return Refuse("only one statement may be executed at a time");
        }

        return Classify(statements[0]);
    }

    /// <summary>
    /// Sorts a parsed statement into permitted or refused.
    /// </summary>
    /// <param name="statement">The statement to classify.</param>
    /// <returns>The decision.</returns>
    private static ReadOnlyDecision Classify(Statement statement)
    {
        // Checked before the statement kind, because a denied call is an escape hatch inside a
        // statement that is otherwise permitted — a SELECT, or the SELECT an EXPLAIN explains.
        if (FindDeniedFunction(statement) is { } denied)
        {
            return Refuse($"the function {denied} is not allowed");
        }

        return statement switch
        {
            Statement.Select select => InspectSelect(select),
            Statement.Explain explain => InspectExplain(explain),
            _ when IsShow(statement) => ReadOnlyDecision.Allow(),
            _ => Refuse($"{statement.GetType().Name} is not allowed"),
        };
    }

    /// <summary>
    /// Inspects a <c>SELECT</c>.
    /// </summary>
    /// <param name="select">The statement to inspect.</param>
    /// <returns>The decision.</returns>
    /// <remarks>
    /// A <c>SELECT</c> is not enough on its own: the parser puts a write back into a
    /// <c>SELECT</c> in two places — behind <c>WITH ... INSERT</c>, which lands in the query body,
    /// and behind <c>SELECT ... INTO</c>, which asks the engine to create a table. Both are found
    /// by walking the query rather than by trusting its outermost type.
    /// </remarks>
    private static ReadOnlyDecision InspectSelect(Statement.Select select)
    {
        if (select.Query.Locks is { Count: > 0 })
        {
            return Refuse("a locking SELECT is not allowed");
        }

        var write = FindWrite(select.Query, new HashSet<SetExpression>());
        if (write is not null)
        {
            return Refuse(write);
        }

        return ReadOnlyDecision.Allow();
    }

    /// <summary>
    /// Inspects an <c>EXPLAIN</c>.
    /// </summary>
    /// <param name="explain">The statement to inspect.</param>
    /// <returns>The decision.</returns>
    /// <remarks>
    /// The statement being explained is classified as if it had been sent on its own, and that one
    /// rule is what refuses <c>EXPLAIN INSERT</c> and <c>EXPLAIN ANALYZE INSERT</c> alike. The
    /// parser does report whether <c>ANALYZE</c> was written, but the flag cannot carry the
    /// decision: it is left unset for the bracketed spelling <c>EXPLAIN (ANALYZE) …</c>, which
    /// means exactly the same thing to the engine. Classifying the inner statement instead does
    /// not care how the <c>EXPLAIN</c> was spelled.
    /// </remarks>
    private static ReadOnlyDecision InspectExplain(Statement.Explain explain) =>
        Classify(explain.Statement);

    /// <summary>
    /// Whether a statement is one of the <c>SHOW</c> forms.
    /// </summary>
    /// <param name="statement">The statement to test.</param>
    /// <returns><see langword="true"/> when the statement is a <c>SHOW</c>.</returns>
    /// <remarks>
    /// The parser models <c>SHOW</c> as separate types rather than as one type with variants, so
    /// the permit list names them one by one. Listing them is also what keeps a future parser
    /// version from widening the permit list by accident.
    /// </remarks>
    private static bool IsShow(Statement statement) => statement is
        Statement.ShowCollation or
        Statement.ShowColumns or
        Statement.ShowCreate or
        Statement.ShowDatabases or
        Statement.ShowFunctions or
        Statement.ShowSchemas or
        Statement.ShowStatus or
        Statement.ShowTables or
        Statement.ShowVariable or
        Statement.ShowVariables or
        Statement.ShowViews;

    /// <summary>
    /// Looks for a write that is hidden inside a query.
    /// </summary>
    /// <param name="query">The query to walk.</param>
    /// <param name="visited">The nodes already walked, so a repeated node is not walked twice.</param>
    /// <returns>The clause describing the refusal, or <see langword="null"/> when there is nothing to refuse.</returns>
    /// <remarks>
    /// Both a <c>WITH</c> clause and a parenthesised query nest a query where a write can sit, so
    /// the search goes all the way down rather than checking only the outer body.
    /// </remarks>
    private static string? FindWrite(Query query, HashSet<SetExpression> visited)
    {
        if (query.With is not null)
        {
            foreach (var commonTableExpression in query.With.CteTables)
            {
                var write = FindWrite(commonTableExpression.Query, visited);
                if (write is not null)
                {
                    return write;
                }
            }
        }

        return FindWrite(query.Body, visited);
    }

    /// <summary>
    /// Looks for a write that is hidden inside a query body.
    /// </summary>
    /// <param name="body">The query body to walk.</param>
    /// <param name="visited">The nodes already walked, so a node reached twice is walked once.</param>
    /// <returns>The clause describing the refusal, or <see langword="null"/> when there is nothing to refuse.</returns>
    /// <remarks>
    /// Only a plain <c>SELECT</c> is walked through. Every other body — one that writes, one that
    /// creates a table, and any body a future parser version adds — is refused, so a new kind of
    /// body arrives refused rather than permitted.
    /// </remarks>
    private static string? FindWrite(SetExpression body, HashSet<SetExpression> visited)
    {
        if (!visited.Add(body))
        {
            // Already walked, so whatever it holds was dealt with the first time.
            return null;
        }

        return body switch
        {
            SetExpression.SelectExpression selectExpression =>
                selectExpression.Select.Into is null
                    ? null
                    : "SELECT INTO is not allowed because it creates a table",
            SetExpression.Insert insert =>
                $"a query that runs {insert.Statement.GetType().Name} is not allowed",
            SetExpression.SetOperation setOperation =>
                FindWrite(setOperation.Left, visited) ?? FindWrite(setOperation.Right, visited),
            SetExpression.QueryExpression queryExpression =>
                FindWrite(queryExpression.Query, visited),
            _ => $"{body.GetType().Name} is not allowed",
        };
    }

    /// <summary>
    /// Creates the decision to refuse a statement.
    /// </summary>
    /// <param name="clause">What is being refused, phrased as the rest of the sentence.</param>
    /// <returns>The refusing decision.</returns>
    private static ReadOnlyDecision Refuse(string clause) =>
        ReadOnlyDecision.Refuse($"Statement rejected: {clause}. {Guidance}");

    /// <summary>
    /// Looks through a statement for a call to a function that a read-only query must not make.
    /// </summary>
    /// <param name="statement">The statement to search.</param>
    /// <returns>The name of the offending function, or <see langword="null"/> when there is none.</returns>
    private static string? FindDeniedFunction(Statement statement)
    {
        var walker = new FunctionWalker();
        ((IElement)statement).Visit(walker);
        return walker.DeniedFunction;
    }

    /// <summary>
    /// Walks a statement's expressions looking for a denied function call.
    /// </summary>
    /// <remarks>
    /// The parser's visitor descends a statement's own children but not a function's arguments, so
    /// a call nested inside another call — <c>CONCAT('x', LOAD_FILE(...))</c> — is walked here by
    /// hand. Stopping at the first hit keeps the cost proportional to how early it is found.
    /// </remarks>
    private sealed class FunctionWalker : Visitor
    {
        /// <summary>
        /// The name of the offending function, once one is found.
        /// </summary>
        public string? DeniedFunction { get; private set; }

        /// <inheritdoc/>
        public override ControlFlow PreVisitExpression(Expression expression)
        {
            if (expression is not Expression.Function function)
            {
                return ControlFlow.Continue;
            }

            if (IsDenied(function.Name))
            {
                DeniedFunction = function.Name.ToString();
                return ControlFlow.Break;
            }

            foreach (var argument in ArgumentsOf(function.Args))
            {
                ((IElement)argument).Visit(this);
                if (DeniedFunction is not null)
                {
                    return ControlFlow.Break;
                }
            }

            return ControlFlow.Continue;
        }

        /// <summary>
        /// Whether a function name names a denied function.
        /// </summary>
        /// <param name="name">The called name, which may be qualified.</param>
        /// <returns><see langword="true"/> when the call is denied.</returns>
        /// <remarks>
        /// The last segment is what is compared, so a qualified call such as
        /// <c>main.load_file</c> is recognised. The parser hands back the name with its quoting
        /// resolved, so a quoted spelling cannot slip past.
        /// </remarks>
        private static bool IsDenied(ObjectName name) =>
            name.Values.Count > 0 && DeniedFunctions.Contains(name.Values[^1].Value);

        /// <summary>
        /// The child elements of a call's argument list.
        /// </summary>
        /// <param name="arguments">The argument list, which may be absent.</param>
        /// <returns>Every element that could carry another call.</returns>
        private static IEnumerable<IElement> ArgumentsOf(FunctionArguments? arguments)
        {
            switch (arguments)
            {
                case FunctionArguments.List list:
                    // The parser declares this list nullable even though a call always has one;
                    // treating a missing one as empty keeps a refusal from becoming a null deref.
                    foreach (var argument in list.ArgumentList.Args ?? [])
                    {
                        var value = argument switch
                        {
                            FunctionArg.Unnamed unnamed => unnamed.FunctionArgExpression,
                            FunctionArg.Named named => named.Arg,
                            _ => null,
                        };

                        if (value is FunctionArgExpression.FunctionExpression functionExpression)
                        {
                            yield return functionExpression.Expression;
                        }
                    }

                    break;

                case FunctionArguments.Subquery subquery:
                    yield return subquery.Query;
                    break;
            }
        }
    }
}
