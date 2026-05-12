using Google.Cloud.BigQuery.V2;
using Xunit;

namespace InMemoryEmulator.BigQuery.Tests.Integration;

/// <summary>
/// Parity verification tests batch 36: Table operations with various column types,
/// INSERT with expressions, UPDATE multiple columns, complex JOIN scenarios,
/// window functions over real tables, and CTE with DML patterns.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ParityVerificationTests36 : IAsyncLifetime
{
	private readonly BigQuerySession _session;
	private ITestDatasetFixture _fixture = null!;
	private string _ds = null!;

	public ParityVerificationTests36(BigQuerySession session) => _session = session;

	public async ValueTask InitializeAsync()
	{
		_fixture = TestFixtureFactory.Create(_session);
		_ds = $"pv36_{Guid.NewGuid():N}"[..28];
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

	private async Task Exec(string sql)
	{
		var client = await _fixture.GetClientAsync();
		await client.ExecuteQueryAsync(sql.Replace("{ds}", _ds), parameters: null);
	}

	// ───────────────────────────────────────────────────────────────────────────
	// Table with timestamp column
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Table_TimestampColumn()
	{
		await Exec("CREATE TABLE `{ds}.events` (id INT64, name STRING, ts TIMESTAMP)");
		await Exec(@"INSERT INTO `{ds}.events` VALUES 
			(1, 'start', TIMESTAMP '2024-01-01 10:00:00 UTC'),
			(2, 'middle', TIMESTAMP '2024-01-01 12:00:00 UTC'),
			(3, 'end', TIMESTAMP '2024-01-01 14:00:00 UTC')");

		var rows = await Q(@"
			SELECT name, TIMESTAMP_DIFF(ts, TIMESTAMP '2024-01-01 10:00:00 UTC', HOUR) AS hours_since_start
			FROM `{ds}.events`
			ORDER BY id");
		Assert.Equal(3, rows.Count);
		Assert.Equal("start", rows[0][0]?.ToString());
		Assert.Equal("0", rows[0][1]?.ToString());
		Assert.Equal("middle", rows[1][0]?.ToString());
		Assert.Equal("2", rows[1][1]?.ToString());
		Assert.Equal("end", rows[2][0]?.ToString());
		Assert.Equal("4", rows[2][1]?.ToString());
	}

	// ───────────────────────────────────────────────────────────────────────────
	// Table with date column
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Table_DateColumn()
	{
		await Exec("CREATE TABLE `{ds}.tasks` (id INT64, title STRING, due_date DATE)");
		await Exec(@"INSERT INTO `{ds}.tasks` VALUES 
			(1, 'Task A', DATE '2024-06-01'),
			(2, 'Task B', DATE '2024-06-15'),
			(3, 'Task C', DATE '2024-07-01')");

		var rows = await Q(@"
			SELECT title FROM `{ds}.tasks`
			WHERE due_date BETWEEN DATE '2024-06-01' AND DATE '2024-06-30'
			ORDER BY due_date");
		Assert.Equal(2, rows.Count);
		Assert.Equal("Task A", rows[0][0]?.ToString());
		Assert.Equal("Task B", rows[1][0]?.ToString());
	}

	// ───────────────────────────────────────────────────────────────────────────
	// UPDATE with multiple SET columns
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Update_MultipleColumns()
	{
		await Exec("CREATE TABLE `{ds}.inventory` (id INT64, name STRING, qty INT64, price FLOAT64)");
		await Exec("INSERT INTO `{ds}.inventory` VALUES (1, 'Widget', 100, 5.0), (2, 'Gadget', 50, 10.0)");
		await Exec("UPDATE `{ds}.inventory` SET qty = qty - 10, price = price * 1.05 WHERE id = 1");

		var rows = await Q("SELECT name, qty, price FROM `{ds}.inventory` WHERE id = 1");
		Assert.Single(rows);
		Assert.Equal("Widget", rows[0][0]?.ToString());
		Assert.Equal("90", rows[0][1]?.ToString());
		Assert.Equal("5.25", rows[0][2]?.ToString());
	}

	// ───────────────────────────────────────────────────────────────────────────
	// INSERT SELECT with transformation
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task InsertSelect_WithTransform()
	{
		await Exec("CREATE TABLE `{ds}.src` (id INT64, val STRING)");
		await Exec("CREATE TABLE `{ds}.dst` (id INT64, upper_val STRING)");
		await Exec("INSERT INTO `{ds}.src` VALUES (1, 'hello'), (2, 'world')");
		await Exec("INSERT INTO `{ds}.dst` SELECT id, UPPER(val) FROM `{ds}.src`");

		var rows = await Q("SELECT id, upper_val FROM `{ds}.dst` ORDER BY id");
		Assert.Equal(2, rows.Count);
		Assert.Equal("HELLO", rows[0][1]?.ToString());
		Assert.Equal("WORLD", rows[1][1]?.ToString());
	}

	// ───────────────────────────────────────────────────────────────────────────
	// Window over table data
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Table_WindowRunningTotal()
	{
		await Exec("CREATE TABLE `{ds}.txns` (id INT64, amount FLOAT64)");
		await Exec("INSERT INTO `{ds}.txns` VALUES (1, 100), (2, -30), (3, 50), (4, -20)");

		var rows = await Q(@"
			SELECT id, amount, SUM(amount) OVER (ORDER BY id) AS running_balance
			FROM `{ds}.txns`
			ORDER BY id");
		Assert.Equal(4, rows.Count);
		Assert.Equal("100", rows[0][2]?.ToString()); // 100
		Assert.Equal("70", rows[1][2]?.ToString()); // 100 - 30
		Assert.Equal("120", rows[2][2]?.ToString()); // 70 + 50
		Assert.Equal("100", rows[3][2]?.ToString()); // 120 - 20
	}

	// ───────────────────────────────────────────────────────────────────────────
	// JOIN with aggregation
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Table_JoinAggregate()
	{
		await Exec("CREATE TABLE `{ds}.categories` (id INT64, name STRING)");
		await Exec("CREATE TABLE `{ds}.products` (id INT64, cat_id INT64, price FLOAT64)");
		await Exec("INSERT INTO `{ds}.categories` VALUES (1, 'Electronics'), (2, 'Clothing')");
		await Exec("INSERT INTO `{ds}.products` VALUES (1, 1, 999), (2, 1, 499), (3, 2, 49), (4, 2, 79)");

		var rows = await Q(@"
			SELECT c.name, COUNT(*) AS cnt, SUM(p.price) AS total
			FROM `{ds}.products` p JOIN `{ds}.categories` c ON p.cat_id = c.id
			GROUP BY c.name
			ORDER BY total DESC");
		Assert.Equal(2, rows.Count);
		Assert.Equal("Electronics", rows[0][0]?.ToString());
		Assert.Equal("2", rows[0][1]?.ToString());
		Assert.Equal("1498", rows[0][2]?.ToString());
		Assert.Equal("Clothing", rows[1][0]?.ToString());
		Assert.Equal("2", rows[1][1]?.ToString());
		Assert.Equal("128", rows[1][2]?.ToString());
	}

	// ───────────────────────────────────────────────────────────────────────────
	// BOOLEAN column operations
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Table_BooleanColumn()
	{
		await Exec("CREATE TABLE `{ds}.flags` (id INT64, name STRING, active BOOL)");
		await Exec("INSERT INTO `{ds}.flags` VALUES (1, 'A', TRUE), (2, 'B', FALSE), (3, 'C', TRUE)");

		var rows = await Q("SELECT name FROM `{ds}.flags` WHERE active = TRUE ORDER BY name");
		Assert.Equal(2, rows.Count);
		Assert.Equal("A", rows[0][0]?.ToString());
		Assert.Equal("C", rows[1][0]?.ToString());
	}

	// ───────────────────────────────────────────────────────────────────────────
	// NULL handling in table operations
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Table_NullValues()
	{
		await Exec("CREATE TABLE `{ds}.nullable` (id INT64, val STRING)");
		await Exec("INSERT INTO `{ds}.nullable` VALUES (1, 'hello'), (2, NULL), (3, 'world')");

		var rows = await Q(@"
			SELECT id, COALESCE(val, 'N/A') AS display_val
			FROM `{ds}.nullable`
			ORDER BY id");
		Assert.Equal(3, rows.Count);
		Assert.Equal("hello", rows[0][1]?.ToString());
		Assert.Equal("N/A", rows[1][1]?.ToString());
		Assert.Equal("world", rows[2][1]?.ToString());
	}

	// ───────────────────────────────────────────────────────────────────────────
	// Complex UPDATE with CASE
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Update_WithCase()
	{
		await Exec("CREATE TABLE `{ds}.scores` (id INT64, name STRING, score INT64, grade STRING)");
		await Exec(@"INSERT INTO `{ds}.scores` VALUES 
			(1, 'Alice', 95, ''), (2, 'Bob', 72, ''), (3, 'Carol', 88, '')");
		await Exec(@"UPDATE `{ds}.scores` SET grade = 
			CASE WHEN score >= 90 THEN 'A' WHEN score >= 80 THEN 'B' ELSE 'C' END
			WHERE TRUE");

		var rows = await Q("SELECT name, grade FROM `{ds}.scores` ORDER BY name");
		Assert.Equal("Alice", rows[0][0]?.ToString());
		Assert.Equal("A", rows[0][1]?.ToString());
		Assert.Equal("Bob", rows[1][0]?.ToString());
		Assert.Equal("C", rows[1][1]?.ToString());
		Assert.Equal("Carol", rows[2][0]?.ToString());
		Assert.Equal("B", rows[2][1]?.ToString());
	}

	// ───────────────────────────────────────────────────────────────────────────
	// COUNT DISTINCT
	// ───────────────────────────────────────────────────────────────────────────

	[Fact] public async Task Table_CountDistinct()
	{
		await Exec("CREATE TABLE `{ds}.visits` (id INT64, user_id INT64, page STRING)");
		await Exec(@"INSERT INTO `{ds}.visits` VALUES 
			(1, 1, 'home'), (2, 1, 'about'), (3, 2, 'home'),
			(4, 2, 'home'), (5, 3, 'contact')");

		var result = await S("SELECT COUNT(DISTINCT user_id) FROM `{ds}.visits`");
		Assert.Equal("3", result);
	}
}
