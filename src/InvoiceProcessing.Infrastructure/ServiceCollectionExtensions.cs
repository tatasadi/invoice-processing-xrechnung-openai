using InvoiceProcessing.Core.Processing;
using InvoiceProcessing.Core.Reading;
using InvoiceProcessing.Core.Validation;
using InvoiceProcessing.Infrastructure.Import;
using InvoiceProcessing.Infrastructure.Persistence;
using InvoiceProcessing.Infrastructure.Readers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InvoiceProcessing.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInvoiceProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.Configure<OpenAiOptions>(configuration.GetSection("OpenAI"));
        services.Configure<ValidationOptions>(configuration.GetSection("Validation"));
        services.Configure<DatabaseOptions>(configuration.GetSection("Database"));

        var connectionString = configuration.GetConnectionString("InvoiceDb")
                               ?? throw new InvalidOperationException("ConnectionStrings:InvoiceDb ist nicht konfiguriert.");
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<PostgresInvoiceRepository>();
        services.AddSingleton<IInvoiceRepository>(sp => sp.GetRequiredService<PostgresInvoiceRepository>());
        services.AddSingleton<IInvoiceQueries>(sp => sp.GetRequiredService<PostgresInvoiceRepository>());
        services.AddSingleton<DatabaseInitializer>();

        // Order matters: structured e-invoices are read directly; OpenAI is only the fallback for plain PDFs.
        services.AddSingleton<IInvoiceReader, XmlInvoiceReader>();
        services.AddSingleton<IInvoiceReader, ZugferdPdfReader>();
        services.AddSingleton<IInvoiceReader, OpenAiPdfReader>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(sp => new InvoiceValidator(
            sp.GetRequiredService<IOptions<ValidationOptions>>().Value, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IDocumentArchive, FileDocumentArchive>();
        services.AddSingleton<InvoiceProcessor>();
        services.AddSingleton<InvoiceImportService>();

        return services;
    }
}
