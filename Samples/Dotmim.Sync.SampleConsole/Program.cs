﻿using Dotmim.Sync;
using Dotmim.Sync.Builders;
using Dotmim.Sync.Data;
using Dotmim.Sync.Data.Surrogate;
using Dotmim.Sync.Enumerations;
using Dotmim.Sync.Proxy;
using Dotmim.Sync.SampleConsole;
using Dotmim.Sync.SQLite;
using Dotmim.Sync.SqlServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using System;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static void Main(string[] args)
    {

        

        //TestSync().Wait();

        //TestSyncThroughKestrellAsync().Wait();

        //TestAllAvailablesColumns().Wait();

        TestSyncThroughWebApi().Wait();

        Console.ReadLine();

    }

    private static async Task TestSQLiteSyncScopeBuilder()
    {
        var path = Path.Combine(Environment.CurrentDirectory, "db.sqlite");

        var builder = new System.Data.SQLite.SQLiteConnectionStringBuilder
        {
            DataSource = path
        };

        var sqliteConnectionString = builder.ConnectionString;

        SQLiteSyncProvider sqliteSyncProvider = new SQLiteSyncProvider(sqliteConnectionString);

        var tbl = new DmTable("ServiceTickets");
        var id = new DmColumn<Guid>("ServiceTicketID");
        tbl.Columns.Add(id);
        var key = new DmKey(new DmColumn[] { id });
        tbl.PrimaryKey = key;
        tbl.Columns.Add(new DmColumn<string>("Title"));
        tbl.Columns.Add(new DmColumn<bool>("IsAware"));
        tbl.Columns.Add(new DmColumn<string>("Description"));
        tbl.Columns.Add(new DmColumn<int>("StatusValue"));
        tbl.Columns.Add(new DmColumn<long>("EscalationLevel"));
        tbl.Columns.Add(new DmColumn<DateTime>("Opened"));
        tbl.Columns.Add(new DmColumn<DateTime>("Closed"));
        tbl.Columns.Add(new DmColumn<int>("CustomerID"));

        var dbTableBuilder = sqliteSyncProvider.GetDatabaseBuilder(tbl, DbBuilderOption.CreateOrUseExistingSchema | DbBuilderOption.CreateOrUseExistingTrackingTables);

        using (var sqliteConnection = new SQLiteConnection(sqliteConnectionString))
        {
            try
            {
                await sqliteConnection.OpenAsync();

                var script = dbTableBuilder.Script(sqliteConnection);

                dbTableBuilder.Apply(sqliteConnection);

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
            }


        }

    }

    private static async Task TestSimpleHttpServer()
    {
        using (var server = new KestrellTestServer())
        {
            var clientHandler = new ResponseDelegate(async baseAdress =>
            {
                var startTime = DateTime.Now;

                var httpClient = new HttpClient();
                var response = await httpClient.GetAsync(baseAdress + "first");
                response.EnsureSuccessStatusCode();
                var resString = await response.Content.ReadAsStringAsync();

                var ellapsedTime = DateTime.Now.Subtract(startTime).TotalSeconds;
                Console.WriteLine($"Ellapsed time : {ellapsedTime}sec.");

                startTime = DateTime.Now;
                response = await httpClient.GetAsync(baseAdress + "first");
                response.EnsureSuccessStatusCode();
                resString = await response.Content.ReadAsStringAsync();
                ellapsedTime = DateTime.Now.Subtract(startTime).TotalSeconds;
                Console.WriteLine($"Ellapsed time : {ellapsedTime}sec.");

            });

            var serverHandler = new RequestDelegate(async context =>
            {
                var pathFirst = new PathString("/first");
                await context.Response.WriteAsync("first_first");
            });

            await server.Run(serverHandler, clientHandler);
        };
    }

    private static async Task TestSyncWithTestServer()
    {
        var builder = new WebHostBuilder()
               .UseKestrel()
               .UseUrls("http://127.0.0.1:0/")
               .Configure(app =>
               {
                   app.UseSession();

                   app.Run(context =>
                   {
                       int? value = context.Session.GetInt32("Key");
                       if (context.Request.Path == new PathString("/first"))
                       {
                           Console.WriteLine("value.HasValue : " + value.HasValue);
                           value = 0;
                       }
                       Console.WriteLine("value.HasValue " + value.HasValue);
                       context.Session.SetInt32("Key", value.Value + 1);
                       return context.Response.WriteAsync(value.Value.ToString());

                   });
               })
               .ConfigureServices(services =>
               {
                   services.AddDistributedMemoryCache();
                   services.AddSession();
               });

        using (var server = new TestServer(builder))
        {
            var client = server.CreateClient();

            // Nothing here seems to work
            // client.BaseAddress = new Uri("http://localhost.fiddler/");

            var response = await client.GetAsync("first");
            response.EnsureSuccessStatusCode();
            Console.WriteLine("Server result : " + await response.Content.ReadAsStringAsync());

            client = server.CreateClient();
            var cookie = SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToList()).First();
            client.DefaultRequestHeaders.Add("Cookie", new CookieHeaderValue(cookie.Name, cookie.Value).ToString());

            Console.WriteLine("Server result : " + await client.GetStringAsync("/"));
            Console.WriteLine("Server result : " + await client.GetStringAsync("/"));
            Console.WriteLine("Server result : " + await client.GetStringAsync("/"));

        }
    }


    private static async Task TestSyncThroughWebApi()
    {
        ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddJsonFile("config.json", true);
        IConfiguration Configuration = configurationBuilder.Build();
        var clientConfig = Configuration["AppConfiguration:ClientConnectionString"];

        var clientProvider = new SqlSyncProvider(clientConfig);
        var proxyClientProvider = new WebProxyClientProvider(new Uri("http://localhost:56782/api/values"));

        var agent = new SyncAgent(clientProvider, proxyClientProvider);

        Console.WriteLine("Press a key to start...");
        Console.ReadKey();
        do
        {
            Console.Clear();
            Console.WriteLine("Sync Start");
            try
            {
                var s = await agent.SynchronizeAsync();
            }
            catch (SyncException e)
            {
                Console.WriteLine(e.ToString());
            }
            catch (Exception e)
            {
                Console.WriteLine("UNKNOW EXCEPTION : " + e.Message);
            }


            Console.WriteLine("Sync Ended. Press a key to start again, or Escapte to end");
        } while (Console.ReadKey().Key != ConsoleKey.Escape);

        Console.WriteLine("End");

    }

    /// <summary>
    /// Test syncking through Kestrell server
    /// </summary>
    private static async Task TestSyncThroughKestrellAsync()
    {
        var id = Guid.NewGuid();

        ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddJsonFile("config.json", true);
        IConfiguration Configuration = configurationBuilder.Build();
        var serverConfig = Configuration["AppConfiguration:ServerConnectionString"];
        var clientConfig = Configuration["AppConfiguration:ClientConnectionString"];

        var serverProvider = new SqlSyncProvider(serverConfig);
        var proxyServerProvider = new WebProxyServerProvider(serverProvider);

        var clientProvider = new SqlSyncProvider(clientConfig);
        var proxyClientProvider = new WebProxyClientProvider();

        var configuration = new SyncConfiguration(new[] { "ServiceTickets" });
        configuration.UseBulkOperations = false;
        configuration.DownloadBatchSizeInKB = 0;

        var agent = new SyncAgent(clientProvider, proxyClientProvider);

        serverProvider.SetConfiguration(configuration);

        using (var server = new KestrellTestServer())
        {
            var serverHandler = new RequestDelegate(async context =>
            {
                proxyServerProvider.SerializationFormat = SerializationFormat.Json;
                await proxyServerProvider.HandleRequestAsync(context);
            });
            var clientHandler = new ResponseDelegate(async (serviceUri) =>
            {
                proxyClientProvider.ServiceUri = new Uri(serviceUri);
                proxyClientProvider.SerializationFormat = SerializationFormat.Json;

                //var startTime = DateTime.Now;
                //var c = await proxyClientProvider.BeginSessionAsync(new SyncContext(Guid.NewGuid()));
                //var ellapsedTime = DateTime.Now.Subtract(startTime).TotalSeconds;
                //Console.WriteLine($"Ellapsed time : {ellapsedTime}sec.");

                //startTime = DateTime.Now;
                //c = await proxyClientProvider.BeginSessionAsync(new SyncContext(Guid.NewGuid()));
                //ellapsedTime = DateTime.Now.Subtract(startTime).TotalSeconds;
                //Console.WriteLine($"Ellapsed time : {ellapsedTime}sec.");

                var insertRowScript =
                $@"INSERT [ServiceTickets] ([ServiceTicketID], [Title], [Description], [StatusValue], [EscalationLevel], [Opened], [Closed], [CustomerID]) 
                VALUES (newid(), N'Insert One Row', N'Description Insert One Row', 1, 0, getdate(), NULL, 1)";

                proxyClientProvider.ServiceUri = new Uri(serviceUri);
                proxyClientProvider.SerializationFormat = SerializationFormat.Json;

                agent.SyncProgress += SyncProgress;
                agent.ApplyChangedFailed += ApplyChangedFailed;

                using (var sqlConnection = new SqlConnection(serverConfig))
                {
                    using (var sqlCmd = new SqlCommand(insertRowScript, sqlConnection))
                    {
                        sqlConnection.Open();
                        sqlCmd.ExecuteNonQuery();
                        sqlConnection.Close();
                    }
                }

                var session = await agent.SynchronizeAsync();
                Console.WriteLine($"Sync ended in {session.CompleteTime.Subtract(session.StartTime).TotalSeconds}sec. Total download / upload / conflicts : {session.TotalChangesDownloaded} / {session.TotalChangesDownloaded} / {session.TotalSyncConflicts} ");


                using (var sqlConnection = new SqlConnection(serverConfig))
                {
                    using (var sqlCmd = new SqlCommand(insertRowScript, sqlConnection))
                    {
                        sqlConnection.Open();
                        sqlCmd.ExecuteNonQuery();
                        sqlConnection.Close();
                    }
                }

                session = await agent.SynchronizeAsync();
                Console.WriteLine($"Sync ended in {session.CompleteTime.Subtract(session.StartTime).TotalSeconds}sec. Total download / upload / conflicts : {session.TotalChangesDownloaded} / {session.TotalChangesDownloaded} / {session.TotalSyncConflicts} ");


            });
            await server.Run(serverHandler, clientHandler);
        }
    }

    private static async Task FilterSync()
    {
        // Get SQL Server connection string
        ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddJsonFile("config.json", true);
        IConfiguration Configuration = configurationBuilder.Build();
        var serverConfig = Configuration["AppConfiguration:ServerFilteredConnectionString"];
        var clientConfig = "sqlitefiltereddb.db";

        SqlSyncProvider serverProvider = new SqlSyncProvider(serverConfig);
        SQLiteSyncProvider clientProvider = new SQLiteSyncProvider(clientConfig);

        // With a config when we are in local mode (no proxy)
        SyncConfiguration configuration = new SyncConfiguration(new string[] { "ServiceTickets" });
        //configuration.DownloadBatchSizeInKB = 500;
        configuration.UseBulkOperations = false;
        // Adding filters on schema
        configuration.Filters.Add("ServiceTickets", "CustomerID");

        SyncAgent agent = new SyncAgent(clientProvider, serverProvider, configuration);

        // Adding a parameter for this agent
        agent.Parameters.Add("ServiceTickets", "CustomerID", 1);

        do
        {
            Console.Clear();
            Console.WriteLine("Sync Start");
            try
            {
                var s = await agent.SynchronizeAsync();

            }
            catch (SyncException e)
            {
                Console.WriteLine(e.ToString());
            }
            catch (Exception e)
            {
                Console.WriteLine("UNKNOW EXCEPTION : " + e.Message);
            }


            Console.WriteLine("Sync Ended. Press a key to start again, or Escapte to end");
        } while (Console.ReadKey().Key != ConsoleKey.Escape);

        Console.WriteLine("End");
    }


    private static async Task TestSyncSQLite()
    {
        // Get SQL Server connection string
        ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddJsonFile("config.json", true);
        IConfiguration Configuration = configurationBuilder.Build();
        var serverConfig = Configuration["AppConfiguration:ServerConnectionString"];
        var clientConfig = Configuration["AppConfiguration:ClientSQLiteConnectionString"];
        var clientConfig2 = Configuration["AppConfiguration:ClientSQLiteConnectionString2"];
        var clientConfig3 = Configuration["AppConfiguration:ClientConnectionString"];

        SqlSyncProvider serverProvider = new SqlSyncProvider(serverConfig);
        SQLiteSyncProvider clientProvider = new SQLiteSyncProvider(clientConfig);
        SQLiteSyncProvider clientProvider2 = new SQLiteSyncProvider(clientConfig2);
        SqlSyncProvider clientProvider3 = new SqlSyncProvider(clientConfig3);

        // With a config when we are in local mode (no proxy)
        SyncConfiguration configuration = new SyncConfiguration(new string[] { "ServiceTickets" });
        //configuration.DownloadBatchSizeInKB = 500;
        configuration.UseBulkOperations = false;

        SyncAgent agent = new SyncAgent(clientProvider, serverProvider, configuration);
        SyncAgent agent2 = new SyncAgent(clientProvider2, serverProvider, configuration);
        SyncAgent agent3 = new SyncAgent(clientProvider3, serverProvider, configuration);

        agent.SyncProgress += SyncProgress;
        agent2.SyncProgress += SyncProgress;
        agent3.SyncProgress += SyncProgress;
        // agent.ApplyChangedFailed += ApplyChangedFailed;

        do
        {
            Console.Clear();
            Console.WriteLine("Sync Start");
            try
            {
                var s = await agent.SynchronizeAsync();
                var s2 = await agent2.SynchronizeAsync();
                var s3 = await agent3.SynchronizeAsync();

            }
            catch (SyncException e)
            {
                Console.WriteLine(e.ToString());
            }
            catch (Exception e)
            {
                Console.WriteLine("UNKNOW EXCEPTION : " + e.Message);
            }


            Console.WriteLine("Sync Ended. Press a key to start again, or Escapte to end");
        } while (Console.ReadKey().Key != ConsoleKey.Escape);

        Console.WriteLine("End");
    }


    private static async Task TestSync()
    {
        // Get SQL Server connection string
        ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddJsonFile("config.json", true);
        IConfiguration Configuration = configurationBuilder.Build();
        var serverConfig = Configuration["AppConfiguration:ServerConnectionString"];
        var clientConfig = Configuration["AppConfiguration:ClientConnectionString"];


        Guid id = Guid.NewGuid();

        using (var sqlConnection = new SqlConnection(clientConfig))
        {
            var script = $@"INSERT [ServiceTickets] 
                            ([ServiceTicketID], [Title], [Description], [StatusValue], [EscalationLevel], [Opened], [Closed], [CustomerID]) 
                            VALUES 
                            (N'{id.ToString()}', N'Conflict Line Client', N'Description client', 1, 0, getdate(), NULL, 1)";

            using (var sqlCmd = new SqlCommand(script, sqlConnection))
            {
                sqlConnection.Open();
                sqlCmd.ExecuteNonQuery();
                sqlConnection.Close();
            }
        }

        using (var sqlConnection = new SqlConnection(serverConfig))
        {
            var script = $@"INSERT [ServiceTickets] 
                            ([ServiceTicketID], [Title], [Description], [StatusValue], [EscalationLevel], [Opened], [Closed], [CustomerID]) 
                            VALUES 
                            (N'{id.ToString()}', N'Conflict Line Server', N'Description client', 1, 0, getdate(), NULL, 1)";

            using (var sqlCmd = new SqlCommand(script, sqlConnection))
            {
                sqlConnection.Open();
                sqlCmd.ExecuteNonQuery();
                sqlConnection.Close();
            }
        }


        SqlSyncProvider serverProvider = new SqlSyncProvider(serverConfig);
        SqlSyncProvider clientProvider = new SqlSyncProvider(clientConfig);

        // With a config when we are in local mode (no proxy)
        SyncConfiguration configuration = new SyncConfiguration(new string[] { "ServiceTickets" });
        //configuration.DownloadBatchSizeInKB = 500;
        SyncAgent agent = new SyncAgent(clientProvider, serverProvider, configuration);

        agent.SyncProgress += SyncProgress;
        agent.ApplyChangedFailed += ApplyChangedFailed;

        do
        {
            Console.Clear();
            Console.WriteLine("Sync Start");
            try
            {
                CancellationTokenSource cts = new CancellationTokenSource();
                CancellationToken token = cts.Token;
                var s = await agent.SynchronizeAsync(token);

            }
            catch (SyncException e)
            {
                Console.WriteLine(e.ToString());
            }
            catch (Exception e)
            {
                Console.WriteLine("UNKNOW EXCEPTION : " + e.Message);
            }


            Console.WriteLine("Sync Ended. Press a key to start again, or Escapte to end");
        } while (Console.ReadKey().Key != ConsoleKey.Escape);

        Console.WriteLine("End");
    }

    private static void ServerProvider_SyncProgress(object sender, SyncProgressEventArgs e)
    {
        SyncProgress(e, ConsoleColor.Red);
    }

    private static void SyncProgress(object sender, SyncProgressEventArgs e)
    {
        SyncProgress(e);
    }

    private static void SyncProgress(SyncProgressEventArgs e, ConsoleColor? consoleColor = null)
    {
        var sessionId = e.Context.SessionId.ToString();

        if (consoleColor.HasValue)
            Console.ForegroundColor = consoleColor.Value;

        switch (e.Context.SyncStage)
        {
            case SyncStage.BeginSession:
                Console.WriteLine($"Begin Session.");
                break;
            case SyncStage.EndSession:
                Console.WriteLine($"End Session.");
                break;
            case SyncStage.EnsureMetadata:
                if (e.Configuration != null)
                {
                    var ds = e.Configuration.ScopeSet;

                    Console.WriteLine($"Configuration readed. {ds.Tables.Count} table(s) involved.");

                    Func<JsonSerializerSettings> settings = new Func<JsonSerializerSettings>(() =>
                    {
                        var s = new JsonSerializerSettings();
                        s.Formatting = Formatting.Indented;
                        s.StringEscapeHandling = StringEscapeHandling.Default;
                        return s;
                    });
                    JsonConvert.DefaultSettings = settings;
                    var dsString = JsonConvert.SerializeObject(new DmSetSurrogate(ds));

                    //Console.WriteLine(dsString);
                }
                if (e.DatabaseScript != null)
                {
                    Console.WriteLine($"Database is created");
                    //Console.WriteLine(e.DatabaseScript);
                }
                break;
            case SyncStage.SelectedChanges:
                Console.WriteLine($"Selected changes : {e.ChangesStatistics.TotalSelectedChanges}");

                //Console.WriteLine($"{sessionId}. Selected added Changes : {e.ChangesStatistics.TotalSelectedChangesInserts}");
                //Console.WriteLine($"{sessionId}. Selected updates Changes : {e.ChangesStatistics.TotalSelectedChangesUpdates}");
                //Console.WriteLine($"{sessionId}. Selected deleted Changes : {e.ChangesStatistics.TotalSelectedChangesDeletes}");
                break;

            case SyncStage.AppliedChanges:
                Console.WriteLine($"Applied changes : {e.ChangesStatistics.TotalAppliedChanges}");
                break;
            //case SyncStage.ApplyingInserts:
            //    Console.WriteLine($"{sessionId}. Applying Inserts : {e.ChangesStatistics.AppliedChanges.Where(ac => ac.State == DmRowState.Added).Sum(ac => ac.ChangesApplied) }");
            //    break;
            //case SyncStage.ApplyingDeletes:
            //    Console.WriteLine($"{sessionId}. Applying Deletes : {e.ChangesStatistics.AppliedChanges.Where(ac => ac.State == DmRowState.Deleted).Sum(ac => ac.ChangesApplied) }");
            //    break;
            //case SyncStage.ApplyingUpdates:
            //    Console.WriteLine($"{sessionId}. Applying Updates : {e.ChangesStatistics.AppliedChanges.Where(ac => ac.State == DmRowState.Modified).Sum(ac => ac.ChangesApplied) }");
            //    break;
            case SyncStage.WriteMetadata:
                if (e.Scopes != null)
                {
                    Console.WriteLine($"Writing Scopes : ");
                    e.Scopes.ForEach(sc => Console.WriteLine($"\t{sc.Id} synced at {sc.LastSync}. "));
                }
                break;
            case SyncStage.CleanupMetadata:
                Console.WriteLine($"CleanupMetadata");
                break;
        }

        Console.ResetColor();
    }

    static void ApplyChangedFailed(object sender, ApplyChangeFailedEventArgs e)
    {
        // Note: LocalChange table name may be null if the record does not exist on the server. So use the remote table name.
        string tableName = e.Conflict.RemoteChanges.TableName;

        // Line exist on client, not on server, force to create it
        if (e.Conflict.Type == ConflictType.RemoteInsertLocalNoRow || e.Conflict.Type == ConflictType.RemoteUpdateLocalNoRow)
            e.Action = ApplyAction.RetryWithForceWrite;
        else
            e.Action = ApplyAction.RetryWithForceWrite;

    }
}