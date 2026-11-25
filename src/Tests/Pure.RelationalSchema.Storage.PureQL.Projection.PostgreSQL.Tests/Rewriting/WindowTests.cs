using System.Linq.Expressions;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;
using Reading = (long Id, string Group, decimal? Amount);

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Rewriting;

// PureQLWindows.Over in, PostgreSqlWindows.Over out: each window function and
// each part of the window, and every window that stays as it is.
public sealed class WindowTests
{
    private static readonly IQueryable<Reading> Rows = Array
        .Empty<Reading>()
        .AsQueryable();

    private static readonly Expression Partitioned = Tree(rows =>
        rows.Over(
            row => new ValueTuple<string, long>(row.Group, row.Id),
            partition =>
                partition.OrderBy(row => row.Amount).ThenByDescending(row => row.Id),
            row => new ValueTuple<decimal?, long>(row.Amount, row.Id),
            PureQLFrame.Rows(2, 0),
            window => new ValueTuple<Reading, decimal?>(
                window.Row,
                window.Rows.Sum(row => row.Amount)
            )
        )
    );

    [Fact]
    public void WindowBecomesTheMarkerWithItsPartitionOrderAndFrame()
    {
        Assert.Equal(
            "PostgreSqlWindows.Over(rows, row => (object)(row.Item2, row.Item1), "
                + "partition => partition.OrderBy(row => row.Item3).ThenByDescending(row => row.Item1), "
                + "[ROWS BETWEEN 2 PRECEDING AND CURRENT ROW], "
                + "(row, window) => (row, window.Rows.Sum(row => row.Item3)))",
            Rewritten(Partitioned)
        );
    }

    [Fact]
    public void WindowWithoutPartitionOrOrderHasNeither()
    {
        Assert.Equal(
            "PostgreSqlWindows.Over(rows, null, null, null, "
                + "(row, window) => (row, window.Rows.LongCount()))",
            Rewritten(
                Tree(rows =>
                    rows.Over(
                        row => new ValueTuple(),
                        partition => partition,
                        row => new ValueTuple(),
                        PureQLFrame.Partition,
                        window => new ValueTuple<Reading, long>(
                            window.Row,
                            window.Rows.LongCount()
                        )
                    )
                )
            )
        );
    }

    public static TheoryData<Expression, string> Functions =>
        new()
        {
            {
                Ranked(window => new ValueTuple<Reading, long>(
                    window.Row,
                    window.RowNumber
                )),
                "(row, window) => (row, PostgreSqlWindows.RowNumber(window))"
            },
            {
                Ranked(window => new ValueTuple<Reading, long>(window.Row, window.Rank)),
                "(row, window) => (row, PostgreSqlWindows.Rank(window))"
            },
            {
                Ranked(window => new ValueTuple<Reading, long>(
                    window.Row,
                    window.DenseRank
                )),
                "(row, window) => (row, PostgreSqlWindows.DenseRank(window))"
            },
            {
                Ranked(window => new ValueTuple<Reading, long>(
                    window.Row,
                    window.Ntile(3L)
                )),
                "(row, window) => (row, (long)PostgreSqlWindows.Ntile(window, 3))"
            },
            {
                Ranked(window => new ValueTuple<Reading, long>(
                    window.Row,
                    window.Ntile(long.MaxValue)
                )),
                "(row, window) => (row, (long)PostgreSqlWindows.Ntile(window, 2147483647))"
            },
            {
                Ranked(window => new ValueTuple<Reading, decimal?>(
                    window.Row,
                    window.Has(-1L) ? window.At(-1L).Amount : null
                )),
                "(row, window) => (row, PostgreSqlWindows.Lag(window, other => other.Item3, 1, null))"
            },
            {
                Ranked(window => new ValueTuple<Reading, decimal?>(
                    window.Row,
                    window.Has(2L) ? window.At(2L).Amount : window.Row.Amount
                )),
                "(row, window) => (row, PostgreSqlWindows.Lead(window, other => other.Item3, 2, row.Item3))"
            },
            {
                Ranked(window => new ValueTuple<Reading, decimal?>(
                    window.Row,
                    window.Rows.Where(row => row.Id > 1).Max(row => row.Amount)
                )),
                "(row, window) => (row, window.Rows.Where(row => (row.Item1 > 1L)).Max(row => row.Item3))"
            },
        };

    [Theory]
    [MemberData(nameof(Functions))]
    public void FunctionBecomesTheMarkersFunction(Expression tree, string selector)
    {
        Assert.EndsWith(
            "[ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING], "
                + selector
                + ")",
            Rewritten(tree)
        );
    }

