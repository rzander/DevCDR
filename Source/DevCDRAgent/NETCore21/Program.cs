using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using System.Net.NetworkInformation;
using System.Reflection;
using DevCDRAgent.Modules;

namespace service
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            IHost host = new HostBuilder()
                 .ConfigureHostConfiguration(configHost =>
                 {
                     configHost.SetBasePath(Directory.GetCurrentDirectory());
                     configHost.AddEnvironmentVariables(prefix: "ASPNETCORE_");
                     configHost.AddCommandLine(args);
                 })
                 .ConfigureAppConfiguration((hostContext, configApp) =>
                 {
                     configApp.SetBasePath(Directory.GetCurrentDirectory());
                     configApp.AddEnvironmentVariables(prefix: "ASPNETCORE_");
                     configApp.AddJsonFile($"appsettings.json", false);
                     configApp.AddJsonFile($"appsettings.{hostContext.HostingEnvironment.EnvironmentName}.json", true);
                     configApp.AddCommandLine(args);
                 })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddLogging();
                    services.AddHostedService<ServiceHost>();
                    services.AddSingleton(typeof(ICommonService), typeof(devcdrService));
                })
                .ConfigureLogging((hostContext, configLogging) =>
                {
                    configLogging.AddConsole();
                    configLogging.AddDebug();
                })
                .Build();

            await host.RunAsync();
        }

        public class ServiceHost : IHostedService
        {
            IApplicationLifetime appLifetime;
            ILogger<ServiceHost> logger;
            IHostingEnvironment environment;
            IConfiguration configuration;
            ICommonService commonService;
            public ServiceHost(
                IConfiguration configuration,
                IHostingEnvironment environment,
                ILogger<ServiceHost> logger,
                IApplicationLifetime appLifetime,
                ICommonService commonService)
            {
                this.configuration = configuration;
                this.logger = logger;
                this.appLifetime = appLifetime;
                this.environment = environment;
                this.commonService = commonService;
            }


            public Task StartAsync(CancellationToken cancellationToken)
            {
                this.logger.LogInformation("StartAsync method called.");

                this.appLifetime.ApplicationStarted.Register(OnStarted);
                this.appLifetime.ApplicationStopping.Register(OnStopping);
                this.appLifetime.ApplicationStopped.Register(OnStopped);

                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            private void OnStarted()
            {
                this.commonService.OnStart();
            }

            private void OnStopping()
            {
            }

            private void OnStopped()
            {
                this.commonService.OnStop();
            }
        }

        public interface ICommonService
        {
            void OnStart();
            void OnStop();
        }

        public abstract class CommonServiceBase : ICommonService
        {
            private IConfiguration configuration;
            ILogger<CommonServiceBase> logger;

            public IConfiguration Configuration => this.configuration;
            public ILogger<CommonServiceBase> Logger => this.logger;


            public CommonServiceBase(
                IConfiguration configuration,
                ILogger<CommonServiceBase> logger)
            {
                this.configuration = configuration;
                this.logger = logger;
            }

            public abstract void OnStart();

            public abstract void OnStop();
        }

        public class devcdrService : CommonServiceBase
        {
            private System.Timers.Timer tReCheck = new System.Timers.Timer(61000); //1min
            private System.Timers.Timer tReInit = new System.Timers.Timer(120100); //2min

            private static string Hostname = Environment.MachineName;
            private static HubConnection connection;
            private static string sScriptResult = "";
            private bool isconnected = false;
            private bool isstopping = false;

            int InventoryCheckHours = 12;
            int HealtchCheckHours = 1;
            DateTime InventorySuccess = new DateTime();
            DateTime HealthCheckSuccess = new DateTime();
            DateTime LastConnection = new DateTime();
            int ConnectionErrors = 0;
            string Groups = "Linux";
            int StatusDelay = 5000;
            

            public string Uri { get; set; } = "https://devcdrcore.azurewebsites.net/chat";

            public devcdrService(IConfiguration configuration, ILogger<devcdrService> logger) : base(configuration, logger)
            {
                logger.LogInformation("Class instatiated");

                if (!string.IsNullOrEmpty(configuration["settings:DevCDRURL"]))
                {
                    Uri = configuration["settings:DevCDRURL"];
                }

                if (!string.IsNullOrEmpty(configuration["settings:Groups"]))
                {
                    Groups = configuration["settings:Groups"];
                }
            }

            public override void OnStart()
            {
                this.Logger.LogInformation("devcdrService OnStart");
                
                isstopping = false;
                sScriptResult = DateTime.Now.ToString();
                tReCheck.Elapsed -= TReCheck_Elapsed;
                tReCheck.Elapsed += TReCheck_Elapsed;
                tReCheck.Enabled = true;
                tReCheck.AutoReset = true;

                tReInit.Elapsed -= TReInit_Elapsed;
                tReInit.Elapsed += TReInit_Elapsed;
                tReInit.Enabled = true;
                tReInit.AutoReset = true;

                if (connection != null)
                {
                    try
                    {
                        connection.DisposeAsync().Wait(1000);
                    }
                    catch { }
                }

                connection = new HubConnectionBuilder()
                    .WithUrl(Uri)
                    .Build();


                connection.Closed += async (error) =>
                {
                    if (!isstopping)
                    {
                        try
                        {
                            await Task.Delay(new Random().Next(0, 5) * 1000); // wait 0-5s
                            await connection.StartAsync();
                            isconnected = true;
                            Console.WriteLine("Connected with " + Uri);
                            LastConnection = DateTime.Now;
                            ConnectionErrors = 0;
                            Connect();

                        }
                        catch (Exception ex)
                        {
                            isconnected = false;
                            Console.WriteLine(ex.Message);
                            //Trace.WriteLine("\tError: " + ex.Message + " " + DateTime.Now.ToString());
                            Random rnd = new Random();
                            tReInit.Interval = 10000 + rnd.Next(1, 90000); //randomize ReInit intervall
                        }
                    }
                };

                try
                {
                    connection.StartAsync().Wait();
                    isconnected = true;
                    Console.WriteLine("Connected with " + Uri);
                    //Trace.WriteLine("Connected with " + Uri + " " + DateTime.Now.ToString());
                    LastConnection = DateTime.Now;
                    ConnectionErrors = 0;
                    Connect();
                }
                catch (Exception ex)
                {
                    isconnected = false;
                    Console.WriteLine(ex.Message);
                    //Trace.WriteLine("\tError: " + ex.Message + " " + DateTime.Now.ToString());

                    ConnectionErrors++;

                    //Only fallback if we have internet...
                    if (IsConnectedToInternet())
                    {
                        //Fallback to default endpoint after 3Days and 15 Errors
                        if (((DateTime.Now - LastConnection).TotalDays > 3) && (ConnectionErrors >= 15))
                        {
                            Uri = "https://devcdrcore.azurewebsites.net/chat";
                            Hostname = Environment.MachineName + "_BAD";
                        }
                    }
                    else
                    {
                        //No Internet, lets ignore connection errors...
                        ConnectionErrors = 0;
                    }

                    Random rnd = new Random();
                    tReInit.Interval = 10000 + rnd.Next(1, 90000); //randomize ReInit intervall
                                                                   //Program.MinimizeFootprint();
                }

            }

            public override void OnStop()
            {
                this.Logger.LogInformation("CommonSampleService OnStop");
            }

            private void TReInit_Elapsed(object sender, ElapsedEventArgs e)
            {
                try
                {
                    Random rnd = new Random();
                    tReInit.Interval = 120100 + rnd.Next(1, 30000); //randomize ReInit intervall

                    if (connection != null && isconnected)
                    {
                        connection.SendAsync("Init", Hostname);

                        if (Hostname == Environment.MachineName) //No Inventory or Healthcheck if agent is running as user or with custom Name
                        {
                            if (InventoryCheckHours > 0) //Inventory is enabled
                            {
                                var tLastCheck = DateTime.Now - InventorySuccess;

                                //Run Inventory every x Hours
                                if (tLastCheck.TotalHours >= InventoryCheckHours)
                                {
                                    //Trace.WriteLine(DateTime.Now.ToString() + " starting Inventory...");
                                    //Trace.Flush();
                                    System.Threading.Thread.Sleep(1000);

                                    connection.SendAsync("Inventory", Hostname);

                                    InventorySuccess = DateTime.Now;
                                }
                            }

                            if (HealtchCheckHours > 0) //Healthcheck is enabled
                            {
                                var tLastCheck = DateTime.Now - HealthCheckSuccess;

                                //Run HealthChekc every x Hours
                                if (tLastCheck.TotalHours >= HealtchCheckHours)
                                {
                                    //Trace.WriteLine(DateTime.Now.ToString() + " starting HealthCheck...");
                                    //Trace.Flush();
                                    System.Threading.Thread.Sleep(3000);

                                    connection.SendAsync("HealthCheck", Hostname);

                                    HealthCheckSuccess = DateTime.Now;
                                }
                            }
                        }

                    }

                    if (!isconnected)
                    {
                        OnStart();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    //Trace.Write(DateTime.Now.ToString() + " ERROR ReInit: " + ex.Message);
                    OnStart();
                }
            }

            private void TReCheck_Elapsed(object sender, ElapsedEventArgs e)
            {
                try
                {
                    if (!isconnected)
                    {
                        OnStart();
                    }
                }
                catch { }
            }

            private void Connect()
            {
                try
                {
                    connection.On<string, string>("returnPS", (s1, s2) =>
                    {
                        TimeSpan timeout = new TimeSpan(0, 5, 0); //default timeout = 5min
                        DateTime dStart = DateTime.Now;
                        TimeSpan dDuration = DateTime.Now - dStart;

                        try
                        {
                            using (PowerShell PowerShellInstance = PowerShell.Create())
                            {
                                //Console.WriteLine(DateTime.Now.ToString() + "\t run PS... " + s1);
                                Trace.WriteLine(DateTime.Now.ToString() + "\t run PS... " + s1);
                                try
                                {
                                    PowerShellInstance.AddScript(s1);
                                    PSDataCollection<PSObject> outputCollection = new PSDataCollection<PSObject>();

                                    outputCollection.DataAdding += OutputCollection_DataAdding; ;
                                    //PowerShellInstance.Streams.Error.DataAdding += ConsoleError;

                                    IAsyncResult async = PowerShellInstance.BeginInvoke<PSObject, PSObject>(null, outputCollection);
                                    while (async.IsCompleted == false || dDuration > timeout)
                                    {
                                        //Thread.Sleep(200);
                                        dDuration = DateTime.Now - dStart;
                                        if (tReInit.Interval > 5000)
                                            tReInit.Interval = 5000;
                                    }

                                    Console.WriteLine(DateTime.Now.ToString() + "\t run PS... " + s1);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("There was an error: {0}", ex.Message);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("ERROR: " + ex.Message);
                        }

                        //Program.MinimizeFootprint();
                    });

                    //New 0.9.0.6
                    connection.On<string, string>("returnPSAsync", (s1, s2) =>
                    {
                        //Trace.WriteLine(DateTime.Now.ToString() + "\t run PS async... " + s1);
                        var tSWScan = Task.Run(() =>
                        {
                            //using (PowerShell PowerShellInstance = PowerShell.Create())
                            //{
                            //    try
                            //    {
                            //        PowerShellInstance.AddScript(s1);
                            //        var PSResult = PowerShellInstance.Invoke();
                            //        if (PSResult.Count() > 0)
                            //        {
                            //            string sResult = PSResult.Last().BaseObject.ToString();

                            //            if (!string.IsNullOrEmpty(sResult)) //Do not return empty results
                            //            {
                            //                if (sResult != sScriptResult)
                            //                {
                            //                    sScriptResult = sResult;
                            //                    Random rnd = new Random();
                            //                    tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max Xs to ReInit
                            //                }
                            //            }
                            //        }
                            //    }
                            //    catch (Exception ex)
                            //    {
                            //        Console.WriteLine("There was an error: {0}", ex.Message);
                            //    }
                            //}

                            //Program.MinimizeFootprint();
                        });
                    });

                    connection.On<string>("init", (s1) =>
                    {
                        try
                        {
                            Trace.Write(DateTime.Now.ToString() + "\t Agent init... ");
                            connection.SendAsync("Init", Hostname).ContinueWith(task1 =>
                            {
                            });
                            Trace.WriteLine(" done.");
                        }
                        catch { }
                        try
                        {
                            foreach (string sGroup in Groups.Split(';'))
                            {
                                connection.SendAsync("JoinGroup", sGroup).ContinueWith(task1 =>
                                {
                                });
                            }
                            //Program.MinimizeFootprint();
                        }
                        catch { }
                    });

                    connection.On<string>("reinit", (s1) =>
                    {
                        try
                        {
                            Random rnd = new Random();
                            tReInit.Interval = rnd.Next(200, StatusDelay); //wait max 5s to ReInit
                        }
                        catch { }
                    });

                    connection.On<string>("status", (s1) =>
                    {
                        try
                        {
                            //Trace.Write(DateTime.Now.ToString() + "\t send status...");
                            string sResult = "{}";

                            var host = Dns.GetHostEntry(Dns.GetHostName());

                            JObject jStatus = new JObject();
                            jStatus.Add("Hostname", Environment.MachineName);
                            jStatus.Add("id", Environment.MachineName);
                            jStatus.Add("Internal IP", host.AddressList.FirstOrDefault(t => t.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToString());
                            jStatus.Add("Last Reboot", DateTime.Now);
                            jStatus.Add("Reboot Pending", false);
                            jStatus.Add("Users Online", true);
                            jStatus.Add("OS", System.Runtime.InteropServices.RuntimeInformation.OSDescription.Split('#')[0]); // Environment.OSVersion.ToString());
                            jStatus.Add("Version", Environment.OSVersion.Version.ToString());
                            jStatus.Add("Arch", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString()); // Environment.Is64BitProcess ? "64-bit" : "???");
                            jStatus.Add("Lang", Thread.CurrentThread.CurrentCulture.LCID.ToString());
                            jStatus.Add("User", Environment.UserName);
                            jStatus.Add("ScriptResult", sScriptResult);
                            jStatus.Add("Groups", Groups);

                            sResult = jStatus.ToString();

                            //using (PowerShell PowerShellInstance = PowerShell.Create())
                            //{
                            //    try
                            //    {
                            //        PowerShellInstance.AddScript(Properties.Settings.Default.PSStatus);
                            //        var PSResult = PowerShellInstance.Invoke();
                            //        if (PSResult.Count() > 0)
                            //        {
                            //            sResult = PSResult.Last().BaseObject.ToString();
                            //            sResult = sResult.Replace(Environment.MachineName, Hostname);
                            //            JObject jRes = JObject.Parse(sResult);
                            //            jRes.Add("ScriptResult", sScriptResult);
                            //            jRes.Add("Groups", Properties.Settings.Default.Groups);
                            //            sResult = jRes.ToString();
                            //        }
                            //    }
                            //    catch (Exception ex)
                            //    {
                            //        Console.WriteLine(" There was an error: {0}", ex.Message);
                            //    }
                            //}

                            //connection.InvokeAsync("Status", new object[] { Hostname, sResult }).ContinueWith(task1 =>
                            //{
                            //});
                            connection.InvokeAsync("Status", Hostname, sResult).Wait(1000);
                            Trace.WriteLine(" done.");
                            //Program.MinimizeFootprint();
                        }
                        catch (Exception ex)
                        {
                            Trace.Write(DateTime.Now.ToString() + " ERROR: " + ex.Message);
                        }
                    });

                    connection.On<string>("version", (s1) =>
                    {
                        try
                        {
                            Trace.Write(DateTime.Now.ToString() + "\t Get Version... ");
                            //Get File-Version
                            sScriptResult = (FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location)).FileVersion.ToString();
                            Trace.WriteLine(sScriptResult);

                            Random rnd = new Random();
                            tReInit.Interval = rnd.Next(200, StatusDelay); //wait max 5s to ReInit
                        }
                        catch (Exception ex)
                        {
                            Trace.Write(DateTime.Now.ToString() + " ERROR: " + ex.Message);
                        }
                    });

                    connection.On<string>("wol", (s1) =>
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(s1))
                            {
                                foreach (string sMAC in s1.Split(';'))
                                {
                                    try
                                    {
                                        WOL.WakeUp(sMAC); //Send Broadcast

                                        //Send to local Gateway
                                        foreach (NetworkInterface f in NetworkInterface.GetAllNetworkInterfaces())
                                            if (f.OperationalStatus == OperationalStatus.Up)
                                                foreach (GatewayIPAddressInformation d in f.GetIPProperties().GatewayAddresses)
                                                {
                                                    //Only use IPv4
                                                    if (d.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                                    {
                                                        WOL.WakeUp(d.Address, 9, sMAC);
                                                    }
                                                }
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }
                    });

                    connection.On<string>("setinstance", (s1) =>
                    {
                        Trace.WriteLine(DateTime.Now.ToString() + "\t Set instance: " + s1);
                        try
                        {
                            //if (!string.IsNullOrEmpty(s1))
                            //{
                            //    string sConfig = Assembly.GetExecutingAssembly().Location + ".config";
                            //    XmlDocument doc = new XmlDocument();
                            //    doc.Load(sConfig);
                            //    doc.SelectSingleNode("/configuration/applicationSettings/DevCDRAgent.Properties.Settings/setting[@name='Instance']/value").InnerText = s1;
                            //    doc.Save(sConfig);
                            //    RestartService();

                            //    //Update Advanced Installer Persistent Properties
                            //    RegistryKey myKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Zander Tools\\{54F5CC06-300A-4DD4-94D9-0E18B2BE8DF1}", true);
                            //    if (myKey != null)
                            //    {
                            //        myKey.SetValue("INSTANCE", s1.Trim(), RegistryValueKind.String);
                            //        myKey.Close();
                            //    }
                            //}
                        }
                        catch { }
                    });

                    connection.On<string>("setendpoint", (s1) =>
                    {
                        Trace.WriteLine(DateTime.Now.ToString() + "\t Set Endpoint: " + s1);
                        try
                        {
                            //if (!string.IsNullOrEmpty(s1))
                            //{
                            //    if (s1.StartsWith("https://"))
                            //    {
                            //        string sConfig = Assembly.GetExecutingAssembly().Location + ".config";
                            //        XmlDocument doc = new XmlDocument();
                            //        doc.Load(sConfig);
                            //        doc.SelectSingleNode("/configuration/applicationSettings/DevCDRAgent.Properties.Settings/setting[@name='Endpoint']/value").InnerText = s1;
                            //        doc.Save(sConfig);
                            //        RestartService();

                            //        //Update Advanced Installer Persistent Properties
                            //        RegistryKey myKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Zander Tools\\{54F5CC06-300A-4DD4-94D9-0E18B2BE8DF1}", true);
                            //        if (myKey != null)
                            //        {
                            //            myKey.SetValue("ENDPOINT", s1.Trim(), RegistryValueKind.String);
                            //            myKey.Close();
                            //        }
                            //    }
                            //}
                        }
                        catch { }
                    });

                    connection.On<string>("setgroups", (s1) =>
                    {
                        Trace.WriteLine(DateTime.Now.ToString() + "\t Set Groups: " + s1);
                        try
                        {
                            //if (!string.IsNullOrEmpty(s1))
                            //{
                            //    string sConfig = Assembly.GetExecutingAssembly().Location + ".config";
                            //    XmlDocument doc = new XmlDocument();
                            //    doc.Load(sConfig);
                            //    doc.SelectSingleNode("/configuration/applicationSettings/DevCDRAgent.Properties.Settings/setting[@name='Groups']/value").InnerText = s1;
                            //    doc.Save(sConfig);

                            //    RestartService();

                            //    //Update Advanced Installer Persistent Properties
                            //    RegistryKey myKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Zander Tools\\{54F5CC06-300A-4DD4-94D9-0E18B2BE8DF1}", true);
                            //    if (myKey != null)
                            //    {
                            //        myKey.SetValue("GROUPS", s1.Trim(), RegistryValueKind.String);
                            //        myKey.Close();
                            //    }
                            //}
                        }
                        catch { }
                    });

                    connection.On<string>("getgroups", (s1) =>
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(s1))
                            {
                                sScriptResult = Groups;

                                Random rnd = new Random();
                                tReInit.Interval = rnd.Next(200, StatusDelay); //wait max 5s to ReInit
                            }
                        }
                        catch { }
                    });

                    connection.On<string>("restartservice", (s1) =>
                    {
                        try
                        {
                            //RestartService();
                            sScriptResult = "restart Agent...";
                        }
                        catch { }
                    });

                    connection.On<string>("rzinstall", (s1) =>
                    {
                        //RZInst(s1);
                    });

                    connection.On<string>("rzupdate", (s1) =>
                    {
                        //var tSWScan = Task.Run(() =>
                        //{
                        //    try
                        //    {
                        //        sScriptResult = "Detecting RZ updates...";
                        //        Random rnd = new Random();
                        //        tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit

                        //        RZUpdater oUpdate = new RZUpdater();
                        //        RZScan oScan = new RZScan(false, false);

                        //        oScan.GetSWRepository().Wait(30000);
                        //        oScan.SWScan().Wait(30000);
                        //        oScan.CheckUpdates(null).Wait(30000);

                        //        if (string.IsNullOrEmpty(s1))
                        //        {
                        //            sScriptResult = oScan.NewSoftwareVersions.Count.ToString() + " RZ updates found";
                        //            rnd = new Random();
                        //            tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                        //        }

                        //        List<string> lSW = new List<string>();
                        //        foreach (var oSW in oScan.NewSoftwareVersions)
                        //        {
                        //            if (string.IsNullOrEmpty(s1) || s1 == "HUB")
                        //            {
                        //                RZInst(oSW.Shortname);
                        //            }
                        //            else
                        //            {
                        //                var SWList = s1.Split(';');
                        //                if (SWList.Contains(oSW.Shortname))
                        //                    RZInst(oSW.Shortname);
                        //            }
                        //        }
                        //    }
                        //    catch { }
                        //});
                    });

                    connection.On<string>("rzscan", (s1) =>
                    {
                        //var tSWScan = Task.Run(() =>
                        //{
                        //    try
                        //    {
                        //        sScriptResult = "Detecting updates...";
                        //        Random rnd = new Random();
                        //        tReInit.Interval = rnd.Next(2000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit

                        //        RZUpdater oUpdate = new RZUpdater();
                        //        RZScan oScan = new RZScan(false, false);

                        //        oScan.GetSWRepository().Wait(30000);
                        //        oScan.SWScan().Wait(30000);
                        //        oScan.CheckUpdates(null).Wait(30000);

                        //        List<string> lSW = new List<string>();
                        //        foreach (var SW in oScan.NewSoftwareVersions)
                        //        {
                        //            lSW.Add(SW.Shortname + " " + SW.ProductVersion + " (old:" + SW.MSIProductID + ")");
                        //        }

                        //        sScriptResult = JsonConvert.SerializeObject(lSW);
                        //        rnd = new Random();
                        //        tReInit.Interval = rnd.Next(2000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                        //    }
                        //    catch { }
                        //});
                    });

                    connection.On<string>("inject", (s1) =>
                    {
                        //var tSWScan = Task.Run(() =>
                        //{
                        //    try
                        //    {
                        //        sScriptResult = "Inject external code...";
                        //        try
                        //        {
                        //            ManagedInjection.Inject(s1);
                        //            sScriptResult = "External code executed.";
                        //        }
                        //        catch (Exception ex)
                        //        {
                        //            sScriptResult = "Injection error:" + ex.Message;
                        //        }
                        //    }
                        //    catch { }
                        //});
                    });

                    connection.On<string, string>("userprocess", (cmd, arg) =>
                    {
                        //var tSWScan = Task.Run(() =>
                        //{
                        //    if (string.IsNullOrEmpty(cmd))
                        //    {
                        //        cmd = Assembly.GetExecutingAssembly().Location;
                        //        arg = Environment.MachineName + ":" + "%USERNAME%";
                        //    }

                        //    try
                        //    {
                        //        if (string.IsNullOrEmpty(arg))
                        //        {
                        //            ProcessExtensions.StartProcessAsCurrentUser(cmd, null, null, false);
                        //        }
                        //        else
                        //        {
                        //            ProcessExtensions.StartProcessAsCurrentUser(null, cmd + " " + arg, null, false);
                        //        }
                        //    }
                        //    catch (Exception ex)
                        //    {
                        //        Console.WriteLine(ex.Message);
                        //    }
                        //});

                    });

                    //connection.InvokeCoreAsync("Init", new object[] { Hostname }).Wait();
                    connection.InvokeAsync("Init", Hostname).ContinueWith(task1 =>
                    {
                        try
                        {
                            if (task1.IsFaulted)
                            {
                                Console.WriteLine("There was an error calling send: {0}", task1.Exception.GetBaseException());
                            }
                            else
                            {
                                try
                                {
                                    foreach (string sGroup in Groups.Split(';'))
                                    {
                                        connection.InvokeAsync("JoinGroup", sGroup).ContinueWith(task2 =>
                                        {
                                        });
                                    }
                                    //Program.MinimizeFootprint();
                                }
                                catch { }
                            }
                        }
                        catch { }
                    });

                }
                catch (Exception ex)
                {
                    Console.WriteLine("There was an error: {0}", ex.Message);
                }
            }

            private void OutputCollection_DataAdding(object sender, DataAddingEventArgs e)
            {
                sScriptResult = e.ItemAdded.ToString();
            }
        }

        public static bool IsConnectedToInternet()
        {
            if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
            {
                using (WebClient webClient = new WebClient())
                {
                    try
                    {
                        string sResult = webClient.DownloadString("http://www.msftncsi.com/ncsi.txt");
                        if (sResult == "Microsoft NCSI")
                            return true;
                    }
                    catch { }
                }
            }

            return false;
        }
    }
}
