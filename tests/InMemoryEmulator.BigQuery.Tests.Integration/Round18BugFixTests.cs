using Google.Cloud.BigQuery.V2;
using Xunit;

namespace InMemoryEmulator.BigQuery.Tests.Integration;

/// <summary>
/// Integration tests for bugs fixed in research round 18:
/// - Bug 1: GREATEST/LEAST with NaN should return NaN if any input is NaN
/// - Bug 2: UNNEST implicit alias (without AS keyword) not parsed correctly
/// </summary>
[Collection(IntegrationCollection.Name)]
public class Round18BugFixTests : IAsyncLifetime
{
	private readonly BigQuerySession _session;
	private ITestDatasetFixture _fixture = null!;
	private string _datasetId = null!;

	public Round18BugFixTests(BigQuerySession session) => _session = session;

	public async ValueTask InitializeAsync()
	{
		_fixture = TestFixtureFactory.Create(_session);
		_datasetId = $"test_r18_{Guid.NewGuid():N}"[..30];
		await _fixture.CreateDatasetAsync(_datasetId);
	}

	public async ValueTask DisposeAsync()
	{
		try { var c = await _fixture.GetClientAsync(); await c.DeleteDatasetAsync(_datasetId, new DeleteDatasetOptions { DeleteContents = true }); } catch { }
		await _fixture.DisposeAsync();
	}

	private async Task<string?> Scalar(string sql)
	{
		var client = await _fixture.GetClientAsync();
		var result = await client.ExecuteQueryAsync(sql, parameters: null);
		var rows = result.ToList();
		return rows.Count > 0 ? rows[0][0]?.ToString() : null;
	}

	private async Task<List<string?>> Column(string sql)
	{
		var client = await _fixture.GetClientAsync();
		var result = await client.ExecuteQueryAsync(sql, parameters: null);
		return result.Select(r => r[0]?.ToString()).ToList();
	}

	// ================================================================
	// Bug 1: GREATEST with NaN should return NaN
	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/mathematical_functions#greatest
	//   "Otherwise, in the case of floating-point arguments, if any argument is NaN, returns NaN."
	// ================================================================

	[Fact]
	public async Task Greatest_WithNaN_ReturnsNaN()
	{
		var result = await Scalar("SELECT GREATEST(1.0, IEEE_DIVIDE(0,0))");
		Assert.Equal("NaN", result);
	}

	[Fact]
	public async Task Greatest_WithNaN_Multiple_ReturnsNaN()
	{
		var result = await Scalar("SELECT GREATEST(1.0, 2.0, IEEE_DIVIDE(0,0), 3.0)");
		Assert.Equal("NaN", result);
	}

	[Fact]
	public async Task Greatest_WithNull_ReturnsNull()
	{
		var result = await Scalar("SELECT GREATEST(1.0, NULL, 3.0)");
		Assert.Null(result);
	}

	[Fact]
	public async Task Greatest_Normal_ReturnsMax()
	{
		var result = await Scalar("SELECT GREATEST(1.0, 5.0, 3.0)");
		Assert.Equal("5", result);
	}

	// ================================================================
	// Bug 1b: LEAST with NaN should return NaN
	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/mathematical_functions#least
	//   "Otherwise, in the case of floating-point arguments, if any argument is NaN, returns NaN."
	// ================================================================

	[Fact]
	public async Task Least_WithNaN_ReturnsNaN()
	{
		var result = await Scalar("SELECT LEAST(1.0, IEEE_DIVIDE(0,0))");
		Assert.Equal("NaN", result);
	}

	[Fact]
	public async Task Least_WithNaN_Multiple_ReturnsNaN()
	{
		var result = await Scalar("SELECT LEAST(1.0, 2.0, IEEE_DIVIDE(0,0), 3.0)");
		Assert.Equal("NaN", result);
	}

	[Fact]
	public async Task Least_WithNull_ReturnsNull()
	{
		var result = await Scalar("SELECT LEAST(1.0, NULL, 3.0)");
		Assert.Null(result);
	}

