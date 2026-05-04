using Google.Cloud.BigQuery.V2;
using Xunit;

namespace BigQuery.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Parity verification tests batch 32: Precision and timestamp conversion edge cases,
/// targeting the TIMESTAMP_MICROS integer division precision loss bug.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ParityVerificationTests32 : IAsyncLifetime
{
	private readonly BigQuerySession _session;
	private ITestDatasetFixture _fixture = null!;
	private string _ds = null!;

	public ParityVerificationTests32(BigQuerySession session) => _session = session;

	public async ValueTask InitializeAsync()
	{
		_fixture = TestFixtureFactory.Create(_session);
		_ds = $"pv32_{Guid.NewGuid():N}"[..28];
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
	// TIMESTAMP_MICROS precision: sub-millisecond values must be preserved
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task TimestampMicros_RoundTrip_SubMillisecond()
	{
		// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/timestamp_functions#timestamp_micros
		// TIMESTAMP_MICROS must preserve sub-millisecond precision (500 microseconds)
		var result = await S("SELECT UNIX_MICROS(TIMESTAMP_MICROS(1704063600000500))");
		Assert.Equal("1704063600000500", result);
	}

	[Fact] public async Task TimestampMicros_RoundTrip_Exact()
	{
		// When divisible by 1000, should also work correctly
		var result = await S("SELECT UNIX_MICROS(TIMESTAMP_MICROS(1704063600000000))");
		Assert.Equal("1704063600000000", result);
	}

	[Fact] public async Task TimestampMicros_RoundTrip_OddMicros()
	{
		// Test with 123 microseconds remainder
		var result = await S("SELECT UNIX_MICROS(TIMESTAMP_MICROS(1704063600123456))");
		Assert.Equal("1704063600123456", result);
	}

	[Fact] public async Task TimestampMicros_RoundTrip_OneMicro()
	{
		// Edge case: just 1 microsecond past a millisecond boundary
		var result = await S("SELECT UNIX_MICROS(TIMESTAMP_MICROS(1704063600000001))");
		Assert.Equal("1704063600000001", result);
	}

	// ───────────────────────────────────────────────────────────────────────────
	// UNIX_SECONDS round-trip
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task TimestampSeconds_RoundTrip()
	{
		var result = await S("SELECT UNIX_SECONDS(TIMESTAMP_SECONDS(1704063600))");
		Assert.Equal("1704063600", result);
	}

	// ───────────────────────────────────────────────────────────────────────────
	// UNIX_MILLIS round-trip
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task TimestampMillis_RoundTrip()
	{
		var result = await S("SELECT UNIX_MILLIS(TIMESTAMP_MILLIS(1704063600123))");
		Assert.Equal("1704063600123", result);
	}

	// ───────────────────────────────────────────────────────────────────────────
	// TIMESTAMP_MICROS compared to TIMESTAMP_MILLIS precision
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task TimestampMicros_MorePreciseThanMillis()
	{
		// TIMESTAMP_MICROS(1000500) should differ from TIMESTAMP_MICROS(1000000) by 500 micros
		var result = await S(@"
			SELECT UNIX_MICROS(TIMESTAMP_MICROS(1000500)) - UNIX_MICROS(TIMESTAMP_MICROS(1000000))");
		Assert.Equal("500", result);
	}

	// ───────────────────────────────────────────────────────────────────────────
	// Additional timestamp precision tests
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task UnixMicros_FromTimestamp()
	{
		// 2024-01-01 00:00:00 UTC = 1704067200 seconds = 1704067200000000 micros
		var result = await S("SELECT UNIX_MICROS(TIMESTAMP '2024-01-01 00:00:00 UTC')");
		Assert.Equal("1704067200000000", result);
	}

	[Fact] public async Task UnixSeconds_FromTimestamp()
	{
		var result = await S("SELECT UNIX_SECONDS(TIMESTAMP '2024-01-01 00:00:00 UTC')");
		Assert.Equal("1704067200", result);
	}

	[Fact] public async Task UnixMillis_FromTimestamp()
	{
		var result = await S("SELECT UNIX_MILLIS(TIMESTAMP '2024-01-01 00:00:00 UTC')");
		Assert.Equal("1704067200000", result);
	}
}
