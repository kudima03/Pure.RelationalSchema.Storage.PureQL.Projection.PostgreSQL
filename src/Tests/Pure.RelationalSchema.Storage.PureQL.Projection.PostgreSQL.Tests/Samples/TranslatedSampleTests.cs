using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Samples;

// Every query of PureQL.CSharp.Model.Samples over its fixtures stored in
// PostgreSQL, read through the rewriting dataset: it returns the sample's
// result, and exactly the result the interpreter computes in memory over the
// same rows, translated to one SQL statement or, for the samples PostgreSQL
// still cannot run exactly (Refused), refused with NotSupportedException and
// run in memory. Needs Docker.
public sealed class TranslatedSampleTests(PostgreSqlDatabase database)
    : IClassFixture<PostgreSqlDatabase>
{
    private readonly PostgreSqlDatabase _database = database;

    // The samples that read no stored table (only inline rows), and so run
    // in memory: there is no provider to give them to.
    internal static readonly HashSet<string> ReadingNoStoredTable =
    [
        "BisectionOfARateQuery",
        "FoldCaseCornersQuery",
        "FoldCaseInATotalQuery",
        "GreatCircleDistanceQuery",
        "GroupedNextOverTotalAndCombinedSeedsQuery",
        "GroupingByNormalizedKeysQuery",
        "GroupingWithoutCaseQuery",
        "InlineRowsOfEveryCellTypeQuery",
        "QuadrantsOfTwoInlineListsQuery",
        "RateOfReturnByIterationQuery",
        "RoundAndTruncateKeepTheOperandTypeQuery",
        "RoundedIntegersAcrossGroupedClausesQuery",
        "RoundedIntegersFromSubqueriesQuery",
        "WalkThatStopsAtACycleQuery",
    ];

    // The samples Pure.RelationalSchema.Storage.PostgreSQL.Querying refuses
    // once rewritten, so that they run in memory: they hold a function this
    // package leaves as it is (see Functions and Windows) or a shape the
    // translator does not know yet. A sample that leaves this list when the
    // provider or this package learns its constructs fails the test, and is
    // removed from it.
    internal static readonly HashSet<string> Refused =
    [
        "AbsentAndNullableOperandsQuery",
        "AddingAndTakingTheDifferenceQuery",
        "AggregatesOverExactValuesQuery",
        "AsStringOfEveryTypeQuery",
        "AsStringOfOperatorResultsQuery",
        "BlankValuesOfEveryTypeAsNullQuery",
        "CircularMeanHourPerUserQuery",
        "CodesMatchedByRegexQuery",
        "ComputedKeysSelectorsAndDefaultsInWindowsQuery",
        "ConcatAllInATotalQuery",
        "ConcatAllNamesPerCountryQuery",
        "ConcatAllRepeatedOnEveryRowQuery",
        "ConstantOperandsNeverNullableQuery",
        "ContextsOfRecursiveStepsQuery",
        "ConversionsInATotalQuery",
        "ConversionsQuery",
        "CountDistinctOverWindowsQuery",
        "CountDistinctSelectorKindsQuery",
        "CovarianceAndCorrelationPerTenantQuery",
        "DailyRevenueTrendQuery",
        "DatePartsQuery",
        "DatetimeZoneKindsQuery",
        "DurationsInPartsQuery",
        "EveryAggregateOverARangeQuery",
        "EveryAggregateOverWindowsQuery",
        "EveryOperatorSignatureInAGroupedSelectQuery",
        "EveryOperatorSignatureInAPlainSelectQuery",
        "EveryOperatorSignatureInATotalQuery",
        "FixedWidthReportLinesQuery",
        "FoldCaseQuery",
        "FrameOfEveryFormQuery",
        "FromPartsInAGroupedQuery",
        "FromPartsInATotalQuery",
        "GreatestAndLeastOfEveryTypeQuery",
        "GroupingWithoutAccentsQuery",
        "GrowthPerUserQuery",
        "HierarchyWalkWithAggregatedStepsQuery",
        "HourAsAnAngleQuery",
        "JoinOnNormalizedSkuQuery",
        "LastTwelveMonthsPerUserQuery",
        "LiftedOverEveryOperandQuery",
        "LocalBusinessDayQuery",
        "LocalDatesGroupedAndTotalledQuery",
        "LocalDatesInEveryClauseQuery",
        "LocalDayBucketsQuery",
        "LocalInstantRoundTripsQuery",
        "LocalInstantsInEveryClauseQuery",
        "LocalInstantsInGroupedAndTotalQueriesQuery",
        "LocalMonthIdentitiesQuery",
        "LocalMonthKindsQuery",
        "LocalMonthsInEveryClauseQuery",
        "LocalMonthsInGroupedAndTotalQueriesQuery",
        "LocalMonthsQuery",
        "LocalPartKindsQuery",
        "LocalPartsInConditionsQuery",
        "LocalPartsInEveryClauseQuery",
        "LocalPartsInGroupedAndTotalQueriesQuery",
        "LocalPeriodStartKindsQuery",
        "LocalPeriodStartsInEveryClauseQuery",
        "LocalPeriodStartsInGroupedAndTotalQueriesQuery",
        "LocalTimesAsInstantsQuery",
        "LocalWeekdayAndHourQuery",
        "MatchingWithoutCaseOverJoinsQuery",
        "MonthArithmeticOfLocalDatesQuery",
        "MonthsBetweenQuery",
        "MonthsInALocalCalendarQuery",
        "MovingAveragesQuery",
        "NormalizeCornersQuery",
        "NormalizeInATotalQuery",
        "NormalizeMatchingOverJoinsQuery",
        "NormalizeQuery",
        "NullableOperandsByRoleQuery",
        "NullableRoleOperandsInAGroupedQuery",
        "NullableRoleOperandsInATotalQuery",
        "OperatorsWithAndWithoutAZoneQuery",
        "OrderLabelsOverJoinsQuery",
        "OrdersPerDayOfADatetimeQuery",
        "PaddingAndJoiningCornersQuery",
        "PaddingAndJoiningInATotalQuery",
        "PaddingAndJoiningQuery",
        "PairStatisticsInAGroupedQuery",
        "PairStatisticsInATotalQuery",
        "PairStatisticsOperandKindsQuery",
        "PairStatisticsOverWindowsQuery",
        "PartsAndBackQuery",
        "PartsLiftedOverEveryPartQuery",
        "PathCountsPerDepthQuery",
        "PowersAndLogarithmsCornersQuery",
        "PowersAndLogarithmsInEveryClauseQuery",
        "PowersAndLogarithmsQuery",
        "PriceStatisticsPerProductQuery",
        "RangeDistancesOverDatesQuery",
        "RangeDistancesOverNumbersQuery",
        "RangeFramesQuery",
        "ReadingTextInATotalQuery",
        "ReadingTextOfEveryTypeQuery",
        "ReadingTextQuery",
        "RegressionInAGroupedQuery",
        "RegressionInATotalQuery",
        "RegressionOperandKindsQuery",
        "RegressionOverWindowsQuery",
        "RegularExpressionDialectQuery",
        "RegularExpressionsInATotalQuery",
        "RegularExpressionsQuery",
        "RemoveDiacriticsCornersQuery",
        "RemoveDiacriticsInATotalQuery",
        "RemoveDiacriticsMatchingOverJoinsQuery",
        "RemoveDiacriticsQuery",
        "ReplacePositionSplitPartCornersQuery",
        "ReplacePositionSplitPartInATotalQuery",
        "ReplacePositionSplitPartQuery",
        "RoundHalfToEvenInATotalQuery",
        "RoundedIntegersAsIntegerOperandsQuery",
        "RoundingDecimalResultsQuery",
        "RowExpressionsInWindowsQuery",
        "RunningTotalOfDailySumsQuery",
        "RunningTotalsQuery",
        "ScoresAgainstTheDistributionQuery",
        "SortingBySelectWindowColumnsQuery",
        "StandardDeviationByHandQuery",
        "StatisticsForEveryTypeQuery",
        "StatisticsFromAGroupedSubqueryQuery",
        "StatisticsPerStatusQuery",
        "StreaksAndFirstValuesQuery",
        "StringMatchingQuery",
        "SubstringQuery",
        "TemporalAveragesPerUserQuery",
        "TemporalMeansInATotalQuery",
        "TemporalMeansOfComputedValuesQuery",
        "TemporalMeansOverWindowsQuery",
        "TemporalMeansWithPredicatesQuery",
        "TextKeysJoinedToTypedKeysQuery",
        "TreeInReadingOrderQuery",
        "TrigonometricIdentitiesQuery",
        "TrigonometryOverAggregatesInATotalQuery",
        "TrimQuery",
        "UnparsedPostalCodesPerCountryQuery",
        "UpperLowerQuery",
        "UsersPerEmailDomainByRegexQuery",
        "UsersPerEmailDomainQuery",
        "WindowFunctionsByKindQuery",
        "WindowsInEveryPositionQuery",
        "WindowsKeyedByRowExpressionsQuery",
        "WindowsOverNullableFieldsQuery",
        "ZoneExpressionKindsQuery",
        "ZoneFreeOperatorsInEveryClauseQuery",
        "ZoneNameFormsQuery",
    ];

    // The translated samples whose decimals differ from the interpreter's in
    // their last digits: the translator renders the interpreter's decimal
    // division (and average over a group or all rows) as PostgreSQL's numeric
    // division and AVG, which keep about 20 significant digits where the
    // specification rounds once to 28. That is the translator's rendering of
    // a BCL shape the interpreter writes itself, not a rewrite of this
    // package, and it is the same without it; these samples compare with the
    // expected result to 12 significant digits only, as the interpreter's own
    // tests do.
    internal static readonly HashSet<string> DividedInPostgreSql =
    [
        "AggregatesOverAllRowsQuery",
        "CountDistinctInAGroupedQuery",
        "DefaultOverInGroupsAndTotalsQuery",
        "GreatestAndLeastOverAggregatesQuery",
        "GroupsWithATotalsRowQuery",
        "LatestOrderNextToEachOrderQuery",
        "LongOperandListsQuery",
        "NestedAndFlatOperatorsQuery",
        "NullIfEqualInEveryClauseOfAGroupedQuery",
        "NullIfEqualQuery",
        "OperatorsOverAListInEveryContextQuery",
        "OperatorsOverAListQuery",
        "PartitionTotalsQuery",
        "RatiosGuardedAgainstZeroDivisorsPerProductQuery",
        "ShareOfTotalQuery",
        "TotalQueryJoinedOnTrueQuery",
    ];

    public static TheoryData<string> SamplesWithResult =>
        [
            .. Sample
                .All.Where(sample => sample.Result is not null)
                .Select(sample => sample.Name),
        ];

    public static TheoryData<string> SamplesWithParameters =>
        [
            .. Sample
                .All.Where(sample => sample.Result is null)
                .Select(sample => sample.Name),
        ];

    [Theory]
    [MemberData(nameof(SamplesWithResult))]
    public void SampleReturnsItsResultExactlyAsInMemory(string name)
    {
        Sample sample = Sample.Named(name);
        PureQLProjection actual = new(_database.Rewriting, sample.Query);
        ExpectedTable expected = new(sample.Result!);

        if (sample.IsOrdered)
        {
            expected.AssertOrdered(actual);
        }
        else
        {
            expected.AssertUnordered(actual);
        }

        AssertExact(
            name,
            actual,
            new PureQLProjection(PostgreSqlDatabase.InMemory, sample.Query)
        );
    }

    [Theory]
    [MemberData(nameof(SamplesWithParameters))]
    public void SampleWithBoundParametersReturnsTheResultInMemory(string name)
    {
        Sample sample = Sample.Named(name);

        AssertExact(
            name,
            new PureQLProjection(
                _database.Rewriting,
                sample.Query,
                SampleParameters.Values
            ),
            new PureQLProjection(
                PostgreSqlDatabase.InMemory,
                sample.Query,
                SampleParameters.Values
            )
        );
    }

    [Fact]
    public void ListsNameSamplesOfTheCatalogue()
    {
        HashSet<string> names = [.. Sample.All.Select(sample => sample.Name)];

        Assert.Equal(386, names.Count);
        Assert.Subset(names, Refused);
        Assert.Subset(names, DividedInPostgreSql);
        Assert.Subset(names, ReadingNoStoredTable);
        Assert.Empty(Refused.Intersect(DividedInPostgreSql));
    }

    private static void AssertExact(
        string name,
        PureQLProjection actual,
        PureQLProjection memory
    )
    {
        bool ordered = Sample.Named(name).IsOrdered;
        IReadOnlyList<string> text = ResultText.Of(actual, ordered);

        if (!DividedInPostgreSql.Contains(name))
        {
            Assert.Equal(ResultText.Of(memory, ordered), text);
        }

        if (ReadingNoStoredTable.Contains(name))
        {
            Assert.Null(actual.FallbackReason);
            Assert.Equal(QueryExecution.InMemory, actual.Execution);
        }
        else if (Refused.Contains(name))
        {
            _ = Assert.IsType<NotSupportedException>(actual.FallbackReason);
            Assert.Equal(QueryExecution.InMemory, actual.Execution);
        }
        else
        {
            Assert.Null(actual.FallbackReason);
            Assert.Equal(QueryExecution.Translated, actual.Execution);
        }
    }
}
