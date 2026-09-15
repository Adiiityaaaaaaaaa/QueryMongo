using MongoDB.Bson;
using QueryMongo.Core.Models;
using QueryMongo.Core.Services;
using Xunit;

namespace QueryMongo.Core.Tests;

public class SchemaChartTests
{
    private static List<BsonValue> Strings(params string[] values) =>
        values.Select(v => (BsonValue)new BsonString(v)).ToList();

    private static List<BsonValue> Numbers(params double[] values) =>
        values.Select(v => (BsonValue)new BsonDouble(v)).ToList();

    [Fact]
    public void NoValuesYieldsNoChart()
    {
        var distribution = SchemaCharts.Build("x", []);

        Assert.Equal(DistributionKind.None, distribution.Kind);
        Assert.False(distribution.HasBars);
    }

    [Fact]
    public void StringsBecomeCategorical()
    {
        var distribution = SchemaCharts.Build("genre", Strings("a", "b", "a", "a", "c"));

        Assert.Equal(DistributionKind.Categorical, distribution.Kind);
        Assert.Equal(3, distribution.Buckets.Count);

        // Most frequent first, so the chart reads left to right by size.
        Assert.Equal("a", distribution.Buckets[0].Label);
        Assert.Equal(3, distribution.Buckets[0].Count);
    }

    [Fact]
    public void CategoricalFractionsSumToOne()
    {
        var distribution = SchemaCharts.Build("x", Strings("a", "b", "c", "a"));

        Assert.Equal(1.0, distribution.Buckets.Sum(b => b.Fraction), precision: 5);
    }

    [Fact]
    public void ManyCategoriesAreTruncatedWithAnOtherBucket()
    {
        var many = Strings([.. Enumerable.Range(0, 30).Select(i => $"v{i}")]);

        var distribution = SchemaCharts.Build("x", many);

        // 12 bars plus one "(n more)" bucket.
        Assert.Equal(13, distribution.Buckets.Count);
        Assert.Contains("more", distribution.Buckets[^1].Label, StringComparison.Ordinal);
    }

    [Fact]
    public void NumbersBecomeAHistogram()
    {
        var distribution = SchemaCharts.Build("n", Numbers(1, 2, 3, 4, 5, 6, 7, 8, 9, 10));

        Assert.Equal(DistributionKind.Numeric, distribution.Kind);
        Assert.Equal(12, distribution.Buckets.Count);
        Assert.Equal(10, distribution.Buckets.Sum(b => b.Count));
    }

