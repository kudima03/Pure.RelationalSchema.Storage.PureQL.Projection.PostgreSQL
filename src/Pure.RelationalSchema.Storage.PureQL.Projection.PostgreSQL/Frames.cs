using Linq.Expressions.Translation.PostgreSql;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL;

// The frame clause PostgreSQL computes a PureQL frame with. Each side of a
// PostgreSqlFrame is an offset from the current row, negative before it, 0
// the current row (with its peers for a range) and null unbounded.
// - The whole partition: no frame without an order (PostgreSQL's default is
//   then the partition), ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED
//   FOLLOWING with one, whose default would end at the current row's peers.
// - Rows: the counts, "all" unbounded; a frame stops at the ends of the
//   partition in both.
// - A range of the one sort key: the distances in its unit, a number, days and
//   seconds (an interval of seconds) or months (an interval of months, the day
//   clamped to the end of a shorter month by PostgreSQL as by dateAddMonths).
//   Both include the bounds and the current row's peers, and give a row whose
//   key is null its null peers as its window. A bound past a number reaches
//   the end of the partition in both (PostgreSQL's in_range takes an overflow
//   as infinitely far). Two things differ, and are written around:
//   - "All" is UNBOUNDED, which reaches the null keys where the specification
//     keeps a row whose key is not null out of their window and them out of
//     its. Over a key that may be null it is a distance past every value of
//     the key's type instead, which never reaches a null key.
//   - PostgreSQL subtracts a distance from a date or a datetime as a
//     timestamp, which fails before 4714 BC. A distance towards smaller keys
//     (before the row ascending, after it descending) is written only up to
//     one that reaches no further back from 0001-01-01; a longer one is "all"
//     over a key that cannot be null when it spans the calendar, and
//     otherwise stays in memory, as "all" on that side does over a key that
//     may be null. Towards greater keys a distance past the calendar is the
//     calendar.
// A range whose two sides are in days and in months, which one interval cannot
// hold, stays in memory.
internal sealed class Frames
{
    private readonly PureQLFrame _frame;

    private readonly bool _ordered;

    private readonly Type? _key;

    private Frames(PureQLFrame frame, bool ordered, Type? key)
    {
        _frame = frame;
        _ordered = ordered;
        _key = key;
    }

    private bool NullableKey =>
        _key is null || Nullable.GetUnderlyingType(_key) is not null;

    private Type? KeyValueType =>
        _key is null ? null : Nullable.GetUnderlyingType(_key) ?? _key;

    // A frame, whether the window is ordered, and the type of its first sort
    // key, which a range has as its one key.
    public static Frames Of(PureQLFrame frame, bool ordered, Type? key)
    {
        return new Frames(frame, ordered, key);
    }

    // The frame clause, or false when PostgreSQL has none for the frame.
    public bool Written(out PostgreSqlFrame? frame)
    {
        frame = null;

        if (_frame.IsRows)
        {
            frame = PostgreSqlFrame.Rows(
                -(long?)_frame.Preceding,
                (long?)_frame.Following
            );
            return true;
        }

        if (!_frame.IsRange)
        {
            frame = _ordered ? PostgreSqlFrame.Rows(null, null) : null;
            return true;
        }

        return Unit() switch
        {
            PureQLRangeUnit.Number => Numbers(out frame),
            PureQLRangeUnit.Days => Interval(Days, out frame),
            PureQLRangeUnit.Seconds => Interval(Seconds, out frame),
            PureQLRangeUnit.Months => Interval(Months, out frame),
            _ => false,
        };
    }

    private static Span Days { get; } = new(1_721_000m, 3_652_059m);

    private static Span Seconds { get; } =
        new(1_721_000m * 86_400m, 3_652_059m * 86_400m);

    private static Span Months { get; } = new(56_000m, 119_988m);

    // The one unit the distances are written in: that of every side that is
    // neither absent (0) nor "all", or none for two units; with no such side,
    // that of the key's type, in which "all" is written.
    private PureQLRangeUnit? Unit()
    {
        PureQLRangeUnit[] units =
        [
            .. new[]
            {
                (Distance: _frame.Preceding, Unit: _frame.PrecedingUnit),
                (Distance: _frame.Following, Unit: _frame.FollowingUnit),
            }
                .Where(side => side.Distance is not (null or 0m))
                .Select(side => side.Unit)
                .Distinct(),
        ];

        return units.Length switch
        {
            0 => KeyValueType == typeof(DateOnly) ? PureQLRangeUnit.Days
            : KeyValueType == typeof(DateTime) ? PureQLRangeUnit.Seconds
            : PureQLRangeUnit.Number,
            1 => units[0],
            _ => null,
        };
    }

    // A numeric key: "all" over a key that may be null is the largest value of
    // its type, a distance no two values are apart by more.
    private bool Numbers(out PostgreSqlFrame? frame)
    {
        decimal? all =
            !NullableKey ? null
            : KeyValueType == typeof(long) ? long.MaxValue
            : decimal.MaxValue;

        frame = PostgreSqlFrame.Range(
            -(_frame.Preceding ?? all),
            _frame.Following ?? all
        );
        return true;
    }

    // The side that reaches smaller values, which PostgreSQL subtracts from
    // the key, is the preceding one ascending and the following one
    // descending.
    private bool Interval(Span span, out PostgreSqlFrame? frame)
    {
        frame = null;
        decimal? smaller = _frame.Descending ? _frame.Following : _frame.Preceding;
        decimal? greater = _frame.Descending ? _frame.Preceding : _frame.Following;

        (bool written, decimal? subtracted) = Before(smaller, span);

        if (!written)
        {
            return false;
        }

        decimal? added = After(greater, span);
        decimal? before = _frame.Descending ? added : subtracted;
        decimal? after = _frame.Descending ? subtracted : added;

        frame =
            span == Months
                ? PostgreSqlFrame.RangeMonths((int?)-before, (int?)after)
                : PostgreSqlFrame.Range(
                    Duration(-before, span == Days ? 86_400m : 1m),
                    Duration(after, span == Days ? 86_400m : 1m)
                );
        return true;
    }

    // The distance towards smaller values, null for unbounded, or false when
    // PostgreSQL cannot subtract it from every date.
    private (bool Written, decimal? Distance) Before(decimal? distance, Span span)
    {
        if (distance is null)
        {
            return (!NullableKey, null);
        }

        return distance <= span.Safe ? (true, distance)
            : !NullableKey && distance >= span.Calendar ? (true, null)
            : (false, null);
    }

    // The distance towards greater values: "all" or one past the calendar is
    // the calendar over a key that may be null, and unbounded otherwise.
    private decimal? After(decimal? distance, Span span)
    {
        return distance is decimal value && value < span.Calendar ? value
            : NullableKey ? span.Calendar
            : null;
    }

    private static TimeSpan? Duration(decimal? amount, decimal unit)
    {
        return amount is decimal value
            ? TimeSpan.FromTicks(
                (long)Math.Round(value * unit * 1_000_000m, MidpointRounding.AwayFromZero)
                    * TimeSpan.TicksPerMicrosecond
            )
            : null;
    }

    // Safe: the longest distance PostgreSQL subtracts from 0001-01-01 within
    // its range of timestamps; Calendar: a distance past any two values of the
    // calendar.
    private sealed record Span(decimal Safe, decimal Calendar);
}