	[Fact]
	public async Task Least_Normal_ReturnsMin()
	{
		var result = await Scalar("SELECT LEAST(1.0, 5.0, 3.0)");
		Assert.Equal("1", result);
	}

	// ================================================================
	// Bug 2: UNNEST implicit alias (without AS keyword)
	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/query-syntax#unnest_operator
	//   "UNNEST(array_expression) [alias]" — alias can be with or without AS keyword
	// ================================================================

	[Fact]
	public async Task Unnest_ImplicitAlias_Values()
	{
		var results = await Column("SELECT x FROM UNNEST([1, 2, 3]) x ORDER BY x");
		Assert.Equal(3, results.Count);
		Assert.Equal("1", results[0]);
		Assert.Equal("2", results[1]);
		Assert.Equal("3", results[2]);
	}

	[Fact]
	public async Task Unnest_ImplicitAlias_FloatValues()
	{
		var results = await Column("SELECT x FROM UNNEST([1.0, 2.5, 3.0]) x ORDER BY x");
		Assert.Equal(3, results.Count);
		Assert.Equal("1", results[0]);
		Assert.Equal("2.5", results[1]);
		Assert.Equal("3", results[2]);
	}

	[Fact]
	public async Task Unnest_ExplicitAlias_SameAsImplicit()
	{
		// Both should produce same results
		var implicit_ = await Column("SELECT x FROM UNNEST(['a', 'b', 'c']) x ORDER BY x");
		var explicit_ = await Column("SELECT x FROM UNNEST(['a', 'b', 'c']) AS x ORDER BY x");
		Assert.Equal(explicit_, implicit_);
	}

	[Fact]
	public async Task Unnest_ImplicitAlias_WithFilter()
	{
		var results = await Column("SELECT x FROM UNNEST([10, 20, 30, 40]) x WHERE x > 15 ORDER BY x");
		Assert.Equal(3, results.Count);
		Assert.Equal("20", results[0]);
		Assert.Equal("30", results[1]);
		Assert.Equal("40", results[2]);
	}

	// ================================================================
	// Verification: NaN ordering is correct (NaN sorts before -inf ascending)
	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/data-types#floating_point_semantics
	//   "NaN is ordered before -inf in ascending sort order"
	// ================================================================

	[Fact]
	public async Task OrderBy_NaN_SortsBeforeNegativeInfinity()
	{
		var client = await _fixture.GetClientAsync();
		var result = await client.ExecuteQueryAsync(
			"SELECT x FROM UNNEST([1.0, IEEE_DIVIDE(0,0), IEEE_DIVIDE(-1,0), IEEE_DIVIDE(1,0), 0.0]) AS x ORDER BY x", parameters: null);
		var values = result.Select(r => (double)r[0]).ToList();
		Assert.True(double.IsNaN(values[0]), $"Expected NaN at [0], got {values[0]}");
		Assert.True(double.IsNegativeInfinity(values[1]), $"Expected -inf at [1], got {values[1]}");
		Assert.Equal(0.0, values[2]);
		Assert.Equal(1.0, values[3]);
		Assert.True(double.IsPositiveInfinity(values[4]), $"Expected +inf at [4], got {values[4]}");
	}

	// ================================================================
	// Verification: DISTINCT and GROUP BY with NaN treats NaN as equal
	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/data-types#floating_point_semantics
	//   "NaN is treated as equal to another NaN for DISTINCT, GROUP BY"
	// ================================================================

	[Fact]
	public async Task Distinct_NaN_TreatedAsEqual()
	{
		var results = await Column(
			"SELECT DISTINCT x FROM UNNEST([IEEE_DIVIDE(0,0), IEEE_DIVIDE(0,0), 1.0]) AS x ORDER BY x");
		Assert.Equal(2, results.Count);
	}

	[Fact]
	public async Task GroupBy_NaN_GroupedTogether()
	{
		var results = await Column(
			"SELECT x FROM UNNEST([IEEE_DIVIDE(0,0), IEEE_DIVIDE(0,0), 1.0, 1.0]) AS x GROUP BY x ORDER BY x");
		Assert.Equal(2, results.Count);
	}
}
