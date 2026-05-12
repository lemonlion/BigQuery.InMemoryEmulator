using Google.Cloud.BigQuery.V2;
using Xunit;

namespace InMemoryEmulator.BigQuery.Tests.Integration;

/// <summary>
/// Parity verification tests batch 25: Testing table-based operations with DML and queries.
/// Real table operations: CREATE TABLE, INSERT, UPDATE, DELETE, SELECT with JOIN across tables,
/// indexed access patterns, multi-column operations, WHERE clause edge cases.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ParityVerificationTests25 : IAsyncLifetime
{
	private readonly BigQuerySession _session;
	private ITestDatasetFixture _fixture = null!;
	private string _ds = null!;

	public ParityVerificationTests25(BigQuerySession session) => _session = session;

	public async ValueTask InitializeAsync()
	{
		_fixture = TestFixtureFactory.Create(_session);
		_ds = $"pv25_{Guid.NewGuid():N}"[..28];
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
	// Table-based operations: Create, Insert, Select, Update, Delete
	// ───────────────────────────────────────────────────────────────────────────

	// Go emulator requires column list in INSERT VALUES.
	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/dml-syntax#insert_statement
	//   "Column names are optional if the target table is not an ingestion-time partitioned table."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.EmulatorDivergence)]
	public async Task Table_CreateInsertSelect()
	{
		await Exec("CREATE TABLE `{ds}.employees` (id INT64, name STRING, dept STRING, salary FLOAT64)");
		await Exec("INSERT INTO `{ds}.employees` VALUES (1, 'Alice', 'Eng', 95000)");
		await Exec("INSERT INTO `{ds}.employees` VALUES (2, 'Bob', 'Eng', 85000)");
		await Exec("INSERT INTO `{ds}.employees` VALUES (3, 'Charlie', 'Sales', 70000)");
		await Exec("INSERT INTO `{ds}.employees` VALUES (4, 'Diana', 'Sales', 80000)");
		await Exec("INSERT INTO `{ds}.employees` VALUES (5, 'Eve', 'HR', 75000)");

		var rows = await Q("SELECT name, salary FROM `{ds}.employees` WHERE dept = 'Eng' ORDER BY salary DESC");
		Assert.Equal(2, rows.Count);
		Assert.Equal("Alice", rows[0][0]?.ToString());
		Assert.Equal("95000", rows[0][1]?.ToString());
		Assert.Equal("Bob", rows[1][0]?.ToString());
	}

	// Go emulator requires column list in INSERT VALUES.
	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/dml-syntax#insert_statement
	//   "Column names are optional if the target table is not an ingestion-time partitioned table."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.EmulatorDivergence)]
	public async Task Table_AggregateByDept()
	{
		await Exec("CREATE TABLE `{ds}.staff` (id INT64, name STRING, dept STRING, salary FLOAT64)");
		await Exec("INSERT INTO `{ds}.staff` VALUES (1, 'Alice', 'Eng', 95000), (2, 'Bob', 'Eng', 85000), (3, 'Charlie', 'Sales', 70000), (4, 'Diana', 'Sales', 80000), (5, 'Eve', 'HR', 75000)");

		var rows = await Q("SELECT dept, COUNT(*) AS cnt, AVG(salary) AS avg_sal FROM `{ds}.staff` GROUP BY dept ORDER BY dept");
		Assert.Equal(3, rows.Count);
		Assert.Equal("Eng", rows[0][0]?.ToString());
		Assert.Equal("2", rows[0][1]?.ToString());
		Assert.Equal("90000", rows[0][2]?.ToString());
		Assert.Equal("HR", rows[1][0]?.ToString());
		Assert.Equal("1", rows[1][1]?.ToString());
		Assert.Equal("Sales", rows[2][0]?.ToString());
		Assert.Equal("2", rows[2][1]?.ToString());
	}

	// Go emulator requires column list in INSERT VALUES.
	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/dml-syntax#insert_statement
	//   "Column names are optional if the target table is not an ingestion-time partitioned table."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.EmulatorDivergence)]
	public async Task Table_UpdateWithWhere()
	{
		await Exec("CREATE TABLE `{ds}.products` (id INT64, name STRING, price FLOAT64)");
		await Exec("INSERT INTO `{ds}.products` VALUES (1, 'Widget', 10.0), (2, 'Gadget', 20.0), (3, 'Doohickey', 30.0)");
		await Exec("UPDATE `{ds}.products` SET price = price * 1.1 WHERE price > 15");

		var rows = await Q("SELECT name, price FROM `{ds}.products` ORDER BY id");
		Assert.Equal("10", rows[0][1]?.ToString()); // unchanged
		Assert.Equal("22", rows[1][1]?.ToString()); // 20 * 1.1
		Assert.Equal("33", rows[2][1]?.ToString()); // 30 * 1.1
	}

	// Go emulator requires column list in INSERT VALUES.
	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/dml-syntax#insert_statement
	//   "Column names are optional if the target table is not an ingestion-time partitioned table."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.EmulatorDivergence)]
	public async Task Table_DeleteWithWhere()
	{
		await Exec("CREATE TABLE `{ds}.items` (id INT64, name STRING, active BOOL)");
		await Exec("INSERT INTO `{ds}.items` VALUES (1, 'A', TRUE), (2, 'B', FALSE), (3, 'C', TRUE), (4, 'D', FALSE)");
		await Exec("DELETE FROM `{ds}.items` WHERE active = FALSE");

		var rows = await Q("SELECT name FROM `{ds}.items` ORDER BY name");
		Assert.Equal(2, rows.Count);
		Assert.Equal("A", rows[0][0]?.ToString());
		Assert.Equal("C", rows[1][0]?.ToString());
	}

	// Go emulator requires column list in INSERT VALUES.
	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/dml-syntax#insert_statement
	//   "Column names are optional if the target table is not an ingestion-time partitioned table."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.EmulatorDivergence)]
	public async Task Table_JoinTwoTables()
	{
		await Exec("CREATE TABLE `{ds}.orders` (id INT64, customer_id INT64, amount FLOAT64)");
		await Exec("CREATE TABLE `{ds}.customers` (id INT64, name STRING)");
		await Exec("INSERT INTO `{ds}.customers` VALUES (1, 'Alice'), (2, 'Bob'), (3, 'Charlie')");
		await Exec("INSERT INTO `{ds}.orders` VALUES (100, 1, 50.0), (101, 1, 75.0), (102, 2, 30.0)");

		var rows = await Q(@"
			SELECT c.name, SUM(o.amount) AS total 
			FROM `{ds}.orders` o JOIN `{ds}.customers` c ON o.customer_id = c.id
			GROUP BY c.name ORDER BY total DESC");
		Assert.Equal(2, rows.Count);
		Assert.Equal("Alice", rows[0][0]?.ToString());
		Assert.Equal("125", rows[0][1]?.ToString());
		Assert.Equal("Bob", rows[1][0]?.ToString());
		Assert.Equal("30", rows[1][1]?.ToString());
	}

	// Go emulator requires column list in INSERT VALUES.
	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/dml-syntax#insert_statement
	//   "Column names are optional if the target table is not an ingestion-time partitioned table."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.EmulatorDivergence)]
	public async Task Table_LeftJoinWithNull()
	{
		await Exec("CREATE TABLE `{ds}.ordersx` (id INT64, cust_id INT64, amount FLOAT64)");
		await Exec("CREATE TABLE `{ds}.custs` (id INT64, name STRING)");
		await Exec("INSERT INTO `{ds}.custs` VALUES (1, 'Alice'), (2, 'Bob'), (3, 'Charlie')");
		await Exec("INSERT INTO `{ds}.ordersx` VALUES (100, 1, 50.0), (101, 2, 30.0)");

		var rows = await Q(@"
			SELECT c.name, IFNULL(SUM(o.amount), 0) AS total 
			FROM `{ds}.custs` c LEFT JOIN `{ds}.ordersx` o ON c.id = o.cust_id
			GROUP BY c.name ORDER BY c.name");
		Assert.Equal(3, rows.Count);
		Assert.Equal("Alice", rows[0][0]?.ToString());
		Assert.Equal("50", rows[0][1]?.ToString());
		Assert.Equal("Bob", rows[1][0]?.ToString());
		Assert.Equal("30", rows[1][1]?.ToString());
		Assert.Equal("Charlie", rows[2][0]?.ToString());
		Assert.Equal("0", rows[2][1]?.ToString());
	}

	// Go emulator requires column list in INSERT VALUES.
	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/dml-syntax#insert_statement
	//   "Column names are optional if the target table is not an ingestion-time partitioned table."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.EmulatorDivergence)]
	public async Task Table_WindowFunctionOverTable()
	{
		await Exec("CREATE TABLE `{ds}.sales` (id INT64, region STRING, amount FLOAT64)");
		await Exec("INSERT INTO `{ds}.sales` VALUES (1, 'East', 100), (2, 'East', 200), (3, 'West', 150), (4, 'West', 250), (5, 'West', 350)");

		var rows = await Q(@"
			SELECT region, amount, 
				RANK() OVER (PARTITION BY region ORDER BY amount DESC) AS rnk
			FROM `{ds}.sales` ORDER BY region, amount DESC");
		Assert.Equal(5, rows.Count);
		Assert.Equal("East", rows[0][0]?.ToString());
		Assert.Equal("200", rows[0][1]?.ToString());
		Assert.Equal("1", rows[0][2]?.ToString());
		Assert.Equal("East", rows[1][0]?.ToString());
		Assert.Equal("100", rows[1][1]?.ToString());
		Assert.Equal("2", rows[1][2]?.ToString());
	}

	// Go emulator requires column list in INSERT VALUES.
	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/dml-syntax#insert_statement
	//   "Column names are optional if the target table is not an ingestion-time partitioned table."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.EmulatorDivergence)]
	public async Task Table_SubqueryFilter()
	{
		await Exec("CREATE TABLE `{ds}.catalog` (id INT64, name STRING, category STRING, price FLOAT64)");
		await Exec("INSERT INTO `{ds}.catalog` VALUES (1, 'A', 'Electronics', 500), (2, 'B', 'Electronics', 1000), (3, 'C', 'Books', 20), (4, 'D', 'Books', 50)");

		var rows = await Q(@"
			SELECT name, price FROM `{ds}.catalog`
			WHERE price > (SELECT AVG(price) FROM `{ds}.catalog`)
			ORDER BY price DESC");
		// AVG = (500+1000+20+50)/4 = 392.5. Items > 392.5: B(1000), A(500)
		Assert.Equal(2, rows.Count);
		Assert.Equal("B", rows[0][0]?.ToString());
		Assert.Equal("A", rows[1][0]?.ToString());
	}

	// Go emulator requires column list in INSERT VALUES and INSERT ... SELECT without column list.
	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/dml-syntax#insert_statement
	//   "Column names are optional if the target table is not an ingestion-time partitioned table."
	[Fact]
	[Trait(TestTraits.Target, TestTraits.EmulatorDivergence)]
	public async Task Table_InsertSelectFrom()
	{
		await Exec("CREATE TABLE `{ds}.source` (id INT64, val STRING)");
		await Exec("CREATE TABLE `{ds}.target` (id INT64, val STRING)");
		await Exec("INSERT INTO `{ds}.source` VALUES (1, 'a'), (2, 'b'), (3, 'c')");
		await Exec("INSERT INTO `{ds}.target` SELECT * FROM `{ds}.source` WHERE id > 1");

		var rows = await Q("SELECT val FROM `{ds}.target` ORDER BY id");
		Assert.Equal(2, rows.Count);
		Assert.Equal("b", rows[0][0]?.ToString());
		Assert.Equal("c", rows[1][0]?.ToString());
	}

	// Go emulator requires column list in INSERT VALUES; also fails MERGE with "zetasqlite_merged_table already exists".
	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/dml-syntax#insert_statement
	//   "Column names are optional if the target table is not an ingestion-time partitioned table."
	// Ref: https://cloud.google.com/bigquery/docs/reference/standard-sql/dml-syntax#merge_statement
	//   MERGE is valid BigQuery DML; the Go emulator has an internal implementation bug.
	[Fact]
	[Trait(TestTraits.Target, TestTraits.EmulatorDivergence)]
	public async Task Table_MergeUpsert()
	{
		await Exec("CREATE TABLE `{ds}.inventory` (id INT64, name STRING, qty INT64)");
		await Exec("INSERT INTO `{ds}.inventory` VALUES (1, 'Widget', 10), (2, 'Gadget', 20)");
		await Exec(@"
			MERGE `{ds}.inventory` AS t
			USING (SELECT 2 AS id, 'Gadget' AS name, 25 AS qty UNION ALL SELECT 3, 'Doohickey', 15) AS s
			ON t.id = s.id
			WHEN MATCHED THEN UPDATE SET qty = s.qty
			WHEN NOT MATCHED THEN INSERT (id, name, qty) VALUES(s.id, s.name, s.qty)");

		var rows = await Q("SELECT name, qty FROM `{ds}.inventory` ORDER BY id");
		Assert.Equal(3, rows.Count);
		Assert.Equal("Widget", rows[0][0]?.ToString());
		Assert.Equal("10", rows[0][1]?.ToString());
		Assert.Equal("Gadget", rows[1][0]?.ToString());
		Assert.Equal("25", rows[1][1]?.ToString()); // updated
		Assert.Equal("Doohickey", rows[2][0]?.ToString());
		Assert.Equal("15", rows[2][1]?.ToString()); // inserted
	}
}
