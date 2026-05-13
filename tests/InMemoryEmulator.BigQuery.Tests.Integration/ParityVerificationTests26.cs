using Google.Cloud.BigQuery.V2;
using Xunit;

namespace InMemoryEmulator.BigQuery.Tests.Integration;

/// <summary>
/// Parity verification tests batch 26: Complex analytical query patterns combining
/// multiple features. Multi-JOIN analytics, window functions over partitioned data,
/// complex aggregation with HAVING, nested CTEs with window functions,
/// PIVOT-style patterns, type casting edge cases.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ParityVerificationTests26 : IAsyncLifetime
{
	private readonly BigQuerySession _session;
	private ITestDatasetFixture _fixture = null!;
	private string _ds = null!;

	public ParityVerificationTests26(BigQuerySession session) => _session = session;

	public async ValueTask InitializeAsync()
	{
		_fixture = TestFixtureFactory.Create(_session);
		_ds = $"pv26_{Guid.NewGuid():N}"[..28];
		await _fixture.CreateDatasetAsync(_ds);
	}

	public async ValueTask DisposeAsync()
	{
		try
		{
			var c = await _fixture.GetClientAsync();
			await c.DeleteDatasetAsync(_ds, new DeleteDatasetOptions { DeleteContents = true });
		}
		catch { }
		await _fixture.DisposeAsync();
	}

	private async Task<string?> S(string sql)
	{
		var client = await _fixture.GetClientAsync();
		var result = await client.ExecuteQueryAsync(sql.Replace("{ds}", _ds), parameters: null);
		var rows = result.ToList();
		return rows.Count == 0 ? null : rows[0][0]?.ToString();
	}

	private async Task<List<BigQueryRow>> Q(string sql)
	{
		var client = await _fixture.GetClientAsync();
		var result = await client.ExecuteQueryAsync(sql.Replace("{ds}", _ds), parameters: null);
		return result.ToList();
	}

	private async Task Exec(string sql)
	{
		var client = await _fixture.GetClientAsync();
		await client.ExecuteQueryAsync(sql.Replace("{ds}", _ds), parameters: null);
	}

	// ───────────────────────────────────────────────────────────────────────────
	// Complex CTE + Window + GROUP BY combined patterns
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task CTE_WindowInGroupBy()
	{
		// Compute running totals then aggregate them
		var rows = await Q(@"
			WITH running AS (
				SELECT x, SUM(x) OVER (ORDER BY x) AS cum_sum
				FROM UNNEST([1, 2, 3, 4, 5]) AS x
			)
			SELECT 
				CASE WHEN cum_sum <= 6 THEN 'low' ELSE 'high' END AS bucket,
				COUNT(*) AS cnt
			FROM running
			GROUP BY bucket
			ORDER BY bucket");
		// cum_sum: 1,3,6,10,15. <= 6: [1,3,6] → 3 low; [10,15] → 2 high
		Assert.Equal(2, rows.Count);
		Assert.Equal("high", rows[0][0]?.ToString());
		Assert.Equal("2", rows[0][1]?.ToString());
		Assert.Equal("low", rows[1][0]?.ToString());
		Assert.Equal("3", rows[1][1]?.ToString());
	}

	[Fact] public async Task CTE_MultipleWindowFunctions()
	{
		var rows = await Q(@"
			WITH data AS (
				SELECT 'A' AS grp, 10 AS val UNION ALL
				SELECT 'A', 20 UNION ALL
				SELECT 'A', 30 UNION ALL
				SELECT 'B', 5 UNION ALL
				SELECT 'B', 15
			)
			SELECT grp, val,
				ROW_NUMBER() OVER (PARTITION BY grp ORDER BY val) AS rn,
				SUM(val) OVER (PARTITION BY grp ORDER BY val) AS running_sum
			FROM data
			ORDER BY grp, val");
		Assert.Equal(5, rows.Count);
		Assert.Equal("A", rows[0][0]?.ToString());
		Assert.Equal("10", rows[0][1]?.ToString());
		Assert.Equal("1", rows[0][2]?.ToString());
		Assert.Equal("10", rows[0][3]?.ToString());
		Assert.Equal("A", rows[2][0]?.ToString());
		Assert.Equal("30", rows[2][1]?.ToString());
		Assert.Equal("3", rows[2][2]?.ToString());
		Assert.Equal("60", rows[2][3]?.ToString());
	}

	// ───────────────────────────────────────────────────────────────────────────
	// PIVOT-style pattern using CASE aggregation
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task PivotStyle_CaseAggregation()
	{
		var rows = await Q(@"
			WITH data AS (
				SELECT 'Q1' AS quarter, 'East' AS region, 100 AS sales UNION ALL
				SELECT 'Q1', 'West', 150 UNION ALL
				SELECT 'Q2', 'East', 200 UNION ALL
				SELECT 'Q2', 'West', 250
			)
			SELECT region,
				SUM(CASE WHEN quarter = 'Q1' THEN sales END) AS q1_sales,
				SUM(CASE WHEN quarter = 'Q2' THEN sales END) AS q2_sales
			FROM data
			GROUP BY region
			ORDER BY region");
		Assert.Equal(2, rows.Count);
		Assert.Equal("East", rows[0][0]?.ToString());
		Assert.Equal("100", rows[0][1]?.ToString());
		Assert.Equal("200", rows[0][2]?.ToString());
		Assert.Equal("West", rows[1][0]?.ToString());
		Assert.Equal("150", rows[1][1]?.ToString());
		Assert.Equal("250", rows[1][2]?.ToString());
	}

	// ───────────────────────────────────────────────────────────────────────────
	// Complex JOIN with aggregate and window
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Join_AggregateWithWindow()
	{
		await Exec("CREATE TABLE `{ds}.txn` (id INT64, acct_id INT64, amount FLOAT64)");
		await Exec("CREATE TABLE `{ds}.accounts` (id INT64, name STRING)");
		await Exec("INSERT INTO `{ds}.accounts` (id, name) VALUES (1, 'Checking'), (2, 'Savings')");
		await Exec("INSERT INTO `{ds}.txn` (id, acct_id, amount) VALUES (1, 1, 100), (2, 1, -50), (3, 1, 200), (4, 2, 500), (5, 2, -100)");

		var rows = await Q(@"
			SELECT a.name, SUM(t.amount) AS balance,
				RANK() OVER (ORDER BY SUM(t.amount) DESC) AS balance_rank
			FROM `{ds}.txn` t JOIN `{ds}.accounts` a ON t.acct_id = a.id
			GROUP BY a.name
			ORDER BY balance_rank");
		Assert.Equal(2, rows.Count);
		Assert.Equal("Savings", rows[0][0]?.ToString());
		Assert.Equal("400", rows[0][1]?.ToString());
		Assert.Equal("1", rows[0][2]?.ToString());
		Assert.Equal("Checking", rows[1][0]?.ToString());
		Assert.Equal("250", rows[1][1]?.ToString());
		Assert.Equal("2", rows[1][2]?.ToString());
	}

	// ───────────────────────────────────────────────────────────────────────────
	// Complex UNION ALL + Aggregate pattern
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task UnionAll_ThenAggregate()
	{
		var rows = await Q(@"
			WITH combined AS (
				SELECT 'source_a' AS src, x AS val FROM UNNEST([10, 20, 30]) AS x
				UNION ALL
				SELECT 'source_b', y FROM UNNEST([5, 15, 25]) AS y
			)
			SELECT src, SUM(val) AS total, COUNT(*) AS cnt
			FROM combined
			GROUP BY src
			ORDER BY src");
		Assert.Equal(2, rows.Count);
		Assert.Equal("source_a", rows[0][0]?.ToString());
		Assert.Equal("60", rows[0][1]?.ToString());
		Assert.Equal("3", rows[0][2]?.ToString());
		Assert.Equal("source_b", rows[1][0]?.ToString());
		Assert.Equal("45", rows[1][1]?.ToString());
		Assert.Equal("3", rows[1][2]?.ToString());
	}

	// ───────────────────────────────────────────────────────────────────────────
	// Complex expression in ORDER BY
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task OrderBy_Expression()
	{
		var rows = await Q(@"
			SELECT x FROM UNNEST([-3, 1, -1, 4, -2]) AS x ORDER BY ABS(x)");
		Assert.Equal("1", rows[0][0]?.ToString());
		Assert.Equal("-1", rows[1][0]?.ToString());
		Assert.Equal("-2", rows[2][0]?.ToString());
		Assert.Equal("-3", rows[3][0]?.ToString());
		Assert.Equal("4", rows[4][0]?.ToString());
	}

	// ───────────────────────────────────────────────────────────────────────────
	// NULL handling in GROUP BY
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task GroupBy_NullGroup()
	{
		var rows = await Q(@"
			SELECT grp, COUNT(*) AS cnt
			FROM (
				SELECT 'A' AS grp UNION ALL
				SELECT 'A' UNION ALL
				SELECT CAST(NULL AS STRING) UNION ALL
				SELECT CAST(NULL AS STRING) UNION ALL
				SELECT 'B'
			)
			GROUP BY grp
			ORDER BY grp NULLS LAST");
		Assert.Equal(3, rows.Count);
		Assert.Equal("A", rows[0][0]?.ToString());
		Assert.Equal("2", rows[0][1]?.ToString());
		Assert.Equal("B", rows[1][0]?.ToString());
		Assert.Equal("1", rows[1][1]?.ToString());
		Assert.Null(rows[2][0]); // NULL group
		Assert.Equal("2", rows[2][1]?.ToString());
	}

	// ───────────────────────────────────────────────────────────────────────────
	// String aggregate with ORDER BY
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task StringAgg_WithOrderBy()
	{
		var result = await S(@"
			SELECT STRING_AGG(name, ' -> ' ORDER BY id)
			FROM (
				SELECT 1 AS id, 'Start' AS name UNION ALL
				SELECT 2, 'Middle' UNION ALL
				SELECT 3, 'End'
			)");
		Assert.Equal("Start -> Middle -> End", result);
	}

	// ───────────────────────────────────────────────────────────────────────────
	// Nested function calls
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task NestedFunctions_Deep()
	{
		var result = await S("SELECT UPPER(TRIM(CONCAT('  hello', ' world  ')))");
		Assert.Equal("HELLO WORLD", result);
	}

	[Fact] public async Task NestedFunctions_MathChain()
	{
		var result = await S("SELECT CAST(ROUND(SQRT(POW(3, 2) + POW(4, 2)), 0) AS INT64)");
		Assert.Equal("5", result); // sqrt(9+16) = sqrt(25) = 5
	}

	// ───────────────────────────────────────────────────────────────────────────
	// ARRAY_AGG combined with STRUCT
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task ArrayAgg_IgnoreNulls()
	{
		var result = await S(@"
			SELECT ARRAY_LENGTH(ARRAY_AGG(x IGNORE NULLS))
			FROM UNNEST([1, CAST(NULL AS INT64), 2, CAST(NULL AS INT64), 3]) AS x");
		Assert.Equal("3", result); // [1, 2, 3] - NULLs ignored
	}

	[Fact] public async Task ArrayAgg_IncludesNullsByDefault()
	{
		var result = await S(@"
			SELECT ARRAY_LENGTH(ARRAY_AGG(x))
			FROM UNNEST([1, CAST(NULL AS INT64), 2, CAST(NULL AS INT64), 3]) AS x");
		Assert.Equal("5", result); // [1, NULL, 2, NULL, 3] - NULLs included
	}

	// ───────────────────────────────────────────────────────────────────────────
	// Complex WHERE with multiple conditions
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Where_ComplexConditions()
	{
		var rows = await Q(@"
			SELECT x FROM UNNEST([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]) AS x
			WHERE (x > 3 AND x < 8) OR x = 1 OR x = 10
			ORDER BY x");
		// x in {1, 4, 5, 6, 7, 10}
		Assert.Equal(6, rows.Count);
		Assert.Equal("1", rows[0][0]?.ToString());
		Assert.Equal("4", rows[1][0]?.ToString());
		Assert.Equal("10", rows[5][0]?.ToString());
	}

	// ───────────────────────────────────────────────────────────────────────────
	// CAST between date types with timestamps
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Cast_StringToTimestamp()
	{
		var result = await S("SELECT CAST(CAST('2024-06-15 14:30:00 UTC' AS TIMESTAMP) AS STRING)");
		Assert.Equal("2024-06-15 14:30:00+00", result);
	}

	[Fact] public async Task Cast_DateToTimestamp()
	{
		var result = await S("SELECT CAST(CAST(DATE '2024-06-15' AS TIMESTAMP) AS STRING)");
		Assert.Equal("2024-06-15 00:00:00+00", result);
	}
}