    [Fact]
    public void PercentRankIsDividedExactly()
    {
        Assert.EndsWith(
            "(row, window) => (row, (((window.Rows.LongCount() - 1L) == 0L) ? 0m : "
                + "(Math.Truncate((((((decimal)(PostgreSqlWindows.Rank(window) - 1L) "
                + "* 20000000000000000000000000000m) + (decimal)(window.Rows.LongCount() - 1L)) "
                + "- ((((decimal)(PostgreSqlWindows.Rank(window) - 1L) "
                + "* 20000000000000000000000000000m) + (decimal)(window.Rows.LongCount() - 1L)) "
                + "% ((decimal)(window.Rows.LongCount() - 1L) * 2m))) "
                + "/ ((decimal)(window.Rows.LongCount() - 1L) * 2m))) "
                + "* 0.0000000000000000000000000001m))))",
            Rewritten(
                Ranked(window => new ValueTuple<Reading, decimal>(
                    window.Row,
                    window.PercentRank
                ))
            )
        );
    }

    [Fact]
    public void CumulativeDistributionCountsThePeersFromCumeDist()
    {
        Assert.Contains(
            "Math.Round((PostgreSqlWindows.CumeDist(window) * (double)window.Rows.LongCount()))",
            Rewritten(
                Ranked(window => new ValueTuple<Reading, decimal>(
                    window.Row,
                    window.CumulativeDistribution
                ))
            )
        );
    }

    public static TheoryData<Expression> Kept =>
        [
            Ranked(window => new ValueTuple<Reading, decimal?>(
                window.Row,
                window.Rows.Average(row => row.Amount)
            )),
            Ranked(window => new ValueTuple<Reading, decimal?>(
                window.Row,
                window.Rows.Average(row => row.Amount)
            )),
            Ranked(window => new ValueTuple<Reading, long>(window.Row, window.Ntile(0L))),
            Ranked(window => new ValueTuple<Reading, decimal?>(
                window.Row,
                window.Has(0L) ? window.At(0L).Amount : null
            )),
            Ranked(window => new ValueTuple<Reading, decimal?>(
                window.Row,
                window.Has(long.MinValue) ? window.At(long.MinValue).Amount : null
            )),
            Ranked(window => new ValueTuple<Reading, decimal?>(
                window.Row,
                window.Has(-1L) ? window.At(-2L).Amount : null
            )),
            Ranked(window => new ValueTuple<Reading, decimal?>(
                window.Row,
                window.Has(-1L) ? window.At(-1L).Amount + window.Row.Amount : null
            )),
            Ranked(window => new ValueTuple<Reading, decimal?>(
                window.Row,
                window.At(1L).Amount
            )),
            Ranked(window => new ValueTuple<Reading, bool>(window.Row, window.Has(1L))),
            Ranked(window => new ValueTuple<PureQLWindow<Reading>, int>(window, 1)),
            Framed(
                PureQLFrame.Rows(1, 0),
                window => new ValueTuple<Reading, decimal>(window.Row, window.PercentRank)
            ),
            Framed(
                PureQLFrame.Rows(1, 0),
                window => new ValueTuple<Reading, decimal>(
                    window.Row,
                    window.CumulativeDistribution
                )
            ),
            Framed(
                PureQLFrame.Range(
                    false,
                    1,
                    PureQLRangeUnit.Days,
                    1,
                    PureQLRangeUnit.Months
                ),
                window => new ValueTuple<Reading, long>(
                    window.Row,
                    window.Rows.LongCount()
                )
            ),
        ];

    [Theory]
    [MemberData(nameof(Kept))]
    public void WindowPostgreSqlComputesOtherwiseIsKept(Expression tree)
    {
        Assert.Equal(TreeText.Of(tree), Rewritten(tree));
    }

