namespace EnterpriseDocumentAssistant.Api;

public static class ApplicationHostingModes
{
    public const string Combined = "Combined";
    public const string Api = "Api";
    public const string Worker = "Worker";
}

public sealed record ApplicationHostingMode(
    string Name,
    bool RunsApi,
    bool RunsWorker)
{
    public static ApplicationHostingMode FromConfiguration(IConfiguration configuration)
    {
        var configured = configuration["ApplicationMode"]?.Trim();
        if (string.IsNullOrWhiteSpace(configured) ||
            string.Equals(configured, ApplicationHostingModes.Combined, StringComparison.OrdinalIgnoreCase))
        {
            return new ApplicationHostingMode(ApplicationHostingModes.Combined, true, true);
        }

        if (string.Equals(configured, ApplicationHostingModes.Api, StringComparison.OrdinalIgnoreCase))
        {
            return new ApplicationHostingMode(ApplicationHostingModes.Api, true, false);
        }

        if (string.Equals(configured, ApplicationHostingModes.Worker, StringComparison.OrdinalIgnoreCase))
        {
            return new ApplicationHostingMode(ApplicationHostingModes.Worker, false, true);
        }

        throw new InvalidOperationException(
            "ApplicationMode must be Combined, Api, or Worker.");
    }
}
