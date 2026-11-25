# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

All `dotnet` commands must be run from the `./src` directory.

```bash
dotnet restore
dotnet build --no-restore -warnaserror
dotnet format --verify-no-changes             # check code style (CI enforces this)
dotnet tool restore && dotnet csharpier check .   # CI enforces this too
dotnet test --no-build --verbosity normal      # unit and integration tests (Docker for Testcontainers)
dotnet pack --configuration Release -p:Version=<version> --output .
```

CI additionally passes `-p:AssemblyVersion` (pinned to the major) and `-p:FileVersion`; see `.github/workflows/publish-nuget.yml`. The mutation step is commented out in `build-and-test.yml` (Stryker over the Docker tests is slow); run `dotnet stryker` locally.

## Architecture

The adapter between the PureQL interpreter (`Pure.RelationalSchema.Storage.PureQL.Projection`) and PostgreSQL: the only package where PureQL meets PostgreSQL. The provider (`Pure.RelationalSchema.Storage.PostgreSQL.Querying`) and the translator (`Linq.Expressions.Translation.PostgreSql`) know nothing of PureQL; the interpreter knows nothing of PostgreSQL.

- **`PureQLPostgreSqlSchemaDataSet`** — the one public type: a `sealed record` implementing `IStoredSchemaDataSet` over an origin, whose tables are a lazy hash-keyed dictionary (`Pure.Collections.Generic`, `TableHash`) of `RewritingTable.Of(origin's table)`: the origin's table itself when its provider is `EnumerableQuery`, otherwise a `RewritingTable`.
- **`RewritingTable`** — an `IStoredTableDataSet` whose `Expression` is `Constant(this, IQueryable<IRow>)` and whose `Provider` is a `RewritingProvider` over the origin's provider; enumeration (also asynchronous) is the origin's. The interpreter's `Execution` picks the provider of the first stored-table constant that is not `EnumerableQuery`, so a projection's tree reaches the `RewritingProvider`; its in-memory fallback (`LocalProvider`) enumerates the tables, i.e. the origins.
- **`RewritingProvider`** — every `CreateQuery` / `Execute` hands `Rewriter.Rewritten(tree)` to the origin's provider; a `NotSupportedException` from it makes the projection run its own (unrewritten) tree in memory.
- **`Rewriter`** — bottom-up `ExpressionVisitor`: a `RewritingTable` constant becomes `origin.Expression` (so a `FilteredTable` origin keeps its `Where`); a call is offered to `Windows`, `Recursion`, then `Functions`; null means "leave it as it is".
- **`Windows`** — `PureQLWindows.Over(rows, partitionBy, orderBy, peers, frame, window => (window.Row, value))` → `PostgreSqlWindows.Over(rows, row => (object)partition, orderBy, frame constant, (row, window) => (row, value))`. The empty partition tuple is `null`, `partition => partition` (no order) is `null`. Declined (kept) when `ColumnNames` finds two derived-column paths sharing 63 characters, the frame has no PostgreSQL form, or `WindowFunctions` keeps the selector.
- **`WindowFunctions`** — rewrites the selector: `Row`, `Rows`, `RowNumber`, `Rank`, `DenseRank`, `Ntile` (int buckets, clamped to `int.MaxValue`), `Has(shift) ? f(At(shift)) : default(Row)` → `Lag` / `Lead`; `PercentRank` / `CumulativeDistribution` only over the whole partition, as an exact ratio (`Ratio`: floor((2·p·10^28 + q)/(2q)) via numeric `%`, `TRUNC`, × 10^-28). Keeps `Enumerable.Average` and any other read of the window.
- **`Frames`** — `PureQLFrame` → `PostgreSqlFrame` (see its header comment for every rule: whole partition, rows, ranges, "all" over nullable keys, the 4714 BC limit of PostgreSQL's timestamps, which side subtracts when descending).
- **`Recursion`** — `PureQLRecursion.Recursive` (IQueryable form) → `PostgreSqlRecursion.WithRecursive(seed, next, maxDepth)` with `maxDepth` computed (`Closed`).
- **`Functions`** — a table from interpreter method (type, name, parameter types) to a rule in `Text`, `Numbers` or `Temporal`, each returning the BCL shape or null. The header comment lists what is rewritten and what is deliberately left, and why.
- **`Closed`** — computes the closed arguments the markers take as values (frames, offsets, buckets, depth, digits), which the interpreter writes as constants or bound parameter values.

**Exactness rule:** rewrite only where PostgreSQL then computes exactly the specification's result (its README, *Semantics*). A rewritten tree only ever runs in PostgreSQL, so a shape is chosen for the SQL the translator writes for it, not for its .NET meaning. Every deliberate non-rewrite has a comment and a test that it still runs in memory with the right result.

**Not AOT-compatible** (`IsAotCompatible = false`), like the interpreter. **Target framework:** net11.0 only. **Package validation:** `EnablePackageValidation = true`, with `PackageValidationBaselineVersion` currently `0.1.0-preview.0.1.0`, the latest release. Breaking API changes against the baseline fail the build.

**Tests** (`src/Tests/...Tests`):
- `Rewriting/` (no Docker): `FramesTests` (every frame form → SQL text or kept), `WindowTests`, `RecursionTests`, `FunctionTests` (tree in → `Data/TreeText` out, and kept trees unchanged), `ClosedTests`, `SampleTreeTests` (every sample's rewritten tree over `Data/RefusingTable`s: no `RewritingTable` left, one marker or PureQL operator per window and recursion, and the in-memory result unchanged).
- `DataSet/` (no Docker): the dataset is its origin (`Data/RefusingTable`, `Data/PassingTable`).
- `Samples/TranslatedSampleTests` and `Semantics/` (Docker, `Data/PostgreSqlDatabase`: the `Pure.RelationalSchema.Storage.Samples` fixtures and `Data/Readings` stored in PostgreSQL by raw SQL, as the interpreter's tests do). Every sample through the dataset gives its expected result and exactly (`Data/ResultText`, cell text) the interpreter's in-memory result; `Refused` lists the samples still run in memory (it fails when one starts being translated: remove it), `DividedInPostgreSql` the translated samples whose decimals differ in the last digits because the translator writes decimal division as numeric `/`, `ReadingNoStoredTable` those with no stored table. `Semantics/*TranslationTests` run JSON queries (`Data/Json`, `PureQL.CSharp.Model.Serialization`) over `t.readings` and compare translated with in-memory results (`Data/Translation`).

## Code Style

Enforced by `.editorconfig`, `dotnet format --verify-no-changes` and csharpier in CI:

- No `var` — always use explicit types
- No expression-bodied methods, constructors, or operators; expression-bodied properties and accessors are required
- Private fields: `_camelCase` (underscore prefix)
- File-scoped namespaces, `using` directives outside the namespace
- Allman brace style
- Max line length: 90 characters
- Pattern matching preferred over `is`-with-cast and `as`-with-null-check
- A chained constructor with an empty body is written as a body that assigns, since csharpier and `dotnet format` disagree on `) { }`; `Data/TreeText.cs` is in `.csharpierignore` for the same reason

## Commit Messages

Do not mention Claude or AI assistance in commit messages.
