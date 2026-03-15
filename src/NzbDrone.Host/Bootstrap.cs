using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using NLog;
using Npgsql;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Options;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Extensions;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using PostgresOptions = NzbDrone.Core.Datastore.PostgresOptions;

namespace NzbDrone.Host
{
    public static class Bootstrap
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(Bootstrap));

        public static readonly List<string> ASSEMBLIES = new List<string>
        {
            "Readarr.Host",
            "Readarr.Core",
            "Readarr.SignalR",
            "Readarr.Api.V1",
            "Readarr.Http"
        };

        public static void Start(string[] args, Action<IHostBuilder> trayCallback = null)
        {
            try
            {
                Logger.Info("Starting {0} - {1} - Version {2}",
                            BuildInfo.AppName,
                            Environment.ProcessPath,
                            Assembly.GetExecutingAssembly().GetName().Version);

                var startupContext = new StartupContext(args);

                LongPathSupport.Enable();
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                var appMode = GetApplicationMode(startupContext);
                var config = GetConfiguration(startupContext);

                RejectLegacyConfigurationKeys(config);

                switch (appMode)
                {
                    case ApplicationModes.Service:
                    {
                        Logger.Debug("Service selected");

                        CreateConsoleHostBuilder(args, startupContext).UseWindowsService().Build().Run();
                        break;
                    }

                    case ApplicationModes.Interactive:
                    {
                        Logger.Debug(trayCallback != null ? "Tray selected" : "Console selected");
                        var builder = CreateConsoleHostBuilder(args, startupContext);

                        if (trayCallback != null)
                        {
                            trayCallback(builder);
                        }

                        builder.Build().Run();
                        break;
                    }

                    // Utility mode
                    default:
                    {
                        new HostBuilder()
                            .UseServiceProviderFactory(new DryIocServiceProviderFactory(new Container(rules => rules.WithNzbDroneRules())))
                            .ConfigureContainer<IContainer>(c =>
                            {
                                c.AutoAddServices(Bootstrap.ASSEMBLIES)
                                    .AddNzbDroneLogger()
                                    .AddDatabase()
                                    .AddStartupContext(startupContext)
                                    .Resolve<UtilityModeRouter>()
                                    .Route(appMode);
                            })
                            .ConfigureServices(services =>
                            {
                                ConfigureOptions<PostgresOptions>(services, config, "Postgres");
                                ConfigureOptions<AppOptions>(services, config, "App");
                                ConfigureOptions<AuthOptions>(services, config, "Auth");
                                ConfigureOptions<ServerOptions>(services, config, "Server");
                                ConfigureOptions<LogOptions>(services, config, "Log");
                                ConfigureOptions<UpdateOptions>(services, config, "Update");
                            }).Build();

                        break;
                    }
                }
            }
            catch (InvalidConfigFileException ex)
            {
                throw new ReadarrStartupException(ex);
            }
            catch (AccessDeniedConfigFileException ex)
            {
                throw new ReadarrStartupException(ex);
            }
            catch (TerminateApplicationException ex)
            {
                Logger.Info(ex.Message);
                LogManager.Configuration = null;
            }

            // Make sure there are no lingering database connections
            GC.Collect();
            GC.WaitForPendingFinalizers();
            SQLiteConnection.ClearAllPools();
            NpgsqlConnection.ClearAllPools();
        }

        public static IHostBuilder CreateConsoleHostBuilder(string[] args, StartupContext context)
        {
            var config = GetConfiguration(context);
            RejectLegacyConfigurationKeys(config);

            var bindAddress = config.GetValue<string>($"Bibliophilarr:Server:{nameof(ServerOptions.BindAddress)}")
                              ?? config.GetValue(nameof(ConfigFileProvider.BindAddress), "*");
            var port = config.GetValue<int?>($"Bibliophilarr:Server:{nameof(ServerOptions.Port)}")
                       ?? config.GetValue(nameof(ConfigFileProvider.Port), 8787);
            var sslPort = config.GetValue<int?>($"Bibliophilarr:Server:{nameof(ServerOptions.SslPort)}")
                          ?? config.GetValue(nameof(ConfigFileProvider.SslPort), 6868);
            var enableSsl = config.GetValue<bool?>($"Bibliophilarr:Server:{nameof(ServerOptions.EnableSsl)}")
                            ?? config.GetValue(nameof(ConfigFileProvider.EnableSsl), false);
            var sslCertPath = config.GetValue<string>($"Bibliophilarr:Server:{nameof(ServerOptions.SslCertPath)}")
                              ?? config.GetValue<string>(nameof(ConfigFileProvider.SslCertPath));
            var sslCertPassword = config.GetValue<string>($"Bibliophilarr:Server:{nameof(ServerOptions.SslCertPassword)}")
                                  ?? config.GetValue<string>(nameof(ConfigFileProvider.SslCertPassword));

            var urls = new List<string> { BuildUrl("http", bindAddress, port) };

            if (enableSsl && sslCertPath.IsNotNullOrWhiteSpace())
            {
                urls.Add(BuildUrl("https", bindAddress, sslPort));
            }

            return new HostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseServiceProviderFactory(new DryIocServiceProviderFactory(new Container(rules => rules.WithNzbDroneRules())))
                .ConfigureContainer<IContainer>(c =>
                {
                    c.AutoAddServices(Bootstrap.ASSEMBLIES)
                        .AddNzbDroneLogger()
                        .AddDatabase()
                        .AddStartupContext(context)
                        .Resolve<IEventAggregator>().PublishEvent(new ApplicationStartingEvent());
                })
                .ConfigureServices(services =>
                {
                    ConfigureOptions<PostgresOptions>(services, config, "Postgres");
                    ConfigureOptions<AppOptions>(services, config, "App");
                    ConfigureOptions<AuthOptions>(services, config, "Auth");
                    ConfigureOptions<ServerOptions>(services, config, "Server");
                    ConfigureOptions<LogOptions>(services, config, "Log");
                    ConfigureOptions<UpdateOptions>(services, config, "Update");
                })
                .ConfigureWebHost(builder =>
                {
                    builder.UseConfiguration(config);
                    builder.UseUrls(urls.ToArray());
                    builder.UseKestrel(options =>
                    {
                        if (enableSsl && sslCertPath.IsNotNullOrWhiteSpace())
                        {
                            options.ConfigureHttpsDefaults(configureOptions =>
                            {
                                configureOptions.ServerCertificate = ValidateSslCertificate(sslCertPath, sslCertPassword);
                            });
                        }
                    });
                    builder.ConfigureKestrel(serverOptions =>
                    {
                        serverOptions.AllowSynchronousIO = false;
                        serverOptions.Limits.MaxRequestBodySize = null;
                    });
                    builder.UseStartup<Startup>();
                });
        }

        public static ApplicationModes GetApplicationMode(IStartupContext startupContext)
        {
            if (startupContext.Help)
            {
                return ApplicationModes.Help;
            }

            if (OsInfo.IsWindows && startupContext.RegisterUrl)
            {
                return ApplicationModes.RegisterUrl;
            }

            if (OsInfo.IsWindows && startupContext.InstallService)
            {
                return ApplicationModes.InstallService;
            }

            if (OsInfo.IsWindows && startupContext.UninstallService)
            {
                return ApplicationModes.UninstallService;
            }

            Logger.Debug("Getting windows service status");

            // IsWindowsService can throw sometimes, so wrap it
            var isWindowsService = false;
            try
            {
                isWindowsService = WindowsServiceHelpers.IsWindowsService();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to get service status");
            }

            if (OsInfo.IsWindows && isWindowsService)
            {
                return ApplicationModes.Service;
            }

            return ApplicationModes.Interactive;
        }

        private static IConfiguration GetConfiguration(StartupContext context)
        {
            var appFolder = new AppFolderInfo(context);
            var configPath = appFolder.GetConfigPath();

            try
            {
                return new ConfigurationBuilder()
                    .AddXmlFile(configPath, optional: true, reloadOnChange: false)
                    .AddInMemoryCollection(new List<KeyValuePair<string, string>> { new ("dataProtectionFolder", appFolder.GetDataProtectionPath()) })
                    .AddEnvironmentVariables()
                    .Build();
            }
            catch (InvalidDataException ex)
            {
                Logger.Error(ex, ex.Message);

                throw new InvalidConfigFileException($"{configPath} is corrupt or invalid. Please delete the config file and Readarr will recreate it.", ex);
            }
        }

        private static string BuildUrl(string scheme, string bindAddress, int port)
        {
            return $"{scheme}://{bindAddress}:{port}";
        }

        private static void ConfigureOptions<T>(IServiceCollection services, IConfiguration config, string sectionName)
            where T : class
        {
            services.Configure<T>(config.GetSection($"Bibliophilarr:{sectionName}"));
        }

        private static void RejectLegacyConfigurationKeys(IConfiguration config)
        {
            var legacyKeys = config.AsEnumerable()
                .Where(kv => kv.Key.StartsWith("Readarr:", StringComparison.OrdinalIgnoreCase))
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => kv.Key)
                .Distinct()
                .OrderBy(k => k)
                .ToList();

            if (legacyKeys.Count == 0)
            {
                return;
            }

            var keyList = string.Join(", ", legacyKeys);

            Logger.Error("Legacy configuration keys are no longer supported: {0}", keyList);

            throw new ReadarrStartupException($"Legacy configuration keys are no longer supported. Rename to Bibliophilarr:* keys: {keyList}");
        }

        private static X509Certificate2 ValidateSslCertificate(string cert, string password)
        {
            X509Certificate2 certificate;

            try
            {
                certificate = new X509Certificate2(cert, password, X509KeyStorageFlags.DefaultKeySet);
            }
            catch (CryptographicException ex)
            {
                if (ex.HResult == 0x2 || ex.HResult == 0x2006D080)
                {
                    throw new ReadarrStartupException(ex,
                        $"The SSL certificate file {cert} does not exist");
                }

                throw new ReadarrStartupException(ex);
            }

            return certificate;
        }
    }
}
