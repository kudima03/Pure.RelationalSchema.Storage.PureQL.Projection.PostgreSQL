using Pure.Primitives.Bool;
using Pure.Primitives.Date;
using Pure.Primitives.Number;
using Pure.Primitives.Time;
using Pure.RelationalSchema.Storage.PureQL.Projection.Parameters.ListParameter;
using Pure.RelationalSchema.Storage.PureQL.Projection.Parameters.Parameter;
using DateTime = Pure.Primitives.DateTime.DateTime;
using Decimal = Pure.Primitives.Number.Decimal;
using Guid = Pure.Primitives.Guid.Guid;
using IParameter = Pure.RelationalSchema.Storage.PureQL.Projection.Parameters.Abstractions.Parameter.IParameter;
using String = Pure.Primitives.String.String;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;

// A value for every parameter of every sample that has parameters, each of
// the one type the catalogue gives it, as the interpreter's own sample tests
// bind them.
internal static class SampleParameters
{
    public static IReadOnlyDictionary<string, IParameter> Values { get; } =
        new Dictionary<string, IParameter>
        {
            ["min_age"] = new DecimalParameter(new Decimal(18m)),
            ["min_total"] = new DecimalParameter(new Decimal(100m)),
            ["min_orders"] = new IntegerParameter(new Long(2)),
            ["statuses"] = new StringListParameter([
                new String("paid"),
                new String("shipped"),
            ]),
            ["currency"] = new StringParameter(new String("USD")),
            ["since"] = new DateTimeParameter(
                new DateTime(new Date(new DateOnly(2024, 1, 1)))
            ),
            ["filter_by_status"] = new BooleanParameter(new True()),
            ["filter_by_user"] = new BooleanParameter(new False()),
            ["user_ids"] = new UuidListParameter([
                new Guid(new System.Guid("00000001-0000-0000-0000-000000000000")),
            ]),
            ["fraction"] = new DecimalParameter(new Decimal(0.5m)),
            ["group"] = new IntegerParameter(new Long(1)),
            ["zone"] = new StringParameter(new String("Europe/Berlin")),
            ["pattern"] = new StringParameter(new String("([a-z]+)")),
            ["separator"] = new StringParameter(new String(", ")),
            ["offset"] = new IntegerParameter(new Long(2)),
            ["buckets"] = new IntegerParameter(new Long(3)),
            ["page_size"] = new IntegerParameter(new Long(2)),
            ["skipped_users"] = new IntegerParameter(new Long(1)),
            ["user_id"] = new UuidParameter(
                new Guid(new System.Guid("00000001-0000-0000-0000-000000000000"))
            ),
            ["status"] = new NullableStringParameter(),
            ["placed_since"] = new NullableDateTimeParameter(),
            ["tax_rate"] = new DecimalParameter(new Decimal(0.2m)),
            ["as_of"] = new DateParameter(new Date(new DateOnly(2024, 3, 1))),
            ["note"] = new StringParameter(new String("net of tax")),
            ["counts"] = new IntegerListParameter([new Long(1), new Long(2)]),
            ["round_totals"] = new DecimalListParameter([
                new Decimal(100m),
                new Decimal(250.5m),
            ]),
            ["use_integer"] = new BooleanParameter(new True()),
            ["use_decimal"] = new BooleanParameter(new True()),
            ["use_string"] = new BooleanParameter(new True()),
            ["use_boolean"] = new BooleanParameter(new True()),
            ["use_date"] = new BooleanParameter(new True()),
            ["use_time"] = new BooleanParameter(new True()),
            ["use_datetime"] = new BooleanParameter(new True()),
            ["use_uuid"] = new BooleanParameter(new True()),
            ["integers"] = new IntegerListParameter([]),
            ["decimals"] = new DecimalListParameter([]),
            ["strings"] = new StringListParameter([]),
            ["booleans"] = new BooleanListParameter([]),
            ["dates"] = new DateListParameter([]),
            ["times"] = new TimeListParameter([]),
            ["datetimes"] = new DateTimeListParameter([]),
            ["uuids"] = new UuidListParameter([]),
            ["amount"] = new NullableIntegerParameter(),
            ["label"] = new StringParameter(new String("from parameters")),
            ["flag"] = new BooleanParameter(new True()),
            ["day"] = new DateParameter(new Date(new DateOnly(2024, 2, 29))),
            ["at"] = new DateTimeParameter(
                new DateTime(
                    new Date(new DateOnly(2024, 2, 29)),
                    new Time(new TimeOnly(12, 0))
                )
            ),
            ["time"] = new TimeParameter(new Time(new TimeOnly(23, 59, 59))),
            ["id"] = new UuidParameter(
                new Guid(new System.Guid("00000002-0000-0000-0000-000000000000"))
            ),
        };
}
