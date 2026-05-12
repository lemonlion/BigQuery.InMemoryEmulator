using Google.Cloud.BigQuery.V2;
using Xunit;

namespace InMemoryEmulator.BigQuery.Tests.Integration;

/// <summary>
/// Tests for IGNORE NULLS with NTH_VALUE, LAG, and LEAD navigation functions.
/// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/navigation_functions
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
public class IgnoreNullsNavigationTests : IAsyncLifetime
{
	private readonly BigQuerySession _session;
	private ITestDatasetFixture _fixture = null!;
	public IgnoreNullsNavigationTests(BigQuerySession session) => _session = session;
	public async ValueTask InitializeAsync() => _fixture = TestFixtureFactory.Create(_session);
	public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();

	private async Task<List<BigQueryRow>> Query(string sql)
	{
		var client = await _fixture.GetClientAsync();
		var result = await client.ExecuteQueryAsync(sql, parameters: null);
		return result.ToList();
	}

	// ---- NTH_VALUE IGNORE NULLS ----

	/// <summary>
	/// NTH_VALUE(val, 2 IGNORE NULLS) should return the 2nd non-null value in the frame.
	/// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/navigation_functions#nth_value
	///   "If ignore_nulls is true, excludes NULL values from the calculation."
	/// </summary>
	[Fact]
	public async Task NthValue_IgnoreNulls_ReturnsNthNonNullValue()
	{
		var rows = await Query(@"
			SELECT id, val,
				NTH_VALUE(val, 2 IGNORE NULLS) OVER (ORDER BY id ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS nth
			FROM UNNEST([
				STRUCT(1 AS id, CAST(NULL AS INT64) AS val),
				STRUCT(2, NULL),
				STRUCT(3, 10),
				STRUCT(4, 20),
				STRUCT(5, 30)
			]) ORDER BY id");

		// 2nd non-null value is 20 (after 10)
		Assert.Equal("20", rows[0]["nth"]?.ToString());
		Assert.Equal("20", rows[2]["nth"]?.ToString());
		Assert.Equal("20", rows[4]["nth"]?.ToString());
	}

	/// <summary>
	/// NTH_VALUE(val, 1 IGNORE NULLS) should return the 1st non-null value.
	/// </summary>
	[Fact]
	public async Task NthValue_IgnoreNulls_FirstNonNull()
	{
		var rows = await Query(@"
			SELECT id, val,
				NTH_VALUE(val, 1 IGNORE NULLS) OVER (ORDER BY id ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS nth
			FROM UNNEST([
				STRUCT(1 AS id, CAST(NULL AS INT64) AS val),
				STRUCT(2, NULL),
				STRUCT(3, 10),
				STRUCT(4, 20)
			]) ORDER BY id");

		// 1st non-null value is 10
		Assert.Equal("10", rows[0]["nth"]?.ToString());
		Assert.Equal("10", rows[3]["nth"]?.ToString());
	}

	/// <summary>
	/// NTH_VALUE(val, 3 IGNORE NULLS) when there are fewer than 3 non-null values should return NULL.
	/// </summary>
	[Fact]
	public async Task NthValue_IgnoreNulls_InsufficientNonNulls_ReturnsNull()
	{
		var rows = await Query(@"
			SELECT id,
				NTH_VALUE(val, 3 IGNORE NULLS) OVER (ORDER BY id ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS nth
			FROM UNNEST([
				STRUCT(1 AS id, CAST(NULL AS INT64) AS val),
				STRUCT(2, 10),
				STRUCT(3, 20)
			]) ORDER BY id");

		// Only 2 non-null values, requesting 3rd → NULL
		Assert.Null(rows[0]["nth"]);
	}

	// ---- LAG IGNORE NULLS ----

	/// <summary>
	/// LAG(val IGNORE NULLS) with no offset - should return previous non-null value.
	/// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/navigation_functions#lag
	///   "If ignore_nulls is true, skips NULL values in the lookup."
	/// </summary>
	[Fact]
	public async Task Lag_IgnoreNulls_NoOffset_ReturnsPreviousNonNull()
	{
		var rows = await Query(@"
			SELECT id, val,
				LAG(val IGNORE NULLS) OVER (ORDER BY id) AS lagged
			FROM UNNEST([
				STRUCT(1 AS id, 10 AS val),
				STRUCT(2, CAST(NULL AS INT64)),
				STRUCT(3, CAST(NULL AS INT64)),
				STRUCT(4, 40)
			]) ORDER BY id");

		// id=1: no previous → NULL
		Assert.Null(rows[0]["lagged"]);
		// id=2: previous non-null is 10
		Assert.Equal("10", rows[1]["lagged"]?.ToString());
		// id=3: previous non-null is 10 (skips null at id=2)
		Assert.Equal("10", rows[2]["lagged"]?.ToString());
		// id=4: previous non-null is 10 (skips nulls at id=2,3)
		Assert.Equal("10", rows[3]["lagged"]?.ToString());
	}

	/// <summary>
	/// LAG(val, 1 IGNORE NULLS) - same as above with explicit offset.
	/// </summary>
	[Fact]
	public async Task Lag_IgnoreNulls_ExplicitOffset_ReturnsPreviousNonNull()
	{
		var rows = await Query(@"
			SELECT id, val,
				LAG(val, 1 IGNORE NULLS) OVER (ORDER BY id) AS lagged
			FROM UNNEST([
				STRUCT(1 AS id, 10 AS val),
				STRUCT(2, CAST(NULL AS INT64)),
				STRUCT(3, 30)
			]) ORDER BY id");

		Assert.Null(rows[0]["lagged"]);
		Assert.Equal("10", rows[1]["lagged"]?.ToString());
		// id=3: 1st non-null before it is 10 (skip the NULL at id=2)
		Assert.Equal("10", rows[2]["lagged"]?.ToString());
	}

	/// <summary>
	/// LAG(val, 2 IGNORE NULLS) - skip 2 non-null values backwards.
	/// </summary>
	[Fact]
	public async Task Lag_IgnoreNulls_Offset2_Skips2NonNulls()
	{
		var rows = await Query(@"
			SELECT id, val,
				LAG(val, 2 IGNORE NULLS) OVER (ORDER BY id) AS lagged
			FROM UNNEST([
				STRUCT(1 AS id, 10 AS val),
				STRUCT(2, 20),
				STRUCT(3, CAST(NULL AS INT64)),
				STRUCT(4, 40),
				STRUCT(5, 50)
			]) ORDER BY id");

		// id=5: 2nd non-null before it is 20 (skip nulls, count: 40, then 20)
		Assert.Equal("20", rows[4]["lagged"]?.ToString());
	}

	/// <summary>
	/// LAG with IGNORE NULLS and a default value.
	/// </summary>
	[Fact]
	public async Task Lag_IgnoreNulls_WithDefault()
	{
		var rows = await Query(@"
			SELECT id, val,
				LAG(val, 1, -1 IGNORE NULLS) OVER (ORDER BY id) AS lagged
			FROM UNNEST([
				STRUCT(1 AS id, CAST(NULL AS INT64) AS val),
				STRUCT(2, 20)
			]) ORDER BY id");

		// id=1: no previous non-null → default -1
		Assert.Equal("-1", rows[0]["lagged"]?.ToString());
		// id=2: previous non-null not found (null at id=1) → default -1
		Assert.Equal("-1", rows[1]["lagged"]?.ToString());
	}

	// ---- LEAD IGNORE NULLS ----

	/// <summary>
	/// LEAD(val IGNORE NULLS) - should return next non-null value.
	/// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/navigation_functions#lead
	/// </summary>
	[Fact]
	public async Task Lead_IgnoreNulls_NoOffset_ReturnsNextNonNull()
	{
		var rows = await Query(@"
			SELECT id, val,
				LEAD(val IGNORE NULLS) OVER (ORDER BY id) AS led
			FROM UNNEST([
				STRUCT(1 AS id, 10 AS val),
				STRUCT(2, CAST(NULL AS INT64)),
				STRUCT(3, CAST(NULL AS INT64)),
				STRUCT(4, 40)
			]) ORDER BY id");

		// id=1: next non-null is 40 (skip nulls at 2,3)
		Assert.Equal("40", rows[0]["led"]?.ToString());
		// id=2: next non-null is 40
		Assert.Equal("40", rows[1]["led"]?.ToString());
		// id=3: next non-null is 40
		Assert.Equal("40", rows[2]["led"]?.ToString());
		// id=4: no next → NULL
		Assert.Null(rows[3]["led"]);
	}

	/// <summary>
	/// LEAD(val, 1 IGNORE NULLS) - explicit offset, skip nulls.
	/// </summary>
	[Fact]
	public async Task Lead_IgnoreNulls_ExplicitOffset()
	{
		var rows = await Query(@"
			SELECT id, val,
				LEAD(val, 1 IGNORE NULLS) OVER (ORDER BY id) AS led
			FROM UNNEST([
				STRUCT(1 AS id, 10 AS val),
				STRUCT(2, CAST(NULL AS INT64)),
				STRUCT(3, 30)
			]) ORDER BY id");

		// id=1: 1st non-null after me = 30 (skip null at id=2)
		Assert.Equal("30", rows[0]["led"]?.ToString());
		Assert.Equal("30", rows[1]["led"]?.ToString());
		Assert.Null(rows[2]["led"]);
	}

	/// <summary>
	/// LEAD with IGNORE NULLS and a default value.
	/// </summary>
	[Fact]
	public async Task Lead_IgnoreNulls_WithDefault()
	{
		var rows = await Query(@"
			SELECT id, val,
				LEAD(val, 1, -1 IGNORE NULLS) OVER (ORDER BY id) AS led
			FROM UNNEST([
				STRUCT(1 AS id, 10 AS val),
				STRUCT(2, CAST(NULL AS INT64))
			]) ORDER BY id");

		// id=1: next non-null not found → default -1
		Assert.Equal("-1", rows[0]["led"]?.ToString());
		// id=2: no next → default -1
		Assert.Equal("-1", rows[1]["led"]?.ToString());
	}
}
