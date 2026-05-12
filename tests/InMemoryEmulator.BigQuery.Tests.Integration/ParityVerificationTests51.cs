using Google.Cloud.BigQuery.V2;
using Xunit;

namespace InMemoryEmulator.BigQuery.Tests.Integration;

/// <summary>
/// Parity verification tests batch 51: NULL argument handling for LEFT/RIGHT, ROUND/TRUNC,
/// TIME constructor, and NET functions.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ParityVerificationTests51 : IAsyncLifetime
{
	private readonly BigQuerySession _session;
	private ITestDatasetFixture _fixture = null!;

	public ParityVerificationTests51(BigQuerySession session) => _session = session;

	public async ValueTask InitializeAsync()
	{
		_fixture = TestFixtureFactory.Create(_session);
	}

	public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();

	private async Task<BigQueryRow> ScalarRowAsync(string sql)
	{
		var client = await _fixture.GetClientAsync();
		var result = await client.ExecuteQueryAsync(sql, parameters: null);
		return result.First();
	}

	// --- LEFT/RIGHT NULL length ---

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/string_functions#left
	//   Returns NULL if any argument is NULL.

	[Fact]
	public async Task Left_NullLength_ReturnsNull()
	{
		var row = await ScalarRowAsync("SELECT LEFT('hello', CAST(NULL AS INT64))");
		Assert.Null(row[0]);
	}

	[Fact]
	public async Task Right_NullLength_ReturnsNull()
	{
		var row = await ScalarRowAsync("SELECT RIGHT('hello', CAST(NULL AS INT64))");
		Assert.Null(row[0]);
	}

	// --- ROUND/TRUNC NULL digits ---

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/mathematical_functions#round
	//   Returns NULL if any argument is NULL.

	[Fact]
	public async Task Round_NullDigits_ReturnsNull()
	{
		var row = await ScalarRowAsync("SELECT ROUND(3.7, CAST(NULL AS INT64))");
		Assert.Null(row[0]);
	}

	[Fact]
	public async Task Trunc_NullDigits_ReturnsNull()
	{
		var row = await ScalarRowAsync("SELECT TRUNC(3.7, CAST(NULL AS INT64))");
		Assert.Null(row[0]);
	}

	// --- TIME() constructor NULL args ---

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/time_functions#time
	//   Returns NULL if any argument is NULL.

	[Fact]
	public async Task TimeConstructor_NullHour_ReturnsNull()
	{
		var row = await ScalarRowAsync("SELECT TIME(CAST(NULL AS INT64), 30, 0)");
		Assert.Null(row[0]);
	}

	[Fact]
	public async Task TimeConstructor_NullMinute_ReturnsNull()
	{
		var row = await ScalarRowAsync("SELECT TIME(12, CAST(NULL AS INT64), 0)");
		Assert.Null(row[0]);
	}

	[Fact]
	public async Task TimeConstructor_NullSecond_ReturnsNull()
	{
		var row = await ScalarRowAsync("SELECT TIME(12, 30, CAST(NULL AS INT64))");
		Assert.Null(row[0]);
	}
}
