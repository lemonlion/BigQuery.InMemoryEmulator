using System.Net.Http;
using System.Net.Sockets;
using Google.Apis.Bigquery.v2.Data;
using Google.Cloud.BigQuery.V2;

namespace InMemoryEmulator.BigQuery.Tests.Infrastructure;

/// <summary>
/// Fixture that creates datasets and tables against the goccy/bigquery-emulator Docker container.
/// Includes automatic recovery when the emulator crashes and is restarted.
/// </summary>
public sealed class EmulatorTestFixture : ITestDatasetFixture
{
	private readonly BigQuerySession _session;
	private readonly List<string> _createdDatasets = [];

	public EmulatorTestFixture(BigQuerySession session) => _session = session;

	public TestTarget Target => TestTarget.BigQueryEmulator;
	public bool IsRemote => true;

	public async Task<BigQueryClient> GetClientAsync()
	{
		// Always return the session's current client — it may have been rebuilt after a crash restart
		var client = _session.RemoteClient;
		if (client is null)
		{
			await _session.EnsureEmulatorHealthyAsync();
			client = _session.RemoteClient
				?? throw new InvalidOperationException("Emulator client not initialized");
		}
		return client;
	}

	public async Task<BigQueryDataset> CreateDatasetAsync(
		string datasetId,
		CreateDatasetOptions? options = null)
	{
		return await WithEmulatorRecoveryAsync(async () =>
		{
			var client = await GetClientAsync();
			var dataset = await client.CreateDatasetAsync(datasetId, options: options);
			_createdDatasets.Add(datasetId);
			return dataset;
		});
	}

	public async Task<BigQueryTable> CreateTableAsync(
		string datasetId,
		string tableId,
		TableSchema schema,
		CreateTableOptions? options = null)
	{
		return await WithEmulatorRecoveryAsync(async () =>
		{
			var client = await GetClientAsync();
			return await client.CreateTableAsync(datasetId, tableId, schema, options);
		});
	}

	/// <summary>
	/// Executes an operation against the emulator, retrying once after crash recovery.
	/// </summary>
	private async Task<T> WithEmulatorRecoveryAsync<T>(Func<Task<T>> operation)
	{
		try
		{
			return await operation();
		}
		catch (Exception ex) when (IsEmulatorCrashException(ex))
		{
			// Emulator crashed — wait for recovery and retry
			await _session.EnsureEmulatorHealthyAsync();
			return await operation();
		}
	}

	private static bool IsEmulatorCrashException(Exception ex)
	{
		return ex is HttpRequestException
			|| ex.InnerException is HttpRequestException
			|| ex.InnerException is SocketException
			|| ex.InnerException is IOException;
	}

	public async ValueTask DisposeAsync()
	{
		if (_session.RemoteClient is { } client)
		{
			foreach (var datasetId in _createdDatasets)
			{
				try
				{
					await client.DeleteDatasetAsync(datasetId, new DeleteDatasetOptions { DeleteContents = true });
				}
				catch
				{
					// Best-effort cleanup
				}
			}
		}
		_createdDatasets.Clear();
	}
}
