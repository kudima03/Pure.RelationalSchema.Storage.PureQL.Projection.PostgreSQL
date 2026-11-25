using System.Globalization;
using Linq.Expressions.Translation.PostgreSql;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Rewriting;

// The frame clause of every form of PureQL frame, or none where PostgreSQL
// has no frame that holds the same rows.
public sealed class FramesTests
{
    private const PureQLRangeUnit Number = PureQLRangeUnit.Number;

    private const PureQLRangeUnit Days = PureQLRangeUnit.Days;

    private const PureQLRangeUnit Seconds = PureQLRangeUnit.Seconds;

    private const PureQLRangeUnit Months = PureQLRangeUnit.Months;

    public static TheoryData<long?, long?, string> RowFrames =>
        new()
        {
            { 6, 0, "ROWS BETWEEN 6 PRECEDING AND CURRENT ROW" },
            { null, 0, "ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW" },
            { 0, null, "ROWS BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING" },
            { 1, 1, "ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING" },
            { 0, 2, "ROWS BETWEEN CURRENT ROW AND 2 FOLLOWING" },
            { 2, null, "ROWS BETWEEN 2 PRECEDING AND UNBOUNDED FOLLOWING" },
            { 0, 0, "ROWS BETWEEN CURRENT ROW AND CURRENT ROW" },
            {
                long.MaxValue,
                long.MaxValue,
                "ROWS BETWEEN 9223372036854775807 PRECEDING AND 9223372036854775807 FOLLOWING"
            },
        };

    [Theory]
    [MemberData(nameof(RowFrames))]
    public void RowsAreCountedFromTheCurrentRow(
        long? preceding,
        long? following,
        string sql
    )
    {
        Assert.Equal(sql, Written(PureQLFrame.Rows(preceding, following), typeof(long?)));
    }

    [Fact]
    public void WholePartitionWithoutAnOrderIsNoFrame()
    {
        Assert.True(
            Frames
                .Of(PureQLFrame.Partition, false, null)
                .Written(out PostgreSqlFrame? frame)
        );
        Assert.Null(frame);
    }

    [Fact]
    public void RangeOfAWindowWithoutAKeyTypeIsOverAKeyThatMayBeNull()
    {
        Assert.True(
            Frames
                .Of(PureQLFrame.Range(false, null, Number, 0, Number), true, null)
                .Written(out PostgreSqlFrame? frame)
        );
        Assert.Equal(
            "RANGE BETWEEN 79228162514264337593543950335 PRECEDING AND CURRENT ROW",
            frame!.TextValue
        );
    }

    [Fact]
    public void WholePartitionWithAnOrderIsEveryRowOfIt()
    {
        Assert.Equal(
            "ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING",
            Written(PureQLFrame.Partition, typeof(long))
        );
    }

