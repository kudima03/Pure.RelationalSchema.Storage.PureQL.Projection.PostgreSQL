using Pure.RelationalSchema.ColumnType;
using Pure.RelationalSchema.Storage.Abstractions;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;

// The rows the construct tests read, entity t.readings: partitions of
// several rows, of one row and of a null key, ties on every sort key, nulls
// in every nullable column, month ends and leap days, the first and the last
// day of the calendar, fractions of seconds, and text outside ASCII.
internal static class Readings
{
    public static IStoredSchemaDataSet Dataset { get; } =
        new InlineTable(
            "t",
            "readings",
            ("id", new LongColumnType()),
            ("grp", new StringColumnType()),
            ("n", new LongColumnType()),
            ("amount", new DoubleColumnType()),
            ("day", new DateColumnType()),
            ("at", new DateTimeColumnType()),
            ("clock", new TimeColumnType()),
            ("label", new StringColumnType()),
            ("uid", new UuidColumnType())
        ).With(
            [
                "1",
                "a",
                "5",
                "1.5",
                "2024-01-31",
                "2024-01-31 10:00:00",
                "10:30:15.25",
                "Apple",
                "00000001-0000-0000-0000-000000000000",
            ],
            [
                "2",
                "a",
                "5",
                "1.5",
                "2024-02-29",
                "2024-02-29 23:59:59.999999",
                "00:00:00",
                "apple",
                "00000002-0000-0000-0000-000000000000",
            ],
            [
                "3",
                "a",
                "",
                "2.25",
                "2024-03-31",
                "2024-03-31 00:00:00",
                "23:59:59.999999",
                "Äpfel",
                "00000003-0000-0000-0000-000000000000",
            ],
            [
                "4",
                "b",
                "-3",
                "",
                "",
                "",
                "",
                "Straße",
                "00000004-0000-0000-0000-000000000000",
            ],
            [
                "5",
                "b",
                "12",
                "-7.5",
                "2023-12-31",
                "2023-12-31 12:00:00",
                "12:00:00",
                "😀x",
                "00000005-0000-0000-0000-000000000000",
            ],
            [
                "6",
                "",
                "0",
                "0",
                "2024-01-01",
                "2024-01-01 00:00:00",
                "01:02:03",
                "z",
                "00000006-0000-0000-0000-000000000000",
            ],
            [
                "7",
                "",
                "10",
                "100.125",
                "2024-01-01",
                "2024-06-15 18:30:15.25",
                "18:30:15.25",
                "zz",
                "00000007-0000-0000-0000-000000000000",
            ],
            [
                "8",
                "b",
                "10",
                "2.25",
                "0001-01-01",
                "0001-01-01 00:00:00",
                "05:00:00",
                "b,c,d",
                "00000008-0000-0000-0000-000000000000",
            ],
            [
                "9",
                "a",
                "-3",
                "",
                "9999-12-31",
                "9999-12-31 23:59:59",
                "05:00:00",
                "a-b-a-b",
                "00000009-0000-0000-0000-000000000000",
            ],
            [
                "10",
                "c",
                "7",
                "3",
                "2024-02-01",
                "2024-02-01 00:00:00",
                "05:00:00.000001",
                "c",
                "0000000a-0000-0000-0000-000000000000",
            ]
        );
}
