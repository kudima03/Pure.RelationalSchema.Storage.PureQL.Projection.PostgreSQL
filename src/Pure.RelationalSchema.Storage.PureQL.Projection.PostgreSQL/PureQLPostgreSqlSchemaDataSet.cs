using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Pure.Collections.Generic;
using Pure.RelationalSchema.Abstractions.Schema;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.HashCodes;
using Pure.RelationalSchema.Storage.Abstractions;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL;

// A schema dataset whose tables run the trees of a PureQLProjection on
// PostgreSQL as far as PostgreSQL computes what the PureQL specification
// defines: the origin's tables, each with a provider that rewrites the
// interpreter's own operators and functions in a tree into the markers and
// the BCL shapes of Linq.Expressions.Translation.PostgreSql, and hands the
// tree to the origin's provider. Only what PostgreSQL then computes exactly as
// the specification is rewritten (Rewriter); anything else is left as it is,
// so the origin's provider refuses it with NotSupportedException and the
// projection runs the query in memory, as it would without this dataset.
//
// A table is the origin's table in every other respect: it has its schema
// and its rows, and LINQ composed over it runs on the origin's provider. A
// table held in memory (LINQ to Objects) is the origin's table itself.
public sealed record PureQLPostgreSqlSchemaDataSet : IStoredSchemaDataSet
{
    private readonly IStoredSchemaDataSet _origin;

    private readonly IReadOnlyDictionary<ITable, IStoredTableDataSet> _tables;

    public PureQLPostgreSqlSchemaDataSet(IStoredSchemaDataSet origin)
    {
        _origin = origin;
        _tables = new Dictionary<
            KeyValuePair<ITable, IStoredTableDataSet>,
            ITable,
            IStoredTableDataSet
        >(
            origin,
            table => table.Key,
            table => RewritingTable.Of(table.Value),
            table => new TableHash(table)
        );
    }

    public ISchema Schema => _origin.Schema;

    public IEnumerable<ITable> Keys => _tables.Keys;

    public IEnumerable<IStoredTableDataSet> Values => _tables.Values;

    public int Count => _tables.Count;

    public IStoredTableDataSet this[ITable key] => _tables[key];

    public bool ContainsKey(ITable key)
    {
        return _tables.ContainsKey(key);
    }

    public bool TryGetValue(
        ITable key,
        [MaybeNullWhen(false)] out IStoredTableDataSet value
    )
    {
        return _tables.TryGetValue(key, out value);
    }

    public IEnumerator<KeyValuePair<ITable, IStoredTableDataSet>> GetEnumerator()
    {
        return _tables.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