    public static TheoryData<
        bool,
        string,
        string?,
        PureQLRangeUnit,
        string?,
        PureQLRangeUnit,
        string?
    > Ranges =>
        new()
        {
            // Numbers.
            {
                false,
                "long",
                "5",
                Number,
                "0",
                Number,
                "RANGE BETWEEN 5 PRECEDING AND CURRENT ROW"
            },
            {
                false,
                "long",
                "0",
                Number,
                "10",
                Number,
                "RANGE BETWEEN CURRENT ROW AND 10 FOLLOWING"
            },
            {
                true,
                "long",
                "5",
                Number,
                "0",
                Number,
                "RANGE BETWEEN 5 PRECEDING AND CURRENT ROW"
            },
            {
                false,
                "decimal",
                "5.5",
                Number,
                "5.5",
                Number,
                "RANGE BETWEEN 5.5 PRECEDING AND 5.5 FOLLOWING"
            },
            {
                false,
                "decimal",
                "25",
                Number,
                "25.25",
                Number,
                "RANGE BETWEEN 25 PRECEDING AND 25.25 FOLLOWING"
            },
            {
                false,
                "long",
                null,
                Number,
                "0",
                Number,
                "RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW"
            },
            {
                false,
                "long",
                "0",
                Number,
                null,
                Number,
                "RANGE BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING"
            },
            {
                false,
                "long?",
                null,
                Number,
                "0",
                Number,
                "RANGE BETWEEN 9223372036854775807 PRECEDING AND CURRENT ROW"
            },
            {
                false,
                "long?",
                "1",
                Number,
                null,
                Number,
                "RANGE BETWEEN 1 PRECEDING AND 9223372036854775807 FOLLOWING"
            },
            {
                false,
                "decimal?",
                null,
                Number,
                "0",
                Number,
                "RANGE BETWEEN 79228162514264337593543950335 PRECEDING AND CURRENT ROW"
            },
            {
                false,
                "decimal",
                null,
                Number,
                "0",
                Number,
                "RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW"
            },
            // Days, as an interval of seconds.
            {
                false,
                "date",
                "29",
                Days,
                "0",
                Number,
                "RANGE BETWEEN INTERVAL '2505600 seconds' PRECEDING AND CURRENT ROW"
            },
            {
                false,
                "date",
                "0",
                Number,
                "2",
                Days,
                "RANGE BETWEEN CURRENT ROW AND INTERVAL '172800 seconds' FOLLOWING"
            },
            {
                false,
                "date?",
                "1",
                Days,
                "1",
                Days,
                "RANGE BETWEEN INTERVAL '86400 seconds' PRECEDING AND INTERVAL '86400 seconds' FOLLOWING"
            },
            {
                false,
                "date",
                null,
                Number,
                "0",
                Number,
                "RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW"
            },
            { false, "date?", null, Number, "0", Number, null },
            {
                false,
                "date?",
                "0",
                Number,
                null,
                Number,
                "RANGE BETWEEN CURRENT ROW AND INTERVAL '315537897600 seconds' FOLLOWING"
            },
            {
                false,
                "date",
                "0",
                Number,
                null,
                Number,
                "RANGE BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING"
            },
            {
                false,
                "date",
                "1721000",
                Days,
                "0",
                Number,
                "RANGE BETWEEN INTERVAL '148694400000 seconds' PRECEDING AND CURRENT ROW"
            },
            { false, "date", "1721001", Days, "0", Number, null },
            { false, "date?", "1721001", Days, "0", Number, null },
            {
                false,
                "date",
                "3652059",
                Days,
                "0",
                Number,
                "RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW"
            },
            { false, "date?", "3652059", Days, "0", Number, null },
            {
                false,
                "date",
                "0",
                Number,
                "5000000",
                Days,
                "RANGE BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING"
            },
            {
                false,
                "date?",
                "0",
                Number,
                "5000000",
                Days,
                "RANGE BETWEEN CURRENT ROW AND INTERVAL '315537897600 seconds' FOLLOWING"
            },
            // Seconds, rounded to microseconds half away from zero.
            {
                false,
                "datetime",
                "90000",
                Seconds,
                "90000",
                Seconds,
                "RANGE BETWEEN INTERVAL '90000 seconds' PRECEDING AND INTERVAL '90000 seconds' FOLLOWING"
            },
            {
                false,
                "datetime",
                "172800.5",
                Seconds,
                "0",
                Number,
                "RANGE BETWEEN INTERVAL '172800.5 seconds' PRECEDING AND CURRENT ROW"
            },
            {
                false,
                "datetime",
                "0.0000005",
                Seconds,
                "0",
                Number,
                "RANGE BETWEEN INTERVAL '0.000001 seconds' PRECEDING AND CURRENT ROW"
            },
            { false, "datetime?", null, Number, "0", Number, null },
            {
                false,
                "datetime?",
                "0",
                Number,
                null,
                Number,
                "RANGE BETWEEN CURRENT ROW AND INTERVAL '315537897600 seconds' FOLLOWING"
            },
            // Months.
            {
                false,
                "date",
                "11",
                Months,
                "0",
                Number,
                "RANGE BETWEEN INTERVAL '11 months' PRECEDING AND CURRENT ROW"
            },
            {
                false,
                "datetime",
                "1",
                Months,
                "1",
                Months,
                "RANGE BETWEEN INTERVAL '1 months' PRECEDING AND INTERVAL '1 months' FOLLOWING"
            },
            {
                false,
                "date",
                "0",
                Number,
                "200000",
                Months,
                "RANGE BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING"
            },
            {
                false,
                "date?",
                "0",
                Number,
                "200000",
                Months,
                "RANGE BETWEEN CURRENT ROW AND INTERVAL '119988 months' FOLLOWING"
            },
            { false, "date", "56001", Months, "0", Number, null },
            {
                false,
                "date",
                null,
                Number,
                "0",
                Number,
                "RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW"
            },
            // Descending, the side after the row reaches smaller dates.
            { true, "date?", "1", Days, null, Number, null },
            {
                true,
                "date?",
                null,
                Number,
                "1",
                Days,
                "RANGE BETWEEN INTERVAL '315537897600 seconds' PRECEDING AND INTERVAL '86400 seconds' FOLLOWING"
            },
            { true, "date", "0", Number, "1721001", Days, null },
            {
                true,
                "date",
                "2000000",
                Days,
                "0",
                Number,
                "RANGE BETWEEN INTERVAL '172800000000 seconds' PRECEDING AND CURRENT ROW"
            },
            { true, "datetime?", "0", Number, null, Number, null },
            {
                true,
                "date?",
                "56001",
                Months,
                "1",
                Months,
                "RANGE BETWEEN INTERVAL '56001 months' PRECEDING AND INTERVAL '1 months' FOLLOWING"
            },
            // Two units, which one interval cannot hold.
            { false, "date", "1", Days, "1", Months, null },
            { false, "datetime", "60", Seconds, "1", Months, null },
        };

    [Theory]
    [MemberData(nameof(Ranges))]
    public void RangeIsWrittenInTheUnitOfItsKey(
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

        Assert.Equal(sql, Written(frame, Key(key)));
    }

    private static string? Written(PureQLFrame frame, Type key)
    {
        return Frames.Of(frame, true, key).Written(out PostgreSqlFrame? written)
            ? written!.TextValue
            : null;
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
}
