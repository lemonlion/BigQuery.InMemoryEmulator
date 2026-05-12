using Xunit;

namespace InMemoryEmulator.BigQuery.Tests.Integration;

/// <summary>
/// xUnit collection definition that shares a single <see cref="BigQuerySession"/>
/// across all integration test classes in this assembly.
/// </summary>
[CollectionDefinition(IntegrationCollection.Name)]
public class IntegrationCollectionDefinition : ICollectionFixture<BigQuerySession>;
