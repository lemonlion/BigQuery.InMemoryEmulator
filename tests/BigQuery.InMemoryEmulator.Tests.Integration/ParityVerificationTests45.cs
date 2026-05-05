using Google.Cloud.BigQuery.V2;
using Xunit;

namespace BigQuery.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Parity verification tests batch 45: LPAD/RPAD truncation, DATETIME/TIME NULL handling,
/// FORMAT NULL handling, UNIX_*/TIMESTAMP_* NULL handling, DATE_ADD/SUB NULL interval.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ParityVerificationTests45 : IAsyncLifetime
{
	private readonly BigQuerySession _session;
	private ITestDatasetFixture _fixture = null!;

	public ParityVerificationTests45(BigQuerySession session) => _session = session;

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
	// LPAD: correct padding behavior
	// ============================================================

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/string_functions#lpad
	//   "Pads a STRING value with leading characters."
	//   LPAD('abc', 10, 'ghd') → 'ghdghdgabc'
	[Fact]
	public async Task Lpad_MultiCharPattern_PadsCorrectly()
	{
		var result = await ScalarAsync("SELECT LPAD('abc', 10, 'ghd')");
		Assert.Equal("ghdghdgabc", result);
	}

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/string_functions#lpad
	//   LPAD('hello', 3, 'x') → 'hel' (original truncated from right)
	[Fact]
	public async Task Lpad_ShorterLength_TruncatesOriginal()
	{
		var result = await ScalarAsync("SELECT LPAD('hello', 3, 'x')");
		Assert.Equal("hel", result);
	}

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/string_functions#lpad
	//   LPAD('hi', 5, 'ab') → 'abahi'
	[Fact]
	public async Task Lpad_TwoCharPattern_PadsCorrectly()
	{
		var result = await ScalarAsync("SELECT LPAD('hi', 5, 'ab')");
		Assert.Equal("abahi", result);
	}

	// ============================================================
	// RPAD: correct padding behavior
	// ============================================================

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/string_functions#rpad
	//   "Pads a STRING value with trailing characters."
	//   RPAD('abc', 10, 'ghd') → 'abcghdghdg'
	[Fact]
	public async Task Rpad_MultiCharPattern_PadsCorrectly()
	{
		var result = await ScalarAsync("SELECT RPAD('abc', 10, 'ghd')");
		Assert.Equal("abcghdghdg", result);
	}

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/string_functions#rpad
	//   RPAD('hello', 3, 'x') → 'hel' (original truncated from right)
	[Fact]
	public async Task Rpad_ShorterLength_TruncatesOriginal()
	{
		var result = await ScalarAsync("SELECT RPAD('hello', 3, 'x')");
		Assert.Equal("hel", result);
	}

	// ============================================================
	// DATETIME_DIFF / DATETIME_ADD / DATETIME_SUB NULL handling
	// ============================================================

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/datetime_functions#datetime_diff
	//   Returns NULL if any input is NULL.
	[Fact]
	public async Task DatetimeDiff_NullFirstArg_ReturnsNull()
	{
		var result = await ScalarAsync(
			"SELECT DATETIME_DIFF(CAST(NULL AS DATETIME), DATETIME '2024-01-01 00:00:00', DAY)");
		Assert.Equal("NULL", result);
	}

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/datetime_functions#datetime_add
	//   Returns NULL if any input is NULL.
	[Fact]
	public async Task DatetimeAdd_NullFirstArg_ReturnsNull()
	{
		var result = await ScalarAsync(
			"SELECT DATETIME_ADD(CAST(NULL AS DATETIME), INTERVAL 1 DAY)");
		Assert.Equal("NULL", result);
	}

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/datetime_functions#datetime_sub
	//   Returns NULL if any input is NULL.
	[Fact]
	public async Task DatetimeSub_NullFirstArg_ReturnsNull()
	{
		var result = await ScalarAsync(
			"SELECT DATETIME_SUB(CAST(NULL AS DATETIME), INTERVAL 1 DAY)");
		Assert.Equal("NULL", result);
	}

	// ============================================================
	// TIME_DIFF NULL handling
	// ============================================================

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/time_functions#time_diff
	//   Returns NULL if any argument is NULL.
	[Fact]
	public async Task TimeDiff_NullFirstArg_ReturnsNull()
	{
		var result = await ScalarAsync(
			"SELECT TIME_DIFF(CAST(NULL AS TIME), TIME '12:00:00', HOUR)");
		Assert.Equal("NULL", result);
	}

	[Fact]
	public async Task TimeDiff_NullSecondArg_ReturnsNull()
	{
		var result = await ScalarAsync(
			"SELECT TIME_DIFF(TIME '12:00:00', CAST(NULL AS TIME), HOUR)");
		Assert.Equal("NULL", result);
	}

	// ============================================================
	// FORMAT NULL handling
	// ============================================================

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/string_functions#format_string
	//   "The function generally produces a NULL value if a NULL argument is present."
	//   "However...if the format specifier is %t or %T, a NULL value produces 'NULL' (without quotes)."
	[Fact]
	public async Task Format_NullArg_NonTSpecifier_ReturnsNull()
	{
		var result = await ScalarAsync("SELECT FORMAT('%d', CAST(NULL AS INT64))");
		Assert.Equal("NULL", result);
	}

	[Fact]
	public async Task Format_NullArg_TSpecifier_ReturnsNullString()
	{
		var result = await ScalarAsync("SELECT FORMAT('%t', CAST(NULL AS INT64))");
		Assert.Equal("NULL", result);
	}

	// ============================================================
	// UNIX_SECONDS / TIMESTAMP_SECONDS NULL handling
	// ============================================================

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/timestamp_functions#unix_seconds
	//   Returns NULL if timestamp_expression is NULL.
	[Fact]
	public async Task UnixSeconds_Null_ReturnsNull()
	{
		var result = await ScalarAsync("SELECT UNIX_SECONDS(CAST(NULL AS TIMESTAMP))");
		Assert.Equal("NULL", result);
	}

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/timestamp_functions#timestamp_seconds
	//   Returns NULL if int64_expression is NULL.
	[Fact]
	public async Task TimestampSeconds_Null_ReturnsNull()
	{
		var result = await ScalarAsync("SELECT TIMESTAMP_SECONDS(CAST(NULL AS INT64))");
		Assert.Equal("NULL", result);
	}

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/timestamp_functions#unix_millis
	//   Returns NULL if timestamp_expression is NULL.
	[Fact]
	public async Task UnixMillis_Null_ReturnsNull()
	{
		var result = await ScalarAsync("SELECT UNIX_MILLIS(CAST(NULL AS TIMESTAMP))");
		Assert.Equal("NULL", result);
	}

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/timestamp_functions#unix_micros
	//   Returns NULL if timestamp_expression is NULL.
	[Fact]
	public async Task UnixMicros_Null_ReturnsNull()
	{
		var result = await ScalarAsync("SELECT UNIX_MICROS(CAST(NULL AS TIMESTAMP))");
		Assert.Equal("NULL", result);
	}

	// ============================================================
	// DATE_ADD / DATE_SUB NULL interval
	// ============================================================

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/date_functions#date_add
	//   Returns NULL if any argument is NULL.
	[Fact]
	public async Task DateAdd_NullInterval_ReturnsNull()
	{
		var result = await ScalarAsync(
			"SELECT DATE_ADD(DATE '2024-01-01', INTERVAL CAST(NULL AS INT64) DAY)");
		Assert.Equal("NULL", result);
	}

	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/date_functions#date_sub
	//   Returns NULL if any argument is NULL.
	[Fact]
	public async Task DateSub_NullInterval_ReturnsNull()
	{
		var result = await ScalarAsync(
			"SELECT DATE_SUB(DATE '2024-01-01', INTERVAL CAST(NULL AS INT64) DAY)");
		Assert.Equal("NULL", result);
	}
}
