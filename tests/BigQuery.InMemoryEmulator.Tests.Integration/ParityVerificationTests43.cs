using Google.Cloud.BigQuery.V2;
using Xunit;

namespace BigQuery.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Parity verification tests batch 43: SAFE. prefix support.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ParityVerificationTests43 : IAsyncLifetime
{
	private readonly BigQuerySession _session;
	private ITestDatasetFixture _fixture = null!;

	public ParityVerificationTests43(BigQuerySession session) => _session = session;

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
	// SAFE. prefix — returns NULL instead of error
	// ============================================================

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/functions-reference#safe_prefix
	//   "If the function is prefixed with SAFE, it returns NULL instead of an error."

	[Fact]
	public async Task Safe_Substr_NegativeLength_ReturnsNull()
	{
		// Without SAFE, this throws an error. With SAFE, returns NULL.
		var result = await ScalarAsync("SELECT SAFE.SUBSTR('hello', 1, -1)");
		Assert.Equal("NULL", result);
	}

	[Fact]
	public async Task Safe_Log_Zero_ReturnsNull()
	{
		// LOG(0) produces an error; SAFE.LOG(0) returns NULL
		var result = await ScalarAsync("SELECT SAFE.LOG(0)");
		Assert.Equal("NULL", result);
	}

	[Fact]
	public async Task Safe_Repeat_NegativeCount_ReturnsNull()
	{
		// REPEAT(..., -1) produces an error; SAFE.REPEAT returns NULL
		var result = await ScalarAsync("SELECT SAFE.REPEAT('abc', -1)");
		Assert.Equal("NULL", result);
	}

	[Fact]
	public async Task Safe_Div_Zero_ReturnsNull()
	{
		// DIV(1, 0) produces an error; SAFE.DIV returns NULL
		var result = await ScalarAsync("SELECT SAFE.DIV(1, 0)");
		Assert.Equal("NULL", result);
	}

	[Fact]
	public async Task Safe_ValidFunction_ReturnsNormalResult()
	{
		// SAFE prefix on a non-erroring call should return the normal result
		var result = await ScalarAsync("SELECT SAFE.SUBSTR('hello', 2, 3)");
		Assert.Equal("ell", result);
	}

	[Fact]
	public async Task Safe_ParseDate_InvalidInput_ReturnsNull()
	{
		var result = await ScalarAsync("SELECT SAFE.PARSE_DATE('%Y-%m-%d', 'not-a-date')");
		Assert.Equal("NULL", result);
	}
}
