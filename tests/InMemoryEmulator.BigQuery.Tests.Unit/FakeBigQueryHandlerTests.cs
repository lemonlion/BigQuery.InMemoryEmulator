using System.Net;
using InMemoryEmulator.BigQuery;
using Xunit;

namespace InMemoryEmulator.BigQuery.Tests.Unit;

public class FakeBigQueryHandlerTests
{
	private readonly InMemoryDataStore _store;
	private readonly FakeBigQueryHandler _handler;
	private readonly HttpClient _httpClient;

	public FakeBigQueryHandlerTests()
	{
		_store = new InMemoryDataStore("test-project");
		_handler = new FakeBigQueryHandler(_store);
		_httpClient = new HttpClient(_handler)
		{
			BaseAddress = new Uri("https://bigquery.googleapis.com")
		};
	}

	[Fact]
	public async Task UnknownRoute_Returns404()
	{
		// Act
		var response = await _httpClient.GetAsync("/bigquery/v2/projects/test-project/unknown");

		// Assert
		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task RequestLog_RecordsAllRequests()
	{
		// Act
		await _httpClient.GetAsync("/bigquery/v2/projects/test-project/datasets");
		await _httpClient.GetAsync("/bigquery/v2/projects/test-project/datasets/my_ds");

		// Assert
		Assert.Equal(2, _handler.RequestLog.Count);
	}

	[Fact]
	public async Task FaultInjector_ReturnsInjectedResponse()
	{
		// Arrange
		_handler.FaultInjector = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

		// Act
		var response = await _httpClient.GetAsync("/bigquery/v2/projects/test-project/datasets");

		// Assert
		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
	}
}
