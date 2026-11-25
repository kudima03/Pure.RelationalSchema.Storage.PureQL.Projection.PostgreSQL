using Pure.RelationalSchema.Abstractions.Column;
using Pure.RelationalSchema.Storage.Abstractions;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;

// The exact text of a result: its columns (names and types, in order), then
// each row as the text of its cells, in order or sorted, so two results
// compare cell by cell, every digit of a decimal included.
internal static class ResultText
{
    public static IReadOnlyList<string> Of(IStoredTableDataSet table, bool ordered)
    {
        List<IColumn> columns = [.. table.TableSchema.Columns];
        IEnumerable<string> rows = table
            .AsEnumerable()
            .Select(row =>
                string.Join(
                    " | ",
                    columns.Select(column =>
                        row.Cells.Single(cell =>
                            cell.Key.Name.TextValue == column.Name.TextValue
                        ).Value.Value.TextValue
                    )
                )
            );

        return
        [
            string.Join(
                " | ",
                columns.Select(column =>
                    $"{column.Name.TextValue}:{column.Type.Name.TextValue}"
                )
            ),
            .. ordered ? rows : rows.Order(StringComparer.Ordinal),
        ];
    }
}
