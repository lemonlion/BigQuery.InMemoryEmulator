using Google.Cloud.BigQuery.V2;
using Xunit;

namespace InMemoryEmulator.BigQuery.Tests.Integration;

/// <summary>
/// Parity verification tests batch 27: Edge cases in date/time functions,
/// numeric precision, string functions with Unicode, advanced window frame specs,
/// ARRAY operations, STRUCT construction, and error handling via SAFE_ prefix.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ParityVerificationTests27 : IAsyncLifetime
{
	private readonly BigQuerySession _session;
	private ITestDatasetFixture _fixture = null!;
	private string _ds = null!;

	public ParityVerificationTests27(BigQuerySession session) => _session = session;

	public async ValueTask InitializeAsync()
	{
		_fixture = TestFixtureFactory.Create(_session);
		_ds = $"pv27_{Guid.NewGuid():N}"[..28];
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

	// ───────────────────────────────────────────────────────────────────────────
	// DATE_DIFF edge cases
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task DateDiff_SameDay()
	{
		var result = await S("SELECT DATE_DIFF(DATE '2024-01-15', DATE '2024-01-15', DAY)");
		Assert.Equal("0", result);
	}

	[Fact] public async Task DateDiff_NegativeResult()
	{
		var result = await S("SELECT DATE_DIFF(DATE '2024-01-01', DATE '2024-01-10', DAY)");
		Assert.Equal("-9", result);
	}

	[Fact] public async Task DateDiff_YearBoundary()
	{
		var result = await S("SELECT DATE_DIFF(DATE '2024-01-01', DATE '2023-12-31', DAY)");
		Assert.Equal("1", result);
	}

	// ───────────────────────────────────────────────────────────────────────────
	// TIMESTAMP_DIFF edge cases
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task TimestampDiff_Hours()
	{
		var result = await S("SELECT TIMESTAMP_DIFF(TIMESTAMP '2024-01-02 00:00:00 UTC', TIMESTAMP '2024-01-01 00:00:00 UTC', HOUR)");
		Assert.Equal("24", result);
	}

	[Fact] public async Task TimestampDiff_Seconds()
	{
		var result = await S("SELECT TIMESTAMP_DIFF(TIMESTAMP '2024-01-01 00:01:30 UTC', TIMESTAMP '2024-01-01 00:00:00 UTC', SECOND)");
		Assert.Equal("90", result);
	}

	// ───────────────────────────────────────────────────────────────────────────
	// GENERATE_DATE_ARRAY
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task GenerateDateArray_Days()
	{
		var result = await S(@"
			SELECT ARRAY_LENGTH(GENERATE_DATE_ARRAY(DATE '2024-01-01', DATE '2024-01-05', INTERVAL 1 DAY))");
		Assert.Equal("5", result); // 01, 02, 03, 04, 05
	}

	[Fact] public async Task GenerateDateArray_Months()
	{
		var result = await S(@"
			SELECT ARRAY_LENGTH(GENERATE_DATE_ARRAY(DATE '2024-01-01', DATE '2024-06-01', INTERVAL 1 MONTH))");
		Assert.Equal("6", result); // Jan, Feb, Mar, Apr, May, Jun
	}

	// ───────────────────────────────────────────────────────────────────────────
	// SAFE_ prefix function calls
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Safe_DivideByZero()
	{
		var result = await S("SELECT SAFE_DIVIDE(10, 0)");
		Assert.Null(result);
	}

	[Fact] public async Task Safe_CastInvalidString()
	{
		var result = await S("SELECT SAFE_CAST('not_a_number' AS INT64)");
		Assert.Null(result);
	}

	[Fact] public async Task Safe_CastInvalidDate()
	{
		var result = await S("SELECT SAFE_CAST('not_a_date' AS DATE)");
		Assert.Null(result);
	}

	// ───────────────────────────────────────────────────────────────────────────
	// IFNULL / COALESCE with multiple NULLs
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Coalesce_AllNulls()
	{
		var result = await S("SELECT COALESCE(CAST(NULL AS STRING), CAST(NULL AS STRING), CAST(NULL AS STRING))");
		Assert.Null(result);
	}

	[Fact] public async Task Coalesce_MiddleValue()
	{
		var result = await S("SELECT COALESCE(CAST(NULL AS INT64), 42, 99)");
		Assert.Equal("42", result);
	}

	// ───────────────────────────────────────────────────────────────────────────
	// BETWEEN with various types
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Between_Int()
	{
		var rows = await Q("SELECT x FROM UNNEST([1,2,3,4,5]) AS x WHERE x BETWEEN 2 AND 4 ORDER BY x");
		Assert.Equal(3, rows.Count);
		Assert.Equal("2", rows[0][0]?.ToString());
		Assert.Equal("4", rows[2][0]?.ToString());
	}

	[Fact] public async Task Between_String()
	{
		var rows = await Q(@"SELECT x FROM UNNEST(['apple','banana','cherry','date','elderberry']) AS x 
			WHERE x BETWEEN 'banana' AND 'date' ORDER BY x");
		Assert.Equal(3, rows.Count);
		Assert.Equal("banana", rows[0][0]?.ToString());
		Assert.Equal("cherry", rows[1][0]?.ToString());
		Assert.Equal("date", rows[2][0]?.ToString());
	}

	[Fact] public async Task Between_Date()
	{
		var rows = await Q(@"SELECT d FROM UNNEST([DATE '2024-01-01', DATE '2024-06-15', DATE '2024-12-31']) AS d
			WHERE d BETWEEN DATE '2024-03-01' AND DATE '2024-09-30'
			ORDER BY d");
		Assert.Single(rows);
	}

	// ───────────────────────────────────────────────────────────────────────────
	// Complex CASE with NULL comparisons
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Case_NullInWhen()
	{
		// CASE WHEN NULL will never match (NULL is not truthy)
		var result = await S("SELECT CASE WHEN NULL THEN 'match' ELSE 'no_match' END");
		Assert.Equal("no_match", result);
	}

	[Fact] public async Task Case_SimpleWithNull()
	{
		// Simple CASE: NULL = NULL is false 
		var result = await S("SELECT CASE CAST(NULL AS STRING) WHEN NULL THEN 'match' ELSE 'no_match' END");
		Assert.Equal("no_match", result);
	}

	// ───────────────────────────────────────────────────────────────────────────
	// LAG/LEAD with offsets and defaults
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Lag_Offset2()
	{
		var rows = await Q(@"
			SELECT x, LAG(x, 2) OVER (ORDER BY x) AS lag2
			FROM UNNEST([10, 20, 30, 40, 50]) AS x
			ORDER BY x");
		Assert.Null(rows[0][1]); // lag(10, 2) = NULL
		Assert.Null(rows[1][1]); // lag(20, 2) = NULL
		Assert.Equal("10", rows[2][1]?.ToString()); // lag(30, 2) = 10
		Assert.Equal("20", rows[3][1]?.ToString()); // lag(40, 2) = 20
		Assert.Equal("30", rows[4][1]?.ToString()); // lag(50, 2) = 30
	}

	[Fact] public async Task Lead_WithDefault()
	{
		var rows = await Q(@"
			SELECT x, LEAD(x, 1, -1) OVER (ORDER BY x) AS lead1
			FROM UNNEST([10, 20, 30]) AS x
			ORDER BY x");
		Assert.Equal("20", rows[0][1]?.ToString()); // lead(10) = 20
		Assert.Equal("30", rows[1][1]?.ToString()); // lead(20) = 30
		Assert.Equal("-1", rows[2][1]?.ToString()); // lead(30) = default(-1)
	}

	// ───────────────────────────────────────────────────────────────────────────
	// NTH_VALUE window function
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task NthValue_Basic()
	{
		var rows = await Q(@"
			SELECT x, NTH_VALUE(x, 2) OVER (ORDER BY x ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS nth2
			FROM UNNEST([10, 20, 30]) AS x
			ORDER BY x");
		Assert.Equal("20", rows[0][1]?.ToString());
		Assert.Equal("20", rows[1][1]?.ToString());
		Assert.Equal("20", rows[2][1]?.ToString());
	}

	// ───────────────────────────────────────────────────────────────────────────
	// CONCAT_WS (concat with separator) - BigQuery doesn't have this,
	// but we can replicate with ARRAY_TO_STRING + ARRAY
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task ArrayToString_AsJoin()
	{
		var result = await S("SELECT ARRAY_TO_STRING(['hello', 'world', 'test'], '-')");
		Assert.Equal("hello-world-test", result);
	}

	[Fact] public async Task ArrayToString_WithNulls()
	{
		var result = await S(@"SELECT ARRAY_TO_STRING(['a', CAST(NULL AS STRING), 'b'], ',', 'X')");
		Assert.Equal("a,X,b", result); // null_text replaces NULLs
	}

	[Fact] public async Task ArrayToString_SkipNullsNoNullText()
	{
		var result = await S(@"SELECT ARRAY_TO_STRING(['a', CAST(NULL AS STRING), 'b'], ',')");
		Assert.Equal("a,b", result); // NULLs skipped when no null_text
	}

	// ───────────────────────────────────────────────────────────────────────────
	// GENERATE_ARRAY with float step
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task GenerateArray_FloatStep()
	{
		var result = await S("SELECT ARRAY_LENGTH(GENERATE_ARRAY(0.0, 1.0, 0.5))");
		Assert.Equal("3", result); // [0.0, 0.5, 1.0]
	}

	// ───────────────────────────────────────────────────────────────────────────
	// Multiple aggregates in HAVING
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Having_MultipleAggregates()
	{
		var rows = await Q(@"
			WITH data AS (
				SELECT 'A' AS grp, 10 AS val UNION ALL
				SELECT 'A', 20 UNION ALL
				SELECT 'A', 30 UNION ALL
				SELECT 'B', 5 UNION ALL
				SELECT 'B', 15 UNION ALL
				SELECT 'C', 100
			)
			SELECT grp, SUM(val) AS total, COUNT(*) AS cnt
			FROM data
			GROUP BY grp
			HAVING COUNT(*) > 1 AND SUM(val) > 15
			ORDER BY grp");
		Assert.Equal(2, rows.Count);
		Assert.Equal("A", rows[0][0]?.ToString());
		Assert.Equal("60", rows[0][1]?.ToString());
		Assert.Equal("B", rows[1][0]?.ToString());
		Assert.Equal("20", rows[1][1]?.ToString());
	}

	// ───────────────────────────────────────────────────────────────────────────
	// String functions: LEFT, RIGHT, LPAD, RPAD
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Left_Basic()
	{
		var result = await S("SELECT LEFT('HelloWorld', 5)");
		Assert.Equal("Hello", result);
	}

	[Fact] public async Task Right_Basic()
	{
		var result = await S("SELECT RIGHT('HelloWorld', 5)");
		Assert.Equal("World", result);
	}

	[Fact] public async Task Lpad_Basic()
	{
		var result = await S("SELECT LPAD('42', 5, '0')");
		Assert.Equal("00042", result);
	}

	[Fact] public async Task Rpad_Basic()
	{
		var result = await S("SELECT RPAD('Hi', 5, '!')");
		Assert.Equal("Hi!!!", result);
	}
}
