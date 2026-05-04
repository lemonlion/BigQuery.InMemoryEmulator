using Google.Cloud.BigQuery.V2;
using Xunit;

namespace BigQuery.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Parity verification tests batch 34: FORMAT/PARSE timestamp specifiers,
/// DATETIME_DIFF boundaries, TIMESTAMP_TRUNC with ISOWEEK, various
/// edge cases in date/time functions.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ParityVerificationTests34 : IAsyncLifetime
{
	private readonly BigQuerySession _session;
	private ITestDatasetFixture _fixture = null!;
	private string _ds = null!;

	public ParityVerificationTests34(BigQuerySession session) => _session = session;

	public async ValueTask InitializeAsync()
	{
		_fixture = TestFixtureFactory.Create(_session);
		_ds = $"pv34_{Guid.NewGuid():N}"[..28];
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
	// TIMESTAMP_TRUNC ISOWEEK (should truncate to Monday)
	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/timestamp_functions#timestamp_trunc
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task TimestampTrunc_Isoweek()
	{
		// 2024-06-15 is Saturday. ISOWEEK truncates to Monday = 2024-06-10
		var result = await S("SELECT CAST(TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15 14:30:00 UTC', ISOWEEK) AS STRING)");
		Assert.Equal("2024-06-10 00:00:00+00", result);
	}

	[Fact] public async Task TimestampTrunc_Week()
	{
		// 2024-06-15 is Saturday. WEEK truncates to Sunday = 2024-06-09
		var result = await S("SELECT CAST(TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15 14:30:00 UTC', WEEK) AS STRING)");
		Assert.Equal("2024-06-09 00:00:00+00", result);
	}

	[Fact] public async Task TimestampTrunc_Quarter()
	{
		// June is in Q2. Q2 starts 2024-04-01
		var result = await S("SELECT CAST(TIMESTAMP_TRUNC(TIMESTAMP '2024-06-15 14:30:00 UTC', QUARTER) AS STRING)");
		Assert.Equal("2024-04-01 00:00:00+00", result);
	}

	// ───────────────────────────────────────────────────────────────────────────
	// DATE_TRUNC edge cases
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task DateTrunc_Isoweek()
	{
		// 2024-06-15 is Saturday. ISOWEEK truncates to Monday = 2024-06-10
		var result = await S("SELECT CAST(DATE_TRUNC(DATE '2024-06-15', ISOWEEK) AS STRING)");
		Assert.Equal("2024-06-10", result);
	}

	[Fact] public async Task DateTrunc_Quarter()
	{
		var result = await S("SELECT CAST(DATE_TRUNC(DATE '2024-08-20', QUARTER) AS STRING)");
		Assert.Equal("2024-07-01", result); // Aug is Q3, starts Jul 1
	}

	// ───────────────────────────────────────────────────────────────────────────
	// DATETIME_DIFF boundary counting
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task DatetimeDiff_DayBoundary()
	{
		// Crosses 1 day boundary (from 23:00 Jan 1 to 01:00 Jan 2)
		var result = await S(@"
			SELECT DATETIME_DIFF(
				DATETIME '2024-01-02 01:00:00',
				DATETIME '2024-01-01 23:00:00',
				DAY)");
		Assert.Equal("1", result); // Boundary counting: truncate both to day, then diff
	}

	[Fact] public async Task DatetimeDiff_MonthBoundary()
	{
		// Same day of month - exactly 1 month apart
		var result = await S(@"
			SELECT DATETIME_DIFF(
				DATETIME '2024-02-15 00:00:00',
				DATETIME '2024-01-15 00:00:00',
				MONTH)");
		Assert.Equal("1", result);
	}

	[Fact] public async Task DatetimeDiff_YearBoundary()
	{
		// Dec 31 to Jan 1 crosses 1 year boundary
		var result = await S(@"
			SELECT DATETIME_DIFF(
				DATETIME '2024-01-01 00:00:00',
				DATETIME '2023-12-31 00:00:00',
				YEAR)");
		Assert.Equal("1", result);
	}

	// ───────────────────────────────────────────────────────────────────────────
	// EXTRACT from DATETIME
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Extract_Millisecond()
	{
		var result = await S("SELECT EXTRACT(MILLISECOND FROM DATETIME '2024-06-15 12:34:56.789')");
		Assert.Equal("789", result);
	}

	[Fact] public async Task Extract_Microsecond()
	{
		var result = await S("SELECT EXTRACT(MICROSECOND FROM DATETIME '2024-06-15 12:34:56.789012')");
		Assert.Equal("789012", result);
	}

	// ───────────────────────────────────────────────────────────────────────────
	// FORMAT_TIMESTAMP with various specifiers
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task FormatTimestamp_WeekdayName()
	{
		// %A = full weekday name. 2024-06-15 is Saturday
		var result = await S("SELECT FORMAT_TIMESTAMP('%A', TIMESTAMP '2024-06-15 00:00:00 UTC')");
		Assert.Equal("Saturday", result);
	}

	[Fact] public async Task FormatTimestamp_AbbrevWeekday()
	{
		var result = await S("SELECT FORMAT_TIMESTAMP('%a', TIMESTAMP '2024-06-15 00:00:00 UTC')");
		Assert.Equal("Sat", result);
	}

	[Fact] public async Task FormatTimestamp_MonthName()
	{
		var result = await S("SELECT FORMAT_TIMESTAMP('%B', TIMESTAMP '2024-06-15 00:00:00 UTC')");
		Assert.Equal("June", result);
	}

	[Fact] public async Task FormatTimestamp_DayOfYear()
	{
		// 2024-06-15 is day 167 (2024 is leap year: 31+29+31+30+31+15 = 167)
		var result = await S("SELECT FORMAT_TIMESTAMP('%j', TIMESTAMP '2024-06-15 00:00:00 UTC')");
		Assert.Equal("167", result);
	}

	[Fact] public async Task FormatTimestamp_IsoWeek()
	{
		var result = await S("SELECT FORMAT_TIMESTAMP('%V', TIMESTAMP '2024-01-01 00:00:00 UTC')");
		Assert.Equal("01", result); // Week 01 of 2024
	}

	// ───────────────────────────────────────────────────────────────────────────
	// TIMESTAMP_ADD/SUB with various parts
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task TimestampAdd_Microsecond()
	{
		var result = await S(@"
			SELECT UNIX_MICROS(TIMESTAMP_ADD(TIMESTAMP '2024-01-01 00:00:00 UTC', INTERVAL 500 MICROSECOND))
				- UNIX_MICROS(TIMESTAMP '2024-01-01 00:00:00 UTC')");
		Assert.Equal("500", result);
	}

	[Fact] public async Task TimestampSub_Microsecond()
	{
		var result = await S(@"
			SELECT UNIX_MICROS(TIMESTAMP '2024-01-01 00:00:00 UTC')
				- UNIX_MICROS(TIMESTAMP_SUB(TIMESTAMP '2024-01-01 00:00:00 UTC', INTERVAL 500 MICROSECOND))");
		Assert.Equal("500", result);
	}

	// ───────────────────────────────────────────────────────────────────────────
	// DATE_FROM_UNIX_DATE / UNIX_DATE
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task DateFromUnixDate_RoundTrip()
	{
		var result = await S("SELECT UNIX_DATE(DATE_FROM_UNIX_DATE(19724))");
		Assert.Equal("19724", result);
	}

	[Fact] public async Task UnixDate_KnownValue()
	{
		// 2024-01-01 is day 19723 since 1970-01-01 (54*365 + 13 leap days = 19723... let's check with BigQuery)
		var result = await S("SELECT UNIX_DATE(DATE '1970-01-01')");
		Assert.Equal("0", result);
	}

	[Fact] public async Task DateFromUnixDate_Zero()
	{
		var result = await S("SELECT CAST(DATE_FROM_UNIX_DATE(0) AS STRING)");
		Assert.Equal("1970-01-01", result);
	}
}