    [Fact]
    public void WindowOverRowsWhoseColumnNamesCollideIsKept()
    {
        Expression tree = Tree(rows =>
            rows.Over(
                row => row.Id,
                partition => partition,
                row => row.Id,
                PureQLFrame.Partition,
                window => new ValueTuple<Reading, long>(
                    window.Row,
                    window.Rows.LongCount()
                )
            )
        );
        Expression deep = tree;

        for (int pass = 0; pass < 11; pass++)
        {
            deep = Over(deep);
        }

        Assert.DoesNotContain(
            "PureQLWindows",
            Rewritten(Over(tree)),
            StringComparison.Ordinal
        );
        Assert.StartsWith(
            "PureQLWindows.Over(",
            Rewritten(deep),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void WindowOverReferenceTuplesIsRewritten()
    {
        Expression<
            Func<
                IQueryable<Tuple<long, string>>,
                IQueryable<ValueTuple<Tuple<long, string>, long>>
            >
        > tree = rows =>
            rows.Over(
                row => row.Item1,
                partition => partition,
                row => row.Item1,
                PureQLFrame.Partition,
                window => new ValueTuple<Tuple<long, string>, long>(
                    window.Row,
                    window.Rows.LongCount()
                )
            );

        Assert.StartsWith(
            "PostgreSqlWindows.Over(",
            Rewritten(tree.Body),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void WindowOverAPlainSequenceIsKept()
    {
        Expression<
            Func<IEnumerable<Reading>, IEnumerable<ValueTuple<Reading, long>>>
        > tree = rows =>
            rows.Over(
                row => row.Id,
                partition => partition,
                row => row.Id,
                PureQLFrame.Partition,
                window => new ValueTuple<Reading, long>(
                    window.Row,
                    window.Rows.LongCount()
                )
            );

        Assert.Equal(TreeText.Of(tree.Body), Rewritten(tree.Body));
    }

    [Fact]
    public void ColumnNamesOfRowsAreTheirPaths()
    {
        Assert.True(ColumnNames.AreDistinct(typeof(long)));
        Assert.True(ColumnNames.AreDistinct(typeof(Tuple<long, string>)));
        Assert.True(
            ColumnNames.AreDistinct(typeof(ValueTuple<long, ValueTuple<long, string>>))
        );
    }

    [Fact]
    public void WindowsInEachOtherAreEachRewritten()
    {
        Assert.Equal(
            2,
            Rewritten(Over(Partitioned))
                .Split("PostgreSqlWindows.Over(", StringSplitOptions.None)
                .Length - 1
        );
    }

    [Fact]
    public void FrameIsComputedFromAParameterValue()
    {
        Holder count = new() { Value = 3 };
        Expression tree = Tree(rows =>
            rows.Over(
                row => row.Id,
                partition => partition.OrderBy(row => row.Id),
                row => row.Id,
                PureQLFrame.Rows(count.Value, 0),
                window => new ValueTuple<Reading, long>(
                    window.Row,
                    window.Rows.LongCount()
                )
            )
        );

        Assert.Contains("[ROWS BETWEEN 3 PRECEDING AND CURRENT ROW]", Rewritten(tree));
    }

    [Fact]
    public void NegativeFrameCountFailsAsInMemory()
    {
        Holder count = new() { Value = -1 };
        Expression tree = Tree(rows =>
            rows.Over(
                row => row.Id,
                partition => partition.OrderBy(row => row.Id),
                row => row.Id,
                PureQLFrame.Rows(count.Value, 0),
                window => new ValueTuple<Reading, long>(
                    window.Row,
                    window.Rows.LongCount()
                )
            )
        );

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Rewriter.Rewritten(tree));
    }

    private static Expression Ranked<TResult>(
        Expression<Func<PureQLWindow<Reading>, TResult>> result
    )
    {
        return Framed(PureQLFrame.Partition, result);
    }

    private static Expression Framed<TResult>(
        PureQLFrame frame,
        Expression<Func<PureQLWindow<Reading>, TResult>> result
    )
    {
        return Expression.Call(
            typeof(PureQLWindows)
                .GetMethods()
                .Single(method =>
                    method.Name == nameof(PureQLWindows.Over)
                    && method.GetParameters()[0].ParameterType.GetGenericTypeDefinition()
                        == typeof(IQueryable<>)
                )
                .MakeGenericMethod(
                    typeof(Reading),
                    typeof(long),
                    typeof(decimal?),
                    typeof(TResult)
                ),
            Expression.Parameter(typeof(IQueryable<Reading>), "rows"),
            Expression.Quote((Expression<Func<Reading, long>>)(row => row.Id)),
            Expression.Quote(
                (Expression<Func<IEnumerable<Reading>, IEnumerable<Reading>>>)(
                    partition => partition.OrderBy(row => row.Amount)
                )
            ),
            Expression.Quote((Expression<Func<Reading, decimal?>>)(row => row.Amount)),
            Expression.Constant(frame),
            Expression.Quote(result)
        );
    }

    // A window over the rows of another, counting them.
    private static Expression Over(Expression rows)
    {
        ParameterExpression source = Expression.Parameter(rows.Type, "source");
        Type element = rows.Type.GetGenericArguments()[0];

        return
            typeof(WindowTests)
                .GetMethod(
                    nameof(Counted),
                    System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Static
                )!
                .MakeGenericMethod(element)
                .Invoke(null, [rows])
                is Expression counted
            ? counted
            : source;
    }

    private static Expression Counted<TRow>(Expression rows)
    {
        Expression<Func<IQueryable<TRow>, IQueryable<ValueTuple<TRow, long>>>> tree =
            source =>
                source.Over(
                    row => 0L,
                    partition => partition,
                    row => 0L,
                    PureQLFrame.Partition,
                    window => new ValueTuple<TRow, long>(
                        window.Row,
                        window.Rows.LongCount()
                    )
                );

        return new Replaced(tree.Parameters[0], rows).Visit(tree.Body);
    }

    private static Expression Tree<TResult>(
        Expression<Func<IQueryable<Reading>, IQueryable<TResult>>> query
    )
    {
        return query.Body;
    }

    private static string Rewritten(Expression tree)
    {
        return TreeText.Of(Rewriter.Rewritten(tree));
    }

    private sealed class Holder
    {
        public long Value { get; init; }
    }

    private sealed class Replaced(ParameterExpression parameter, Expression by)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == parameter ? by : node;
        }
    }
}
