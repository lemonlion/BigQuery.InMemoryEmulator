using Google.Cloud.BigQuery.V2;
using Xunit;

namespace InMemoryEmulator.BigQuery.Tests.Integration;

/// <summary>
/// Parity verification tests batch 44: STARTS_WITH/ENDS_WITH NULL,
/// STRING_AGG LIMIT, DATE_TRUNC WEEK(WEEKDAY).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ParityVerificationTests44 : IAsyncLifetime
{
	private readonly BigQuerySession _session;
	private ITestDatasetFixture _fixture = null!;

	public ParityVerificationTests44(BigQuerySession session) => _session = session;

	public async ValueTask InitializeAsync()
	{
		_fixture = TestFixtureFactory.Create(_session);
	}

	public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();

	private async Task<string> ScalarAsync(string sql)
	{
		var client = await _fixture.GetClientAsync();
		var result = await client.ExecuteQueryAsync(sql, parameters: null);
		var row = result.First();
		return row[0]?.ToString() ?? "NULL";
	}

	// ============================================================
	// STARTS_WITH / ENDS_WITH NULL handling
	// ============================================================

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/string_functions#starts_with
	//   "Returns TRUE if prefix is an empty STRING."
	//   NULL propagation: returns NULL if any arg is NULL.
	[Fact]
	public async Task StartsWith_NullPrefix_ReturnsNull()
	{
		var result = await ScalarAsync("SELECT STARTS_WITH('hello', NULL)");
		Assert.Equal("NULL", result);
	}

	[Fact]
	public async Task StartsWith_NullValue_ReturnsNull()
	{
		var result = await ScalarAsync("SELECT STARTS_WITH(NULL, 'h')");
		Assert.Equal("NULL", result);
	}

	[Fact]
	public async Task StartsWith_EmptyPrefix_ReturnsTrue()
	{
		var result = await ScalarAsync("SELECT STARTS_WITH('hello', '')");
		Assert.Equal("True", result);
	}

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/string_functions#ends_with
	[Fact]
	public async Task EndsWith_NullSuffix_ReturnsNull()
	{
		var result = await ScalarAsync("SELECT ENDS_WITH('hello', NULL)");
		Assert.Equal("NULL", result);
	}

	[Fact]
	public async Task EndsWith_NullValue_ReturnsNull()
	{
		var result = await ScalarAsync("SELECT ENDS_WITH(NULL, 'o')");
		Assert.Equal("NULL", result);
	}

	// ============================================================
	// STRING_AGG with LIMIT
	// ============================================================

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/aggregate_functions#string_agg
	//   "LIMIT n: specifies the maximum number of value inputs in the result."
	[Fact]
	public async Task StringAgg_WithLimit()
	{
		var result = await ScalarAsync(
			"SELECT STRING_AGG(x, ',' ORDER BY x LIMIT 2) FROM UNNEST(['c','a','b','d','e']) AS x");
		Assert.Equal("a,b", result);
	}

	[Fact]
	public async Task StringAgg_WithoutLimit_ReturnsAll()
	{
		var result = await ScalarAsync(
			"SELECT STRING_AGG(x, ',' ORDER BY x) FROM UNNEST(['c','a','b']) AS x");
		Assert.Equal("a,b,c", result);
	}

	[Fact]
	public async Task StringAgg_LimitExceedsCount_ReturnsAll()
	{
		var result = await ScalarAsync(
			"SELECT STRING_AGG(x, ',' ORDER BY x LIMIT 100) FROM UNNEST(['b','a']) AS x");
		Assert.Equal("a,b", result);
	}

	// ============================================================
	// DATE_TRUNC with WEEK(MONDAY) — parameterized week start
	// ============================================================

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/date_functions#date_trunc
	//   "WEEK(WEEKDAY): Truncates date_expression to the preceding day that has the specified WEEKDAY name."
	[Fact]
	public async Task DateTrunc_WeekMonday()
	{
		// 2024-03-13 is Wednesday. Preceding Monday is 2024-03-11.
		var result = await ScalarAsync("SELECT CAST(DATE_TRUNC(DATE '2024-03-13', WEEK(MONDAY)) AS STRING)");
		Assert.Equal("2024-03-11", result);
	}

	[Fact]
	public async Task DateTrunc_WeekFriday()
	{
		// 2024-03-13 is Wednesday. Preceding Friday is 2024-03-08.
		var result = await ScalarAsync("SELECT CAST(DATE_TRUNC(DATE '2024-03-13', WEEK(FRIDAY)) AS STRING)");
		Assert.Equal("2024-03-08", result);
	}

	[Fact]
	public async Task DateTrunc_WeekSunday()
	{
		// Default WEEK behaves same as WEEK(SUNDAY)
		// 2024-03-13 is Wednesday. Preceding Sunday is 2024-03-10.
		var result = await ScalarAsync("SELECT CAST(DATE_TRUNC(DATE '2024-03-13', WEEK(SUNDAY)) AS STRING)");
		Assert.Equal("2024-03-10", result);
	}
}
