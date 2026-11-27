using System.Globalization;
using System.Linq.Expressions;
using Linq.Expressions.Translation.PostgreSql;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;
using Reading = (long Id, System.DateOnly? Day, string Label, decimal? Amount);

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Rewriting;

// The edges of the window rewriting: the 63 characters of a column name,
// the first of several sort keys as the key of a range, bucket counts and
// offsets at the limits of an int, a window without an order, and frames at
// the bounds of the calendar.
public sealed class WindowEdgeTests
{
    // Two leaf paths X.Item1 and X.Item2 differ in their last character: with
    // X of 57 characters they are 63 long and distinct, with X of 58 they share
    // the first 63, which PostgreSQL keeps of a name.
    [Theory]
    [InlineData(8, 3, true)]
    [InlineData(2, 8, true)]
    [InlineData(7, 4, false)]
    [InlineData(1, 9, false)]
    public void ColumnNamesAreDistinctUpTo63Characters(
        int rests,
        int items,
        bool distinct
    )
    {
        Type type = typeof(ValueTuple<long, long>);
        int length = (rests * 4) + (items * 5) + (rests + items - 1) + ".Item1".Length;

        for (int item = 0; item < items; item++)
        {
            type = typeof(ValueTuple<,>).MakeGenericType(type, typeof(long));
        }

        for (int rest = 0; rest < rests; rest++)
        {
            type = typeof(ValueTuple<,,,,,,,>).MakeGenericType(
                typeof(long),
                typeof(long),
                typeof(long),
                typeof(long),
                typeof(long),
                typeof(long),
                typeof(long),
                type
            );
        }

        Assert.Equal(distinct ? 63 : 64, length);
        Assert.Equal(distinct, ColumnNames.AreDistinct(type));
    }

