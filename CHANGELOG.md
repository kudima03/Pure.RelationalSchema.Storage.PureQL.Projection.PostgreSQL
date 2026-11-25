# Changelog

All notable changes to `Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0-preview.0.1.0] - 2026-11-25

### Added

- `PureQLPostgreSqlSchemaDataSet`, a schema dataset over an `IStoredSchemaDataSet` whose tables rewrite the trees of a `PureQLProjection` (`Pure.RelationalSchema.Storage.PureQL.Projection` 0.1.0-preview.7.0.1) for the origin's provider, where PostgreSQL computes exactly what the PureQL specification defines: window functions (`rowNumber`, `rank`, `denseRank`, `percentRank`, `cumulativeDistribution`, `ntile`, `previous`, `next`, and `count`, `sum`, `min`, `max`, `any`, `all` over a window) over frames of rows and ranges of numbers, days, seconds and months as `PostgreSqlWindows.Over`, recursive subqueries as `PostgreSqlRecursion.WithRecursive` with the same `maxDepth`, and `length`, `position`, `replace`, `asString` of an `integer` or a `uuid`, `modulo`, `integerDivide`, `round` and `truncate` with literal or parameter digits, `quarter`, `dayOfWeek`, `microsecond` and `startOfYear` ... `startOfSecond` without a zone as the BCL shapes of `Linq.Expressions.Translation.PostgreSql` 0.1.0-preview.0.10.0. Everything else is left as it is, refused by the provider and run in memory. Through `Pure.RelationalSchema.Storage.PostgreSQL.Querying` 0.1.0-preview.0.12.0, 147 of the 386 samples of `PureQL.CSharp.Model.Samples` 0.1.0-preview.4.1.0 are refused instead of 187.
