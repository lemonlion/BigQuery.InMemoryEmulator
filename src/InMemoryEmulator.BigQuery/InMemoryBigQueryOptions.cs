namespace InMemoryEmulator.BigQuery;

/// <summary>
/// Options for configuring the in-memory BigQuery instance.
/// Used by <see cref="ServiceCollectionExtensions.UseInMemoryBigQuery"/>.
/// </summary>
public class InMemoryBigQueryOptions
{
	/// <summary>The project ID for the emulated instance. Defaults to "test-project".</summary>
	public string ProjectId { get; set; } = "test-project";

	/// <summary>Datasets to pre-create.</summary>
	public List<(string DatasetId, Action<InMemoryDatasetBuilder>? Configure)> Datasets { get; } = [];

	/// <summary>Adds a dataset to be created on initialization.</summary>
	public InMemoryBigQueryOptions AddDataset(string datasetId, Action<InMemoryDatasetBuilder>? configure = null)
	{
		Datasets.Add((datasetId, configure));
		return this;
	}

	/// <summary>Callback invoked after the client is created.</summary>
	public Action<InMemoryBigQueryResult>? OnClientCreated { get; set; }

	/// <summary>
	/// Optional function that wraps the final <see cref="HttpMessageHandler"/>
	/// (the <see cref="FakeBigQueryHandler"/>) before it is passed to
	/// <see cref="FakeBigQueryHttpClientFactory"/>.
	/// <para>
	/// Use this to insert a <see cref="DelegatingHandler"/> into the pipeline.
	/// The input is the handler that serves in-memory responses; the return value
	/// replaces it as the outermost handler in the HTTP client.
	/// </para>
	/// <para>
	/// When <c>null</c> (the default), the handler is used as-is.
	/// </para>
	/// </summary>
	public Func<HttpMessageHandler, HttpMessageHandler>? HttpMessageHandlerWrapper { get; set; }

	/// <summary>
	/// Sets <see cref="HttpMessageHandlerWrapper"/> to the specified function.
	/// The function receives the <see cref="FakeBigQueryHandler"/> and must return
	/// the handler to use as the outermost handler in the HTTP client.
	/// </summary>
	public InMemoryBigQueryOptions WithHttpMessageHandlerWrapper(
		Func<HttpMessageHandler, HttpMessageHandler> wrapper)
	{
		HttpMessageHandlerWrapper = wrapper;
		return this;
	}
}
