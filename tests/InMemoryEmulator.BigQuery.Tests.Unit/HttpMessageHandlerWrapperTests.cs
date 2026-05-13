using Google.Cloud.BigQuery.V2;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InMemoryEmulator.BigQuery.Tests.Unit;

/// <summary>
/// Tests for WithHttpMessageHandlerWrapper on both InMemoryBigQueryOptions (DI) and InMemoryBigQueryBuilder.
/// </summary>
public class HttpMessageHandlerWrapperTests
{
	[Fact]
	public async Task Builder_WithHttpMessageHandlerWrapper_InterceptsRequests()
	{
		var interceptedRequests = new List<HttpRequestMessage>();

		using var result = InMemoryBigQuery.Builder()
			.WithProjectId("test-project")
			.AddDataset("ds1")
			.WithHttpMessageHandlerWrapper(fakeHandler =>
				new TrackingDelegatingHandler(interceptedRequests) { InnerHandler = fakeHandler })
			.Build();

		// Execute a query that goes through the HTTP pipeline
		await result.Client.GetDatasetAsync("ds1");

		Assert.NotEmpty(interceptedRequests);
	}

	[Fact]
	public async Task Builder_WithoutWrapper_StillWorks()
	{
		using var result = InMemoryBigQuery.Builder()
			.WithProjectId("test-project")
			.AddDataset("ds1")
			.Build();

		var ds = await result.Client.GetDatasetAsync("ds1");
		Assert.NotNull(ds);
	}

	[Fact]
	public async Task Options_WithHttpMessageHandlerWrapper_InterceptsRequests()
	{
		var interceptedRequests = new List<HttpRequestMessage>();
		InMemoryBigQueryResult? capturedResult = null;

		var services = new ServiceCollection();
		services.UseInMemoryBigQuery(options =>
		{
			options.ProjectId = "test-project";
			options.AddDataset("ds1");
			options.WithHttpMessageHandlerWrapper(fakeHandler =>
				new TrackingDelegatingHandler(interceptedRequests) { InnerHandler = fakeHandler });
			options.OnClientCreated = r => capturedResult = r;
		});

		var provider = services.BuildServiceProvider();
		var client = provider.GetRequiredService<BigQueryClient>();

		await client.GetDatasetAsync("ds1");

		Assert.NotEmpty(interceptedRequests);
	}

	[Fact]
	public void Options_WithHttpMessageHandlerWrapper_ReturnsSelf()
	{
		var options = new InMemoryBigQueryOptions();
		var returned = options.WithHttpMessageHandlerWrapper(h => h);
		Assert.Same(options, returned);
	}

	[Fact]
	public void Builder_WithHttpMessageHandlerWrapper_ReturnsSelf()
	{
		var builder = InMemoryBigQuery.Builder();
		var returned = builder.WithHttpMessageHandlerWrapper(h => h);
		Assert.Same(builder, returned);
	}

	[Fact]
	public async Task Builder_WrapperReceivesFakeBigQueryHandler()
	{
		HttpMessageHandler? receivedHandler = null;

		using var result = InMemoryBigQuery.Builder()
			.WithProjectId("test-project")
			.AddDataset("ds1")
			.WithHttpMessageHandlerWrapper(fakeHandler =>
			{
				receivedHandler = fakeHandler;
				return fakeHandler; // Pass through without wrapping
			})
			.Build();

		Assert.NotNull(receivedHandler);
		Assert.IsType<FakeBigQueryHandler>(receivedHandler);
	}

	[Fact]
	public async Task Builder_WrapperCanChainMultipleHandlers()
	{
		var outerCalls = new List<string>();
		var innerCalls = new List<string>();

		using var result = InMemoryBigQuery.Builder()
			.WithProjectId("test-project")
			.AddDataset("ds1")
			.WithHttpMessageHandlerWrapper(fakeHandler =>
			{
				var inner = new TaggingDelegatingHandler("inner", innerCalls) { InnerHandler = fakeHandler };
				var outer = new TaggingDelegatingHandler("outer", outerCalls) { InnerHandler = inner };
				return outer;
			})
			.Build();

		await result.Client.GetDatasetAsync("ds1");

		Assert.NotEmpty(outerCalls);
		Assert.NotEmpty(innerCalls);
		// Outer should be called first (it's the outermost handler)
		Assert.Equal("outer", outerCalls[0]);
		Assert.Equal("inner", innerCalls[0]);
	}

	/// <summary>A simple DelegatingHandler that records intercepted requests.</summary>
	private class TrackingDelegatingHandler : DelegatingHandler
	{
		private readonly List<HttpRequestMessage> _requests;

		public TrackingDelegatingHandler(List<HttpRequestMessage> requests) => _requests = requests;

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken)
		{
			_requests.Add(request);
			return base.SendAsync(request, cancellationToken);
		}
	}

	/// <summary>A DelegatingHandler that records a tag when invoked.</summary>
	private class TaggingDelegatingHandler : DelegatingHandler
	{
		private readonly string _tag;
		private readonly List<string> _calls;

		public TaggingDelegatingHandler(string tag, List<string> calls)
		{
			_tag = tag;
			_calls = calls;
		}

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken)
		{
			_calls.Add(_tag);
			return base.SendAsync(request, cancellationToken);
		}
	}
}
