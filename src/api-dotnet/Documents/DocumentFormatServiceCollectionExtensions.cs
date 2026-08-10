namespace EnterpriseDocumentAssistant.Api.Documents;

public static class DocumentFormatServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentFormatSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var processingSection = configuration.GetSection(DocumentProcessingOptions.SectionName);
        var processingOptions = processingSection.Get<DocumentProcessingOptions>() ?? new DocumentProcessingOptions();
        processingOptions.Validate();
        services.Configure<DocumentProcessingOptions>(processingSection);

        var scanningSection = configuration.GetSection(FileThreatScanningOptions.SectionName);
        var scanningOptions = scanningSection.Get<FileThreatScanningOptions>() ?? new FileThreatScanningOptions();
        scanningOptions.Validate();
        services.Configure<FileThreatScanningOptions>(scanningSection);

        services.AddSingleton<IDocumentUploadInspector, DocumentUploadInspector>();
        services.AddSingleton<IDocumentTextExtractor, SafeDocumentTextExtractor>();

        if (string.Equals(
            scanningOptions.Provider,
            FileThreatScanningProviders.ClamAv,
            StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IFileThreatScanner, ClamAvFileThreatScanner>();
        }
        else
        {
            services.AddSingleton<IFileThreatScanner, DisabledFileThreatScanner>();
        }

        return services;
    }
}
