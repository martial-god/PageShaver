﻿using Autofac;
using Benny_Scraper.BusinessLogic;
using Benny_Scraper.BusinessLogic.Config;
using Benny_Scraper.BusinessLogic.Factory;
using Benny_Scraper.BusinessLogic.Factory.Interfaces;
using Benny_Scraper.BusinessLogic.FileGenerators;
using Benny_Scraper.BusinessLogic.FileGenerators.Interfaces;
using Benny_Scraper.BusinessLogic.Helper;
using Benny_Scraper.BusinessLogic.Interfaces;
using Benny_Scraper.BusinessLogic.Services;
using Benny_Scraper.BusinessLogic.Services.Interface;
using Benny_Scraper.DataAccess.Data;
using Benny_Scraper.DataAccess.DbInitializer;
using Benny_Scraper.DataAccess.Repository;
using Benny_Scraper.DataAccess.Repository.IRepository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Targets;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Benny_Scraper
{
    internal class Program
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private static IContainer Container { get; set; }
        public static IConfiguration Configuration { get; set; }

        // Added Task to Main in order to avoid "Program does not contain a static 'Main method suitable for an entry point"
        static async Task Main(string[] args)
        {
            SetupLogger();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            SQLitePCL.Batteries.Init();
            Configuration = BuildConfiguration();

            var builder = new ContainerBuilder();
            ConfigureServices(builder);
            Container = builder.Build();

            if (args.Length > 0)
            {
                await RunAsync(args);
            }
            else
            {
                Logger.Info("Application Started");
                await RunAsync();
            }
        }

        private static async Task RunAsync()
        {
            using (var scope = Container.BeginLifetimeScope())
            {
                var logger = NLog.LogManager.GetCurrentClassLogger();
                Logger.Info("Initializing Database");
                DbInitializer dbInitializer = scope.Resolve<DbInitializer>();
                dbInitializer.Initialize();
                Logger.Info("Database Initialized");

                string instructions = GetInstructions();

                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine(instructions);
                Console.ResetColor();

                INovelProcessor novelProcessor = scope.Resolve<INovelProcessor>();

                bool isApplicationRunning = true;
                while (isApplicationRunning)
                {
                    // Uri help https://www.dotnetperls.com/uri#:~:text=URI%20stands%20for%20Universal%20Resource,strings%20starting%20with%20%22http.%22
                    Console.WriteLine("\nEnter the site url (or 'exit' to quit): ");
                    string siteUrl = Console.ReadLine();
                    string[] input = siteUrl.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    if (string.IsNullOrWhiteSpace(siteUrl))
                    {
                        Console.WriteLine("Invalid input. Please enter a valid URL.");
                        continue;
                    }

                    if (siteUrl.ToLower() == "exit")
                    {
                        isApplicationRunning = false;
                        continue;
                    }

                    if (!Uri.TryCreate(siteUrl, UriKind.Absolute, out Uri novelTableOfContentUri))
                    {
                        Console.WriteLine("Invalid URL. Please enter a valid URL.");
                        continue;
                    }

                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();
                    try
                    {
                        await novelProcessor.ProcessNovelAsync(novelTableOfContentUri);

                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Exception when trying to process novel. {ex}");
                    }
                    stopwatch.Stop();
                    TimeSpan elapsedTime = stopwatch.Elapsed;
                    Logger.Info($"Elapsed time: {elapsedTime}");
                }
            }
        }

        private static string GetInstructions()
        {
            List<string> supportedSites = new List<string>
                {
                    "\nLight Novel World (https://www.lightnovelworld.com/)",
                    "Novel Full (https://www.novelfull.com/)",
                    "Mangakakalot (https://mangakakalot.to/)",
                    "Mangareader (https://www.mangareader.to/)",
                    "Mangakatana (https://mangakatana.com/)",
                };

            string instructions = "\n" + $@"Welcome to our novel scraper application!
                Currently, we support the following websites:
                {string.Join("\n", supportedSites)}

                To use our application, please follow these steps:
                1. Visit a supported website.
                2. Choose a novel and navigate to its table of contents page.
                3. Copy the URL of this page.
                4. Paste the URL into our application when prompted.

                Please ensure the URL is from the table of contents page of a novel.
                Our application will then download the novel and convert it into an EPUB file.
                Thank you for using our application! Enjoy your reading.";

            return instructions;
        }


        private static async Task RunAsync(string[] args)
        {
            using (var scope = Container.BeginLifetimeScope())
            {
                var logger = NLog.LogManager.GetCurrentClassLogger();
                INovelService novelService = scope.Resolve<INovelService>();
                IConfigurationRepository configurationRepository = scope.Resolve<IConfigurationRepository>();

                switch (args[0])
                {
                    case "list":
                        {
                            var novels = await novelService.GetAllAsync();

                            // Calculate the maximum lengths of each column
                            int maxIdLength = novels.Max(novel => novel.Id.ToString().Length);
                            int maxTitleLength = novels.Max(novel => novel.Title.Length);

                            Console.ForegroundColor = ConsoleColor.Blue;
                            Console.WriteLine($"Id:".PadRight(maxIdLength) + "\tTitle:".PadRight(maxTitleLength));
                            Console.ResetColor();
                            foreach (var novel in novels)
                            {
                                Console.WriteLine($"{novel.Id.ToString().PadRight(maxIdLength)}\t{novel.Title.PadRight(maxTitleLength)}");
                            }
                            break;
                        }
                    case "clear_database":
                        {
                            logger.Info("Clearing all novels and chapter from database");
                            await novelService.RemoveAllAsync();
                        }
                        break;
                    case "delete_novel_by_id": // only way to resovle the same variable, in the case novelService is to surround the case statement in curly braces
                        {
                            logger.Info($"Deleting novel with id {args[1]}");
                            Guid.TryParse(args[1], out Guid novelId);
                            await novelService.RemoveByIdAsync(novelId);
                            logger.Info($"Novel with id: {args[1]} deleted.");
                        }
                        break;
                    case "recreate": // at this momement should only work for webnovels not mangas. Need to create something to distinguish between the two in the database
                        {
                            try
                            {
                                var configuration = await configurationRepository.GetByIdAsync(1);
                                IEpubGenerator epubGenerator = scope.Resolve<IEpubGenerator>();
                                Uri.TryCreate(args[1], UriKind.Absolute, out Uri tableOfContentUri);
                                bool isNovelInDatabase = await novelService.IsNovelInDatabaseAsync(tableOfContentUri.ToString());
                                if (isNovelInDatabase)
                                {
                                    var novel = await novelService.GetByUrlAsync(tableOfContentUri);
                                    Logger.Info($"Recreating novel {novel.Title}. Id: {novel.Id}, Total Chapters: {novel.Chapters.Count()}");
                                    var chapters = novel.Chapters.Where(c => c.Number != 0).OrderBy(c => c.Number).ToList();
                                    string safeTitle = CommonHelper.SanitizeFileName(novel.Title, true);
                                    var documentsFolder = CommonHelper.GetOutputDirectoryForTitle(safeTitle, configuration.DetermineSaveLocation());
                                    Directory.CreateDirectory(documentsFolder);
                                    string epubFile = Path.Combine(documentsFolder, $"{safeTitle}.epub");
                                    epubGenerator.CreateEpub(novel, chapters, epubFile, null);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"Exception when trying to recreate novel. {ex}");
                            }
                        }
                        break;
                    case "concurrency_limit":
                        {
                            var configuration = await configurationRepository.GetByIdAsync(1);
                            Console.WriteLine($"Concurrency limit: {configuration.ConcurrencyLimit}");
                        }
                        break;
                    case "set_concurrency_limit":
                        {
                            var configuration = await configurationRepository.GetByIdAsync(1);
                            try
                            {
                                configuration.ConcurrencyLimit = int.Parse(args[1]);
                                configurationRepository.Update(configuration); // need to add valid way to update this. May need to create a service for this.
                                Console.WriteLine($"Concurrency limit updated: {configuration.ConcurrencyLimit}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Invalid Concurrency limit. {ex}");
                            }
                        }
                        break;
                    case "set_save_location":
                        {
                            var configuration = await configurationRepository.GetByIdAsync(1);
                            try
                            {
                                configuration.SaveLocation = args[1];
                                configurationRepository.Update(configuration); // need to add valid way to update this. May need to create a service for this.
                                Console.WriteLine($"Save location updated: {configuration.SaveLocation}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Invalid save location. {ex}");
                            }
                        }
                        break;
                    case "set_manga_save_location":
                        {
                            var configuration = await configurationRepository.GetByIdAsync(1);
                            try
                            {
                                configuration.MangaSaveLocation = args[1];
                                configurationRepository.Update(configuration); // need to add valid way to update this. May need to create a service for this.
                                Console.WriteLine($"Manga save location updated: {configuration.MangaSaveLocation}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Invalid manga save location. {ex}");
                            }
                        }
                        break;
                    case "set_novel_save_location":
                        {
                            var configuration = await configurationRepository.GetByIdAsync(1);
                            try
                            {
                                configuration.NovelSaveLocation = args[1];
                                configurationRepository.Update(configuration); // need to add valid way to update this. May need to create a service for this.
                                Console.WriteLine($"Novel save location updated: {configuration.NovelSaveLocation}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Invalid novel save location. {ex}");
                            }
                        }
                        break;
                    case "save_location":
                        {
                            var configuration = await configurationRepository.GetByIdAsync(1);
                            Console.WriteLine($"Save location: {configuration.SaveLocation}");
                        }
                        break;
                    case "help":
                        {
                            Console.WriteLine("list - list all novels in database");
                            Console.WriteLine("clear_database - clear all novels and chapters from database");
                            Console.WriteLine("delete_novel_by_id [ID]        Delete a novel by its ID");
                            Console.WriteLine("recreate [URL]                 Recreate a novel EPUB by its URL, currently not implemented to handle Mangas");
                            Console.WriteLine("concurrency_limit              Get the concurrency limit for the application. i.e. How many simultaneous request are made");
                            Console.WriteLine("set_concurrency_limit [LIMIT]     Set the concurrency limit for the application. i.e. How many simultaneous request are made");
                            Console.WriteLine("set_save_location [PATH]          Set the save location for the application. i.e. Where the EPUBs are saved");
                            Console.WriteLine("save_location                    Get the save location for the application. i.e. Where the EPUBs are saved");
                            Console.WriteLine("set_manga_save_location [PATH]    Set the manga save location for the application. i.e. Where the EPUBs are saved");
                            Console.WriteLine("set_novel_save_location [PATH]    Set the novel save location for the application. i.e. Where the EPUBs are saved");
                        }
                        break;
                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"The command '{args[0]}' is not a valid command.");
                        Console.ResetColor();
                        break;
                }
            }
        }

        private static string GetDocumentsFolder(string title)
        {

            string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.Equals(Environment.UserName, "emiya", StringComparison.OrdinalIgnoreCase))
                documentsFolder = DriveInfo.GetDrives().FirstOrDefault(drive => drive.Name == @"H:\")?.Name ?? documentsFolder;
            string fileRegex = @"[^a-zA-Z0-9-\s]";
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            var novelFileSafeTitle = textInfo.ToTitleCase(Regex.Replace(title, fileRegex, string.Empty).ToLower().ToLowerInvariant());
            return Path.Combine(documentsFolder, "BennyScrapedNovels", novelFileSafeTitle);
        }

        private static void SetupLogger()
        {
            var config = new NLog.Config.LoggingConfiguration();

            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string directoryPath = Path.Combine(appDataPath, "BennyScraper", "logs");

            string logPath = Path.Combine(directoryPath, $"log-book {DateTime.Now.ToString("MM-dd-yyyy")}.log");
            var logfile = new NLog.Targets.FileTarget("logfile") { FileName = logPath };

            var logconsole = new NLog.Targets.ColoredConsoleTarget("logconsole")
            {
                Layout = @"${date:format=HH\:mm\:ss} ${level} ${message} ${exception}"
            };

            logconsole.RowHighlightingRules.Add(new NLog.Targets.ConsoleRowHighlightingRule(
                NLog.Conditions.ConditionParser.ParseExpression("level == LogLevel.Info"),
                ConsoleOutputColor.Green, ConsoleOutputColor.Black));
            logconsole.RowHighlightingRules.Add(new NLog.Targets.ConsoleRowHighlightingRule(
                NLog.Conditions.ConditionParser.ParseExpression("level == LogLevel.Warn"),
                ConsoleOutputColor.DarkYellow, ConsoleOutputColor.Black));
            logconsole.RowHighlightingRules.Add(new NLog.Targets.ConsoleRowHighlightingRule(
                NLog.Conditions.ConditionParser.ParseExpression("level == LogLevel.Error"),
                ConsoleOutputColor.Red, ConsoleOutputColor.Black));
            logconsole.RowHighlightingRules.Add(new NLog.Targets.ConsoleRowHighlightingRule(
                NLog.Conditions.ConditionParser.ParseExpression("level == LogLevel.Fatal"),
                ConsoleOutputColor.White, ConsoleOutputColor.Red));

            config.AddRule(LogLevel.Info, LogLevel.Fatal, logconsole);
            config.AddRule(LogLevel.Info, LogLevel.Fatal, logfile);

            NLog.LogManager.Configuration = config;
        }


        /// <summary>
        /// Loads the configuration for the application from appsettings.json. The configuration is used to configure the application's services, and will be 
        /// handed to the Autofac container builder in Startup.cs, which will register the appsettings as classes I have defined.
        /// </summary>
        /// <returns>The loaded configuration object.</returns>
        private static IConfigurationRoot BuildConfiguration()
        {
            var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(appSettingsPath, optional: false, reloadOnChange: true)
                .Build();

            return configuration;
        }

        /// <summary>
        /// Register all services and repositories, including the DbContext, appsettings.json as NovelScraperSettings and EpubTemplates based on the key in the file
        /// </summary>
        /// <param name="builder"></param>
        public static void ConfigureServices(ContainerBuilder builder)
        {
            // Register IConfiguration
            builder.RegisterInstance(Configuration).As<IConfiguration>();

            builder.Register(c => new Database(new DbContextOptionsBuilder<Database>()
                .UseSqlite(GetConnectionString(), options => options.MigrationsAssembly("Benny-Scraper.DataAccess")).Options)).InstancePerLifetimeScope();


            builder.RegisterType<DbInitializer>().As<DbInitializer>();
            builder.RegisterType<UnitOfWork>().As<IUnitOfWork>();
            builder.RegisterType<NovelProcessor>().As<INovelProcessor>();
            builder.RegisterType<ChapterRepository>().As<IChapterRepository>();
            builder.RegisterType<NovelService>().As<INovelService>().InstancePerLifetimeScope();
            builder.RegisterType<ChapterService>().As<IChapterService>().InstancePerLifetimeScope();
            builder.RegisterType<NovelRepository>().As<INovelRepository>();
            builder.RegisterType<ConfigurationRepository>().As<IConfigurationRepository>();
            builder.RegisterType<EpubGenerator>().As<IEpubGenerator>().InstancePerDependency();
            builder.RegisterType<PdfGenerator>().As<PdfGenerator>().InstancePerDependency();

            builder.Register(c =>
            {
                var config = c.Resolve<IConfiguration>();
                var settings = new NovelScraperSettings();
                config.GetSection("NovelScraperSettings").Bind(settings);
                return settings;
            }).SingleInstance();
            //needed to register NovelScraperSettings implicitly, Autofac does not resolve 'IOptions<T>' by defualt. Optoins.Create avoids ArgumentException
            builder.Register(c => Options.Create(c.Resolve<NovelScraperSettings>())).As<IOptions<NovelScraperSettings>>().SingleInstance();

            // register EpuTemplates.cs as singleton from the appsettings.json file
            builder.Register(c =>
            {
                var config = c.Resolve<IConfiguration>();
                var settings = new EpubTemplates();
                config.GetSection("EpubTemplates").Bind(settings);
                return settings;
            }).SingleInstance();
            builder.Register(c => Options.Create(c.Resolve<EpubTemplates>())).As<IOptions<EpubTemplates>>().SingleInstance();

            // register the factory
            builder.Register<Func<string, INovelScraper>>(c =>
            {
                var context = c.Resolve<IComponentContext>();
                return key => context.ResolveNamed<INovelScraper>(key);
            });

            builder.RegisterType<NovelScraperFactory>().As<INovelScraperFactory>().InstancePerDependency();
            builder.RegisterType<SeleniumNovelScraper>().Named<INovelScraper>("Selenium").InstancePerDependency(); // InstancePerDependency() similar to transient
            builder.RegisterType<HttpNovelScraper>().Named<INovelScraper>("Http").InstancePerDependency();
        }

        /// <summary>
        /// Get the connection string for the database file, if the file does not exist, create it
        /// </summary>
        /// <returns>connection string</returns>
        private static string GetConnectionString()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string directoryPath = Path.Combine(appDataPath, "BennyScraper", "Database");
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            string dbPath = Path.Combine(directoryPath, "BennyTestDb.db");
            var connectionString = $"Data Source={dbPath};";
            return connectionString;
        }
    }
}