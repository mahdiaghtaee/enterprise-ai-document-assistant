namespace EnterpriseDocumentAssistant.Api.Documents;

public static class SemanticIndexServiceCollectionExtensions
{
    public static IServiceCollection AddConfiguredSemanticIndex(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["SemanticIndex:Provider"] ?? SemanticIndexDefaults.InMemoryProvider;

        switch (provider.ToUpperInvariant())
        {
            case "INMEMORY":
                services.AddSingleton<ISemanticIndexStore, InMemorySemanticIndexStore>();
                break;
            case "POSTGRES":
                services.AddSingleton<ISemanticIndexStore, PostgresSemanticIndexStore>();
                break;
            default:
                throw new InvalidOperationException($"Unknown semantic index provider: {provider}");
        }

        return services;
    }
}
