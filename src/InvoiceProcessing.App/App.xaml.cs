using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using InvoiceProcessing.App.ViewModels;
using InvoiceProcessing.Infrastructure;
using InvoiceProcessing.Infrastructure.Import;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace InvoiceProcessing.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // German number and date formats in all bindings (1.234,56 / 05.10.2026).
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("de-DE")));
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            _host = BuildHost(e.Args);
            await _host.StartAsync();

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
            await window.ViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Die Anwendung konnte nicht gestartet werden:\n\n{ex.Message}", "Rechnungsverarbeitung",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private static IHost BuildHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // OpenAI key for development: dotnet user-secrets set "OpenAI:ApiKey" "<key>" --id invoice-processing
        builder.Configuration.AddUserSecrets("invoice-processing");

        var logDirectory = Path.GetFullPath(builder.Configuration["Logging:Directory"] ?? "logs", AppContext.BaseDirectory);
        builder.Services.AddSerilog(logging => logging
            .ReadFrom.Configuration(builder.Configuration)
            .WriteTo.File(
                Path.Combine(logDirectory, "rechnungsverarbeitung-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 90,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                encoding: Encoding.UTF8));

        builder.Services.AddInvoiceProcessing(builder.Configuration);
        builder.Services.PostConfigure<StorageOptions>(options => options.ResolveRelativeTo(AppContext.BaseDirectory));
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Services.GetService<ILogger<App>>()?.LogError(e.Exception, "Unbehandelter Fehler");
        MessageBox.Show($"Unerwarteter Fehler:\n\n{e.Exception.Message}", "Rechnungsverarbeitung", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
