# Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL

Runs more PureQL queries as one PostgreSQL statement: a schema dataset that rewrites the trees of [`Pure.RelationalSchema.Storage.PureQL.Projection`](https://www.nuget.org/packages/Pure.RelationalSchema.Storage.PureQL.Projection) into the PostgreSQL markers and BCL shapes of [`Linq.Expressions.Translation.PostgreSql`](https://www.nuget.org/packages/Linq.Expressions.Translation.PostgreSql), wherever PostgreSQL then computes exactly what the [PureQL specification](https://github.com/kudima03/PureQL-Specification) defines.

[![.NET build & test](https://github.com/kudima03/Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL/actions/workflows/build-and-test.yml/badge.svg?branch=main)](https://github.com/kudima03/Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL/actions/workflows/build-and-test.yml)
[![Build and Deploy](https://github.com/kudima03/Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL/actions/workflows/publish-nuget.yml/badge.svg?branch=main)](https://github.com/kudima03/Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL/actions/workflows/publish-nuget.yml)
[![NuGet](https://img.shields.io/nuget/v/Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL)](https://www.nuget.org/packages/Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

## Overview

The interpreter compiles a PureQL query to one LINQ expression tree and hands it to the provider of its stored tables; [`Pure.RelationalSchema.Storage.PostgreSQL.Querying`](https://www.nuget.org/packages/Pure.RelationalSchema.Storage.PostgreSQL.Querying) translates it to SQL, or refuses it with `NotSupportedException`, and the interpreter then runs it in memory. The tree holds the interpreter's own operators and functions (`PureQLWindows`, `PureQLRecursion`, `PureQLText`, `PureQLMath`, `PureQLTime`, `PureQLConversions`, ...), which the provider does not know, as the provider and the translator know nothing of PureQL. This package is the one place where the two meet.

```csharp
IStoredSchemaDataSet stored = new PostgreSqlStoredSchemaDataSet(schema, connection);

PureQLProjection projection = new([new PureQLPostgreSqlSchemaDataSet(stored)], query);
```

`PureQLPostgreSqlSchemaDataSet` is its origin in every respect but one: each of its tables has a provider that rewrites the tree it is given and hands it to the origin's provider. Its schema, tables, rows and asynchronous rows are the origin's, LINQ composed over a table runs on the origin's provider, and a table held in memory (LINQ to Objects) is the origin's table itself. A query the origin's provider refuses still runs in memory, from the interpreter's own tree, so the result is the same with or without this package; only more of it runs in PostgreSQL. It composes with the interpreter's `PureQLFilteredSchemaDataSet` in either order.

## What runs in PostgreSQL

Only what PostgreSQL computes exactly as the specification's **Semantics** is rewritten; everything else is left as it is, refused by the provider and run in memory.

| PureQL | PostgreSQL |
|---|---|
| Window functions (`PureQLWindows.Over`) | `PostgreSqlWindows.Over`: `OVER (PARTITION BY ... ORDER BY ... frame)`, nulls first ascending, strings by code point, peers by every sort key |
| `rowNumber`, `rank`, `denseRank`, `ntile` | `ROW_NUMBER`, `RANK`, `DENSE_RANK`, `NTILE` |
| `percentRank`, `cumulativeDistribution` | `RANK`, `CUME_DIST` and `COUNT` of the partition, divided in exact integer arithmetic and rounded once to 28 places |
| `previous`, `next` (offset, default) | `LAG`, `LEAD`, the default read in the current row for a missing row only |
| `count`, `sum`, `min`, `max`, `any`, `all` over a window | window aggregates, a predicate as `FILTER` |
| Frames of rows | `ROWS BETWEEN n PRECEDING AND m FOLLOWING`; the whole partition is `UNBOUNDED` on both sides |
| Ranges of numbers, days, seconds, months | `RANGE BETWEEN` an offset or an interval; `"all"` over a key that may be null is a distance past every value, as `UNBOUNDED` would reach the null keys |
| Recursive subqueries (`PureQLRecursion.Recursive`) | `PostgreSqlRecursion.WithRecursive` with the same `maxDepth`: a deeper row fails the statement |
| `length`, `position`, `replace` | `LENGTH`, `STRPOS`, `REPLACE`, in code points |
| `asString` of an `integer` or a `uuid` | `CAST(... AS text)` |
| `modulo`, `integerDivide` | numeric `%`, exact, and an exact division of a multiple |
| `round`, `truncate` with literal or parameter `digits` | `ROUND` to the places (scaled by a power of ten below 0), the operand less its remainder |
| `quarter`, `dayOfWeek`, `microsecond`, `startOfYear` ... `startOfSecond` without a zone | built from `EXTRACT`ed parts with `MAKE_DATE`, `MAKE_TIME`, `MAKE_TIMESTAMP` |

Left in memory on purpose, as PostgreSQL computes them otherwise:

- `average` over a window (`AVG` of numeric keeps about 20 significant digits, not the specification's 28), the statistical aggregates, `first`, `last` and `concatAll` over a window;
- a range PostgreSQL cannot write: days and months on its two sides, `"all"` towards smaller keys over a date or a datetime that may be null, or a distance it cannot subtract from every date (PostgreSQL's timestamps end in 4714 BC);
- a window whose derived columns would share their first 63 characters (PostgreSQL's identifier limit), as many nested windows reach;
- `upper`, `lower`, `foldCase`, `removeDiacritics` (PostgreSQL maps case by its collation), the regular expressions (another dialect), `trim`, `substring`, `position` with a start, `splitPart`, `padStart`, `padEnd`, `repeat`, `reverse`, `concatWith`;
- `asString` of a `decimal`, `boolean`, `date`, `time` or `datetime`, and every reading of text (`as...`, `tryAs...`), whose casts accept spellings the specification rejects;
- powers, roots, logarithms and trigonometry (correctly rounded to 28 digits in the specification);
- `round` and `truncate` with computed `digits`, the parts and periods of a time zone, the ISO week, months between, the month arithmetic of a zone, dates and times from parts.

Over the 386 samples of `PureQL.CSharp.Model.Samples` 0.1.0-preview.4.1.0, stored in PostgreSQL, the provider 0.1.0-preview.0.12.0 refuses 187 on its own and 147 through this dataset.

## Known gaps outside this package

- The translator renders the interpreter's decimal division (and `average` over a group or all rows) as PostgreSQL's numeric division and `AVG`, which keep about 20 significant digits where the specification rounds once to 28. A translated query that divides decimals differs from the in-memory result in its last digits, with or without this package.
- Translated, a stored `datetime` is read without its fraction of a second.

## Dependencies

- `Pure.RelationalSchema.Storage.PureQL.Projection` 0.1.0-preview.7.0.1
- `Linq.Expressions.Translation.PostgreSql` 0.1.0-preview.0.10.0
- `Pure.Collections.Generic`, `Pure.RelationalSchema.HashCodes`

## Target framework

- .NET 11
