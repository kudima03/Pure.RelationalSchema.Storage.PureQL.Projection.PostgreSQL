using System.Runtime.CompilerServices;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL;

// The names the translator gives the columns of a derived table of rows of a
// type: the path of each value in the nested value tuples, Item1.Item2...
// PostgreSQL keeps the first 63 bytes of a name, so two paths that share them
// are one name, and a statement reading the table fails as ambiguous, where it
// should have been refused. A window adds a level of nesting to the rows it
// passes on (the row and its value), so a query of many windows reaches that
// length; such a window stays in memory.
internal static class ColumnNames
{
    private const int Length = 63;

    public static bool AreDistinct(Type rows)
    {
        List<string> paths = [];
        Paths(rows, null, paths);
        return paths
                .Select(path => path.Length > Length ? path[..Length] : path)
                .Distinct(StringComparer.Ordinal)
                .Count() == paths.Count;
    }

    private static void Paths(Type type, string? path, List<string> paths)
    {
        if (!typeof(ITuple).IsAssignableFrom(type) || !type.IsValueType)
        {
            paths.Add(path ?? "Value");
            return;
        }

        foreach (System.Reflection.FieldInfo field in type.GetFields())
        {
            Paths(
                field.FieldType,
                path is null ? field.Name : $"{path}.{field.Name}",
                paths
            );
        }
    }
}
