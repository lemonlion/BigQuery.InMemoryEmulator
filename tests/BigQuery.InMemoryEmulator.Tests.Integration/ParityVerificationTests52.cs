using Google.Cloud.BigQuery.V2;
using Xunit;

namespace BigQuery.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Parity verification tests batch 52: NULL argument handling for DATE/DATETIME constructors,
/// INSTR, and REGEXP_EXTRACT position/occurrence arguments.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ParityVerificationTests52 : IAsyncLifetime
{
	private readonly BigQuerySession _session;
	private ITestDatasetFixture _fixture = null!;

	public ParityVerificationTests52(BigQuerySession session) => _session = session;

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

	// --- DATE() constructor NULL args ---

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/date_functions#date
	//   Returns NULL if any input is NULL.

	[Fact]
	public async Task DateConstructor_NullYear_ReturnsNull()
	{
		var row = await ScalarRowAsync("SELECT DATE(CAST(NULL AS INT64), 1, 1)");
		Assert.Null(row[0]);
	}

	[Fact]
	public async Task DateConstructor_NullMonth_ReturnsNull()
	{
		var row = await ScalarRowAsync("SELECT DATE(2024, CAST(NULL AS INT64), 1)");
		Assert.Null(row[0]);
	}

	[Fact]
	public async Task DateConstructor_NullDay_ReturnsNull()
	{
		var row = await ScalarRowAsync("SELECT DATE(2024, 1, CAST(NULL AS INT64))");
		Assert.Null(row[0]);
	}

	// --- DATETIME() constructor NULL args ---

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/datetime_functions#datetime
	//   Returns NULL if any input is NULL.

	[Fact]
	public async Task DateTimeConstructor_NullYear_ReturnsNull()
	{
		var row = await ScalarRowAsync("SELECT DATETIME(CAST(NULL AS INT64), 1, 1, 0, 0, 0)");
		Assert.Null(row[0]);
	}

	[Fact]
	public async Task DateTimeConstructor_NullMonth_ReturnsNull()
	{
		var row = await ScalarRowAsync("SELECT DATETIME(2024, CAST(NULL AS INT64), 1, 0, 0, 0)");
		Assert.Null(row[0]);
	}

	// --- INSTR NULL position/occurrence ---

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/string_functions#instr
	//   Returns NULL if any input is NULL.

	[Fact]
	public async Task Instr_NullPosition_ReturnsNull()
	{
		var row = await ScalarRowAsync("SELECT INSTR('hello', 'l', CAST(NULL AS INT64))");
		Assert.Null(row[0]);
	}

	[Fact]
	public async Task Instr_NullOccurrence_ReturnsNull()
	{
		var row = await ScalarRowAsync("SELECT INSTR('hello', 'l', 1, CAST(NULL AS INT64))");
		Assert.Null(row[0]);
	}

	// --- REGEXP_EXTRACT NULL position/occurrence ---

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/string_functions#regexp_extract
	//   Returns NULL if any input is NULL.

	[Fact]
	public async Task RegexpExtract_NullPosition_ReturnsNull()
	{
		var row = await ScalarRowAsync("SELECT REGEXP_EXTRACT('hello world', r'(\\w+)', CAST(NULL AS INT64))");
		Assert.Null(row[0]);
	}

	[Fact]
	public async Task RegexpExtract_NullOccurrence_ReturnsNull()
	{
		var row = await ScalarRowAsync("SELECT REGEXP_EXTRACT('hello world', r'(\\w+)', 1, CAST(NULL AS INT64))");
		Assert.Null(row[0]);
	}
}
