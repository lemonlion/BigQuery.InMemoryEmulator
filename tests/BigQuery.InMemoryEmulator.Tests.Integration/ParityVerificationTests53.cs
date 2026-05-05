using Google.Cloud.BigQuery.V2;
using Xunit;

namespace BigQuery.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Parity verification tests batch 53: FORMAT_TIMESTAMP %Z timezone, window function NULL args.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ParityVerificationTests53 : IAsyncLifetime
{
	private readonly BigQuerySession _session;
	private ITestDatasetFixture _fixture = null!;

	public ParityVerificationTests53(BigQuerySession session) => _session = session;

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

	// --- FORMAT_TIMESTAMP %Z ---

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/format-elements#format_elements_date_time
	//   "%Z: The time zone name."

	[Fact]
	public async Task FormatTimestamp_PercentZ_ReturnsUTC()
	{
		var result = await ScalarAsync("SELECT FORMAT_TIMESTAMP('%Z', TIMESTAMP '2024-01-01 12:00:00 UTC')");
		Assert.Equal("UTC", result);
	}

	[Fact]
	public async Task FormatTimestamp_DateAndTimezone_FormattedCorrectly()
	{
		var result = await ScalarAsync("SELECT FORMAT_TIMESTAMP('%Y-%m-%d %Z', TIMESTAMP '2024-06-15 00:00:00 UTC')");
		Assert.Equal("2024-06-15 UTC", result);
	}

	// --- PARSE_TIMESTAMP with %Z ---

	[Fact]
	public async Task ParseTimestamp_PercentZ_UTC_Parses()
	{
		var result = await ScalarAsync("SELECT CAST(PARSE_TIMESTAMP('%Y-%m-%d %Z', '2024-01-01 UTC') AS STRING)");
		Assert.Equal("2024-01-01 00:00:00+00", result);
	}

	// --- Window function NULL offset ---

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/navigation_functions#lag
	//   Returns NULL if offset argument is NULL.

	[Fact]
	public async Task Lag_NullOffset_ReturnsNull()
	{
		var result = await ScalarAsync(
			"SELECT LAG(x, CAST(NULL AS INT64)) OVER (ORDER BY x) FROM UNNEST([1,2,3]) AS x LIMIT 1");
		Assert.Equal("NULL", result);
	}

	[Fact]
	public async Task Lead_NullOffset_ReturnsNull()
	{
		var result = await ScalarAsync(
			"SELECT LEAD(x, CAST(NULL AS INT64)) OVER (ORDER BY x) FROM UNNEST([1,2,3]) AS x LIMIT 1");
		Assert.Equal("NULL", result);
	}
}