    [Fact]
    public void NumericSummaryReportsTheRange()
    {
        var distribution = SchemaCharts.Build("n", Numbers(10, 20, 30));

        Assert.Contains("min 10", distribution.Summary, StringComparison.Ordinal);
        Assert.Contains("max 30", distribution.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void MaximumValueLandsInTheLastBucket()
    {
        // Without clamping, the maximum would fall one bucket past the end.
        var distribution = SchemaCharts.Build("n", Numbers(0, 100));

        Assert.Equal(2, distribution.Buckets.Sum(b => b.Count));
    }

    [Fact]
    public void ConstantNumericFieldIsReportedNotCharted()
    {
        var distribution = SchemaCharts.Build("n", Numbers(7, 7, 7));

        Assert.Single(distribution.Buckets);
        Assert.Contains("every sampled value", distribution.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void BooleansAreTreatedAsCategories()
    {
        var values = new List<BsonValue> { BsonBoolean.True, BsonBoolean.False, BsonBoolean.True };

        var distribution = SchemaCharts.Build("flag", values);

        Assert.Equal(DistributionKind.Boolean, distribution.Kind);
        Assert.Equal(2, distribution.Buckets.Count);
    }

    [Fact]
    public void DatesBecomeATemporalHistogram()
    {
        var values = Enumerable.Range(0, 10)
            .Select(i => (BsonValue)new BsonDateTime(new DateTime(2020, 1, 1).AddDays(i * 30)))
            .ToList();

        var distribution = SchemaCharts.Build("when", values);

        Assert.Equal(DistributionKind.Temporal, distribution.Kind);
        Assert.Equal(10, distribution.Buckets.Sum(b => b.Count));
    }

    [Fact]
    public void NullsAreExcluded()
    {
        var values = new List<BsonValue> { new BsonString("a"), BsonNull.Value, new BsonString("b") };

        var distribution = SchemaCharts.Build("x", values);

        Assert.Equal(2, distribution.Buckets.Sum(b => b.Count));
    }
}

public class ExplainPlanTests
{
    private static BsonDocument Explain(string json) => BsonDocument.Parse(json);

    [Fact]
    public void ParsesANestedPlan()
    {
        var plan = ExplainPlan.Parse(Explain("""
            {
              "queryPlanner": { "namespace": "db.movies" },
              "executionStats": {
                "nReturned": 10, "totalDocsExamined": 10, "totalKeysExamined": 10,
                "executionTimeMillis": 3,
                "executionStages": {
                  "stage": "LIMIT", "nReturned": 10,
                  "inputStage": {
                    "stage": "FETCH", "nReturned": 10, "docsExamined": 10,
                    "inputStage": {
                      "stage": "IXSCAN", "nReturned": 10, "keysExamined": 10,
                      "indexName": "year_rating", "keyPattern": { "year": 1, "rating": -1 }
                    }
                  }
                }
              }
            }
            """));

        Assert.Equal("LIMIT", plan.Root!.Stage);
        Assert.Equal(["LIMIT", "FETCH", "IXSCAN"], plan.AllNodes.Select(n => n.Stage));
        Assert.Equal("db.movies", plan.Namespace);
        Assert.Equal(10, plan.TotalReturned);
        Assert.Equal(TimeSpan.FromMilliseconds(3), plan.ExecutionTime);
    }

    [Fact]
    public void IndexKeysAreDescribedWithDirection()
    {
        var plan = ExplainPlan.Parse(Explain("""
            {
              "executionStats": {
                "executionStages": {
                  "stage": "IXSCAN", "indexName": "i",
                  "keyPattern": { "a": 1, "b": -1 }
                }
              }
            }
            """));

        Assert.Equal("a ↑, b ↓", plan.Root!.KeyDescription);
    }

    [Fact]
    public void CollectionScanIsFlaggedAndAdvised()
    {
        var plan = ExplainPlan.Parse(Explain("""
            { "executionStats": { "executionStages": { "stage": "COLLSCAN", "docsExamined": 5000 } } }
            """));

        Assert.True(plan.Root!.IsScan);
        Assert.True(plan.Root.IsProblem);
        Assert.NotNull(plan.Root.Advice);
    }

    [Fact]
    public void InMemorySortIsFlagged()
    {
        var plan = ExplainPlan.Parse(Explain("""
            { "executionStats": { "executionStages": { "stage": "SORT" } } }
            """));

        Assert.True(plan.Root!.IsBlockingSort);
        Assert.NotNull(plan.Root.Advice);
    }

    [Fact]
    public void BranchingPlansKeepEveryChild()
    {
        var plan = ExplainPlan.Parse(Explain("""
            {
              "executionStats": {
                "executionStages": {
                  "stage": "OR",
                  "inputStages": [ { "stage": "IXSCAN" }, { "stage": "IXSCAN" } ]
                }
              }
            }
            """));

        Assert.Equal(2, plan.Root!.Children.Count);
        Assert.Equal(3, plan.AllNodes.Count());
    }

    [Fact]
    public void FallsBackToQueryPlannerWhenThereAreNoExecutionStats()
    {
        var plan = ExplainPlan.Parse(Explain("""
            { "queryPlanner": { "namespace": "db.c", "winningPlan": { "stage": "COLLSCAN" } } }
            """));

        Assert.Equal("COLLSCAN", plan.Root!.Stage);
    }

    [Fact]
    public void EmptyExplainYieldsNoRoot()
    {
        Assert.Null(ExplainPlan.Parse([]).Root);
    }

    [Fact]
    public void InefficiencyIsTheExaminedToReturnedRatio()
    {
        var plan = ExplainPlan.Parse(Explain("""
            {
              "executionStats": {
                "nReturned": 1, "totalDocsExamined": 5000,
                "executionStages": { "stage": "COLLSCAN" }
              }
            }
            """));

        Assert.True(plan.IsInefficient);
        Assert.Equal(5000, plan.ExaminedPerReturned);
    }

    [Fact]
    public void AWellIndexedQueryIsNotFlagged()
    {
        var plan = ExplainPlan.Parse(Explain("""
            {
              "executionStats": {
                "nReturned": 10, "totalDocsExamined": 10,
                "executionStages": { "stage": "IXSCAN" }
              }
            }
            """));

        Assert.False(plan.IsInefficient);
    }
}