    // A range is written in the unit of the first sort key, the OrderBy at the
    // root of a ThenBy chain.
    [Fact]
    public void RangeIsOfTheFirstOfSeveralSortKeys()
    {
        Expression tree = Window(
            partition =>
                partition
                    .OrderBy(row => row.Day)
                    .ThenBy(row => row.Id)
                    .ThenByDescending(row => row.Label, StringComparer.Ordinal),
            PureQLFrame.Range(false, 1, PureQLRangeUnit.Days, 0, PureQLRangeUnit.Number),
            window => new ValueTuple<Reading, long>(window.Row, window.Rows.LongCount())
        );

        Assert.Contains(
            "[RANGE BETWEEN INTERVAL '86400 seconds' PRECEDING AND CURRENT ROW]",
            Rewritten(tree),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void RangeOfAllOverANullableFirstKeyIsADistance()
    {
        Expression tree = Window(
            partition =>
                partition.OrderBy(row => row.Amount).ThenByDescending(row => row.Id),
            PureQLFrame.Range(
                false,
                null,
                PureQLRangeUnit.Number,
                0,
                PureQLRangeUnit.Number
            ),
            window => new ValueTuple<Reading, long>(window.Row, window.Rows.LongCount())
        );

        Assert.Contains(
            "[RANGE BETWEEN 79228162514264337593543950335 PRECEDING AND CURRENT ROW]",
            Rewritten(tree),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void StringSortKeyKeepsItsOrdinalComparer()
    {
        Expression tree = Window(
            partition => partition.OrderBy(row => row.Label, StringComparer.Ordinal),
            PureQLFrame.Partition,
            window => new ValueTuple<Reading, long>(window.Row, window.RowNumber)
        );

        Assert.Equal(
            "PostgreSqlWindows.Over(rows, row => (object)row.Item1, "
                + "partition => partition.OrderBy(row => row.Item3, StringComparer.Ordinal), "
                + "[ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING], "
                + "(row, window) => (row, PostgreSqlWindows.RowNumber(window)))",
            Rewritten(tree)
        );
    }

    public static TheoryData<long, string> Buckets =>
        new()
        {
            { 1L, "1" },
            { 2_147_483_646L, "2147483646" },
            { 2_147_483_647L, "2147483647" },
            { 2_147_483_648L, "2147483647" },
            { 3_000_000_000L, "2147483647" },
        };

    [Theory]
    [MemberData(nameof(Buckets))]
    public void NtileTakesTheBucketsAsAnInt(long buckets, string written)
    {
        Holder holder = new() { Value = buckets };
        Expression tree = Ordered(window => new ValueTuple<Reading, long>(
            window.Row,
            window.Ntile(holder.Value)
        ));

        Assert.EndsWith(
            "(row, window) => (row, (long)PostgreSqlWindows.Ntile(window, "
                + written
                + ")))",
            Rewritten(tree),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void NtileOfNoBucketsIsKept(long buckets)
    {
        Holder holder = new() { Value = buckets };
        Expression tree = Ordered(window => new ValueTuple<Reading, long>(
            window.Row,
            window.Ntile(holder.Value)
        ));

        Assert.StartsWith(
            "PureQLWindows.Over(",
            Rewritten(tree),
            StringComparison.Ordinal
        );
    }

    public static TheoryData<long, string> Shifts =>
        new()
        {
            { 2_147_483_647L, "Lead(window, other => other.Item4, 2147483647, null)" },
            { -2_147_483_647L, "Lag(window, other => other.Item4, 2147483647, null)" },
            { 1L, "Lead(window, other => other.Item4, 1, null)" },
            { -1L, "Lag(window, other => other.Item4, 1, null)" },
        };

    [Theory]
    [MemberData(nameof(Shifts))]
    public void ShiftWithinAnIntIsLagOrLead(long shift, string marker)
    {
        Holder holder = new() { Value = shift };
        Expression tree = Ordered(window => new ValueTuple<Reading, decimal?>(
            window.Row,
            window.Has(holder.Value) ? window.At(holder.Value).Amount : null
        ));

        Assert.EndsWith(
            "(row, window) => (row, PostgreSqlWindows." + marker + "))",
            Rewritten(tree),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(-2_147_483_648L)]
    [InlineData(2_147_483_648L)]
    [InlineData(long.MaxValue)]
    [InlineData(0L)]
    public void ShiftPastAnIntIsKept(long shift)
    {
        Holder holder = new() { Value = shift };
        Expression tree = Ordered(window => new ValueTuple<Reading, decimal?>(
            window.Row,
            window.Has(holder.Value) ? window.At(holder.Value).Amount : null
        ));

        Assert.StartsWith(
            "PureQLWindows.Over(",
            Rewritten(tree),
            StringComparison.Ordinal
        );
    }

    // The default of LAG and LEAD is read in the current row.
    [Fact]
    public void DefaultOfAShiftReadsTheCurrentRow()
    {
        Expression tree = Ordered(window => new ValueTuple<Reading, string>(
            window.Row,
            window.Has(-2L) ? window.At(-2L).Label : window.Row.Label + "!"
        ));

        Assert.EndsWith(
            "PostgreSqlWindows.Lag(window, other => other.Item3, 2, "
                + "(row.Item3 + \"!\"))))",
            Rewritten(tree),
            StringComparison.Ordinal
        );
    }

    // A shift whose operand also reads another neighbour, or the window's
    // rows, is not one LAG or LEAD.
    [Fact]
    public void ShiftReadingAnotherNeighbourIsKept()
    {
        Expression tree = Ordered(window => new ValueTuple<Reading, long>(
            window.Row,
            window.Has(-1L) ? window.At(-1L).Id + window.Rows.LongCount() : 0L
        ));

        Assert.StartsWith(
            "PureQLWindows.Over(",
            Rewritten(tree),
            StringComparison.Ordinal
        );
    }

    // Without an order every row of the partition is a peer of every other;
    // the ratios, which need the rows of the partition, stay in memory, the
    // ranks are those of peers.
    [Fact]
    public void RatioOfAWindowWithoutAnOrderIsKept()
    {
        Expression percent = Window(
            partition => partition,
            PureQLFrame.Partition,
            window => new ValueTuple<Reading, decimal>(window.Row, window.PercentRank)
        );
        Expression cumulative = Window(
            partition => partition,
            PureQLFrame.Partition,
            window => new ValueTuple<Reading, decimal>(
                window.Row,
                window.CumulativeDistribution
            )
        );

        Assert.StartsWith(
            "PureQLWindows.Over(",
            Rewritten(percent),
            StringComparison.Ordinal
        );
        Assert.StartsWith(
            "PureQLWindows.Over(",
            Rewritten(cumulative),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void RankOfAWindowWithoutAnOrderHasNoOrder()
    {
        Expression tree = Window(
            partition => partition,
            PureQLFrame.Partition,
            window => new ValueTuple<Reading, long>(window.Row, window.Rank)
        );

        Assert.Equal(
            "PostgreSqlWindows.Over(rows, row => (object)row.Item1, null, null, "
                + "(row, window) => (row, PostgreSqlWindows.Rank(window)))",
            Rewritten(tree)
        );
    }

    // Frames at the bounds of the calendar and of the integers, which
    // FramesTests does not reach.
    public static TheoryData<
        bool,
        string,
        string?,
        PureQLRangeUnit,
        string?,
        PureQLRangeUnit,
        string?
    > Bounds =>
        new()
        {
            {
                false,
                "date?",
                "1721000",
                PureQLRangeUnit.Days,
                "0",
                PureQLRangeUnit.Number,
                "RANGE BETWEEN INTERVAL '148694400000 seconds' PRECEDING AND CURRENT ROW"
            },
            {
                false,
                "datetime?",
                "148694400000",
                PureQLRangeUnit.Seconds,
                "0",
                PureQLRangeUnit.Number,
                "RANGE BETWEEN INTERVAL '148694400000 seconds' PRECEDING AND CURRENT ROW"
            },
            {
                false,
                "datetime?",
                "148694400000.000001",
                PureQLRangeUnit.Seconds,
                "0",
                PureQLRangeUnit.Number,
                null
            },
            {
                false,
                "datetime",
                "315537897600",
                PureQLRangeUnit.Seconds,
                "0",
                PureQLRangeUnit.Number,
                "RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW"
            },
            {
                false,
                "datetime",
                "315537897599",
                PureQLRangeUnit.Seconds,
                "0",
                PureQLRangeUnit.Number,
                null
            },
            {
                false,
                "date?",
                "0",
                PureQLRangeUnit.Number,
                "119987",
                PureQLRangeUnit.Months,
                "RANGE BETWEEN CURRENT ROW AND INTERVAL '119987 months' FOLLOWING"
            },
            {
                false,
                "date?",
                "0",
                PureQLRangeUnit.Number,
                "119988",
                PureQLRangeUnit.Months,
                "RANGE BETWEEN CURRENT ROW AND INTERVAL '119988 months' FOLLOWING"
            },
            {
                false,
                "date",
                "0",
                PureQLRangeUnit.Number,
                "119988",
                PureQLRangeUnit.Months,
                "RANGE BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING"
            },
            {
                false,
                "date?",
                "56000",
                PureQLRangeUnit.Months,
                "0",
                PureQLRangeUnit.Number,
                "RANGE BETWEEN INTERVAL '56000 months' PRECEDING AND CURRENT ROW"
            },
            {
                true,
                "date",
                "1",
                PureQLRangeUnit.Days,
                null,
                PureQLRangeUnit.Number,
                "RANGE BETWEEN INTERVAL '86400 seconds' PRECEDING AND UNBOUNDED FOLLOWING"
            },
            {
                true,
                "datetime?",
                null,
                PureQLRangeUnit.Number,
                "60",
                PureQLRangeUnit.Seconds,
                "RANGE BETWEEN INTERVAL '315537897600 seconds' PRECEDING AND INTERVAL '60 seconds' FOLLOWING"
            },
            {
                true,
                "date?",
                "0",
                PureQLRangeUnit.Number,
                "1721001",
                PureQLRangeUnit.Days,
                null
            },
            {
                true,
                "long?",
                "9223372036854775807",
                PureQLRangeUnit.Number,
                null,
                PureQLRangeUnit.Number,
                "RANGE BETWEEN 9223372036854775807 PRECEDING AND 9223372036854775807 FOLLOWING"
            },
            {
                false,
                "long",
                "1",
                PureQLRangeUnit.Number,
                null,
                PureQLRangeUnit.Number,
                "RANGE BETWEEN 1 PRECEDING AND UNBOUNDED FOLLOWING"
            },
            {
                false,
                "decimal?",
                "0.0000000000000000000000000001",
                PureQLRangeUnit.Number,
                "9999999999999999999999999999",
                PureQLRangeUnit.Number,
                "RANGE BETWEEN 0.0000000000000000000000000001 PRECEDING AND 9999999999999999999999999999 FOLLOWING"
            },
        };

    [Theory]
    [MemberData(nameof(Bounds))]
    public void FrameAtTheBoundsIsWrittenOrKept(
        bool descending,
        string key,
        string? preceding,
        PureQLRangeUnit precedingUnit,
        string? following,
        PureQLRangeUnit followingUnit,
        string? sql
    )
    {
        PureQLFrame frame = PureQLFrame.Range(
            descending,
            Distance(preceding),
            precedingUnit,
            Distance(following),
            followingUnit
        );

        Assert.Equal(
            sql,
            Frames.Of(frame, true, Key(key)).Written(out PostgreSqlFrame? written)
                ? written!.TextValue
                : null
        );
    }

    [Fact]
    public void FrameOfRowsWithoutAnOrderIsStillRows()
    {
        Assert.True(
            Frames
                .Of(PureQLFrame.Rows(1, null), false, null)
                .Written(out PostgreSqlFrame? frame)
        );
        Assert.Equal(
            "ROWS BETWEEN 1 PRECEDING AND UNBOUNDED FOLLOWING",
            frame!.TextValue
        );
    }

    private static Expression Ordered<TResult>(
        Expression<Func<PureQLWindow<Reading>, TResult>> result
    )
    {
        return Window(
            partition => partition.OrderBy(row => row.Day).ThenBy(row => row.Id),
            PureQLFrame.Partition,
            result
        );
    }

    private static Expression Window<TResult>(
        Expression<Func<IEnumerable<Reading>, IEnumerable<Reading>>> orderBy,
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
                    typeof(ValueTuple<DateOnly?, long>),
                    typeof(TResult)
                ),
            Expression.Parameter(typeof(IQueryable<Reading>), "rows"),
            Expression.Quote((Expression<Func<Reading, long>>)(row => row.Id)),
            Expression.Quote(orderBy),
            Expression.Quote(
                (Expression<Func<Reading, ValueTuple<DateOnly?, long>>>)(
                    row => new ValueTuple<DateOnly?, long>(row.Day, row.Id)
                )
            ),
            Expression.Constant(frame),
            Expression.Quote(result)
        );
    }

    private static string Rewritten(Expression tree)
    {
        return TreeText.Of(Rewriter.Rewritten(tree));
    }

    private static decimal? Distance(string? text)
    {
        return text is null ? null : decimal.Parse(text, CultureInfo.InvariantCulture);
    }

    private static Type Key(string name)
    {
        return name switch
        {
            "long" => typeof(long),
            "long?" => typeof(long?),
            "decimal" => typeof(decimal),
            "decimal?" => typeof(decimal?),
            "date" => typeof(DateOnly),
            "date?" => typeof(DateOnly?),
            "datetime" => typeof(DateTime),
            _ => typeof(DateTime?),
        };
    }

    private sealed class Holder
    {
        public long Value { get; init; }
    }
}
