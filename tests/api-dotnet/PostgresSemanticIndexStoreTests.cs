using EnterpriseDocumentAssistant.Api.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class PostgresSemanticIndexStoreTests
{
    [Fact]
    public async Task UpsertAsync_rejects_embeddings_with_the_wrong_dimension_before_connecting()
    {
        var store = CreateStore();
        var record = new SemanticIndexRecord(
            Guid.NewGuid(),
            "sample.txt",
            0,
            "Sample text",
            new[] { 1f, 0f });

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.UpsertAsync(new[] { record }, CancellationToken.None));

        Assert.Equal("Embedding", exception.ParamName);
    }

    [Fact]
    public async Task SearchAsync_rejects_non_finite_values_before_connecting()
    {
        var store = CreateStore();
        var embedding = Enumerable.Repeat(0f, SemanticIndexDefaults.EmbeddingDimensions).ToArray();
        embedding[0] = float.NaN;

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SearchAsync(new SemanticSearchRequest(embedding), CancellationToken.None));

        Assert.Equal("QueryEmbedding", exception.ParamName);
    }

    [Fact]
    public void AddConfiguredSemanticIndex_defaults_to_in_memory()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddConfiguredSemanticIndex(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        Assert.IsType<InMemorySemanticIndexStore>(serviceProvider.GetRequiredService<ISemanticIndexStore>());
    }

    [Fact]
    public void AddConfiguredSemanticIndex_selects_postgres()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(SemanticIndexDefaults.PostgresProvider);

        services.AddConfiguredSemanticIndex(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        Assert.IsType<PostgresSemanticIndexStore>(serviceProvider.GetRequiredService<ISemanticIndexStore>());
    }

    [Fact]
    public void AddConfiguredSemanticIndex_rejects_unknown_provider()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration("Unknown");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddConfiguredSemanticIndex(configuration));

        Assert.Contains("Unknown semantic index provider", exception.Message);
    }

    private static PostgresSemanticIndexStore CreateStore()
    {
        return new PostgresSemanticIndexStore(CreateConfiguration(SemanticIndexDefaults.PostgresProvider));
    }

    private static IConfiguration CreateConfiguration(string provider)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SemanticIndex:Provider"] = provider,
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=documents;Username=documents;Password=documents"
            })
            .Build();
    }
}
