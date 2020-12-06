using DevCDRAgent.Modules;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RuckZuck.Base;
using RZUpdate;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Management.Automation;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Xml;


namespace DevCDRAgent
{
    public partial class Service1 : ServiceBase
    {
        private System.Timers.Timer tReCheck = new System.Timers.Timer(61000); //1min
        private System.Timers.Timer tReInit = new System.Timers.Timer(120100); //2min
        private DateTime tLastStatus = new DateTime();
        private DateTime tLastPSAsync = new DateTime();
        private DateTime tLastCertReq = new DateTime();
        private long lConnectCount = 0;

        private static string Hostname = Environment.MachineName;
        private static HubConnection connection;
        private static string sScriptResult = "";
        private static X509AgentCert xAgent;
        private bool isconnected = false;
        private bool isstopping = false;
        public string Uri { get; set; } = Properties.Settings.Default.Endpoint;
        public static AzureLogAnalytics AzureLog = new AzureLogAnalytics();
        public static long iLastEventId = 0;
        public int HealthCheckMinutes = 5;
        EventLogWatcher watcher;

        static readonly object _locker = new object();

        public Service1(string Host)
        {
            if (!string.IsNullOrEmpty(Host))
                Hostname = Host;

            InitializeComponent();
        }

        internal void TReInit_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                Random rnd = new Random();
                tReInit.Interval = 150100 + rnd.Next(1, 30000); //randomize ReInit intervall

                if (connection != null && isconnected)
                {
                    if (string.IsNullOrEmpty(xAgent.Signature))
                        connection.SendAsync("Init", Hostname);
                    else
                    {
                        connection.SendAsync("InitCert", Hostname, xAgent.Signature);
                    }


                    if (Hostname == Environment.MachineName) //No Inventory or Healthcheck if agent is running as user or with custom Name
                    {
                        if (Properties.Settings.Default.InventoryCheckHours > 0) //Inventory is enabled
                        {
                            var tLastCheck = DateTime.Now - Properties.Settings.Default.InventorySuccess;
                            xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID );

                            //Run Inventory every x Hours
                            if (tLastCheck.TotalHours >= Properties.Settings.Default.InventoryCheckHours)
                            {
                                lock (_locker)
                                {
                                    Properties.Settings.Default.InventorySuccess = DateTime.Now;

                                    if (string.IsNullOrEmpty(xAgent.Signature))
                                    {
                                        Trace.WriteLine(DateTime.Now.ToString() + "\t starting Inventory...");
                                        connection.SendAsync("Inventory", Hostname);
                                        tReInit.Interval = 60000 + rnd.Next(1, 30000); //randomize ReInit intervall
                                        return;
                                    }
                                    else
                                    {
                                        Trace.WriteLine(DateTime.Now.ToString() + "\t run inventory (cert) ... ");
                                        tLastPSAsync = DateTime.Now;
                                        xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID);
                                        string sEndPoint = xAgent.EndpointURL.Replace("/chat", "");
                                        string sCommand = "";

                                        //recommended way
                                        using (HttpClient oWebClient = new HttpClient())
                                        {
                                            sCommand = oWebClient.GetStringAsync(sEndPoint + "/devcdr/getfile?filename=inventory2.ps1&signature=" + xAgent.Signature).Result;
                                        }
                                        //alternative way
                                        if (string.IsNullOrEmpty(sCommand))
                                            sCommand = "Invoke-RestMethod -Uri '" + sEndPoint + "/devcdr/getfile?filename=inventory2.ps1&signature=" + xAgent.Signature + "' | IEX";

                                        var tSWScan = Task.Run(() =>
                                        {
                                            using (PowerShell PowerShellInstance = PowerShell.Create())
                                            {
                                                try
                                                {
                                                    PowerShellInstance.AddScript(sCommand);
                                                    var PSResult = PowerShellInstance.Invoke();
                                                    if (PSResult.Count() > 0)
                                                    {
                                                        string sResult2 = PSResult.Last().BaseObject.ToString();

                                                        if (!string.IsNullOrEmpty(sResult2)) //Do not return empty results
                                                        {
                                                            using (HttpClient oWebClient = new HttpClient())
                                                            {
                                                                HttpContent oCont = new StringContent(sResult2);
                                                                var sRes = oWebClient.PostAsync(sEndPoint + "/devcdr/PutFile?signature=" + xAgent.Signature, oCont).Result;
                                                                Trace.WriteLine(DateTime.Now.ToString() + "\t PutFile Status code: " + sRes.StatusCode.ToString());

                                                            }

                                                            sScriptResult = "Inventory completed...";
                                                            Random rnd2 = new Random();
                                                            tReInit.Interval = rnd2.Next(200, Properties.Settings.Default.StatusDelay); //wait max Xs to ReInit
                                                            Properties.Settings.Default.InventorySuccess = DateTime.Now;
                                                            Trace.WriteLine(DateTime.Now.ToString() + "\t Inventory completed.");
                                                        }
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    Console.WriteLine("There was an error: {0}", ex.Message);
                                                }
                                            }
                                        });
                                    }

                                    Properties.Settings.Default.Save();
                                }
                            }
                        }

                        if (HealthCheckMinutes > 0) //Healthcheck is enabled
                        {
                            var tLastCheck = DateTime.Now - Properties.Settings.Default.HealthCheckSuccess;

                            //Run HealthChekc every x Minutes
                            if (tLastCheck.TotalMinutes >= HealthCheckMinutes)
                            {
                                //Update HardwareID
                                string sResult = "{}";
                                using (PowerShell PowerShellInstance = PowerShell.Create())
                                {
                                    try
                                    {
                                        PowerShellInstance.AddScript(Properties.Settings.Default.PSStatus);
                                        var PSResult = PowerShellInstance.Invoke();
                                        if (PSResult.Count() > 0)
                                        {
                                            sResult = PSResult.Last().BaseObject.ToString();
                                            sResult = sResult.Replace(Environment.MachineName, Hostname);
                                            JObject jRes = JObject.Parse(sResult);

                                            if (Properties.Settings.Default.HardwareID != jRes["id"].Value<string>())
                                            {
                                                Properties.Settings.Default.HardwareID = jRes["id"].Value<string>();
                                                Properties.Settings.Default.Save();
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine(" There was an error: {0}", ex.Message);
                                    }
                                }

                                lock (_locker)
                                {
                                    xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID);
                                    Properties.Settings.Default.HealthCheckSuccess = DateTime.Now;

                                    if (string.IsNullOrEmpty(xAgent.Signature))
                                    {
                                        Trace.WriteLine(DateTime.Now.ToString() + " starting HealthCheck...");
                                        connection.SendAsync("HealthCheck", Hostname);
                                    }
                                    else
                                    {
                                        //connection.SendAsync("HealthCheckCert", Hostname, xAgent.Signature, Properties.Settings.Default.CustomerID);
                                        string sEndPoint = xAgent.EndpointURL.Replace("/chat", "");
                                        string sCommand = "Invoke-RestMethod -Uri '" + sEndPoint + "/devcdr/getfile?filename=compliance.ps1&signature=" + xAgent.Signature + "' | IEX";

                                        var tSWScan = Task.Run(() =>
                                        {
                                            using (PowerShell PowerShellInstance = PowerShell.Create())
                                            {
                                                try
                                                {
                                                    PowerShellInstance.AddScript(sCommand);
                                                    var PSResult = PowerShellInstance.Invoke();
                                                    if (PSResult.Count() > 0)
                                                    {
                                                        string sResult2 = PSResult.Last().BaseObject.ToString();

                                                        if (!string.IsNullOrEmpty(sResult2)) //Do not return empty results
                                                        {

                                                            //sScriptResult = "Compliance check completed...";
                                                            connection.InvokeAsync("UpdateComplianceCert2", sResult2, xAgent.Signature).Wait(2000);

                                                            //Random rnd2 = new Random();
                                                            //tReInit.Interval = rnd2.Next(200, Properties.Settings.Default.StatusDelay); //wait max Xs to ReInit

                                                        }
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    Console.WriteLine("There was an error: {0}", ex.Message);
                                                }
                                            }

                                            Program.MinimizeFootprint();
                                        });
                                    }


                                    Properties.Settings.Default.Save();
                                }
                            }
                        }
                    }

                }

                if (!isconnected)
                {
                    OnStart(new string[0]);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Trace.Write(DateTime.Now.ToString() + " ERROR ReInit: " + ex.Message);
                OnStart(null);
            }
        }

        private void TReCheck_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                if (!isconnected)
                {
                    OnStart(null);
                }
            }
            catch { }
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                HealthCheckMinutes = Properties.Settings.Default.HealtchCheckMinutes;
            }
            catch { }


            if (File.Exists("c:\\remove-me.txt"))
            {
                OFF(true);
                return;
            }

            //Check if OOBE or during Setup
            bool bSetupInProgress = false;
            try
            {
                var oWinSetup = Registry.LocalMachine.OpenSubKey("System\\Setup", false);
                if ((int)oWinSetup.GetValue("OOBEInProgress", 0) != 0)
                    bSetupInProgress = true;
                if ((int)oWinSetup.GetValue("SetupPhase", 0) != 0)
                    bSetupInProgress = true;
                if ((int)oWinSetup.GetValue("SetupType", 0) != 0)
                    bSetupInProgress = true;
            }
            catch { }

            isstopping = false;
            sScriptResult = DateTime.Now.ToString();
            tReCheck.Elapsed -= TReCheck_Elapsed;
            tReCheck.Elapsed += TReCheck_Elapsed;
            if (!bSetupInProgress)
            {
                tReCheck.Enabled = true;
                tReCheck.AutoReset = true;
            }
            else
            {
                tReCheck.Enabled = false;
                tReCheck.AutoReset = false;
                Properties.Settings.Default.HealthCheckSuccess = DateTime.Now;
                Properties.Settings.Default.InventorySuccess = DateTime.Now;
                Properties.Settings.Default.Save();
                Trace.WriteLine(DateTime.Now.ToString() + "\tWarning: Windows Setup is in progress... ");
            }

            tReInit.Elapsed -= TReInit_Elapsed;
            tReInit.Elapsed += TReInit_Elapsed;
            if (!bSetupInProgress)
            {
                tReInit.Enabled = true;
                tReInit.AutoReset = true;
            }
            else
            {
                tReInit.Enabled = false;
                tReInit.AutoReset = false;
            }

            try
            {
                if (string.IsNullOrEmpty(AzureLog.WorkspaceId))
                {
                    AzureLog = new DevCDRAgent.Modules.AzureLogAnalytics("DevCDR");
                }
            }
            catch { }

            try
            {
                //Register for Defender EventLogs
                watcher = new EventLogWatcher("Microsoft-Windows-Windows Defender/Operational");
                watcher.EventRecordWritten -= EventLogEventRead;
                watcher.EventRecordWritten += EventLogEventRead;
                watcher.Enabled = true;
            }
            catch { }

            if (connection != null)
            {
                try
                {
                 connection.DisposeAsync().Wait(1000);
                }
                catch { }
            }
            connection = new HubConnectionBuilder().WithUrl(Uri).Build();

            connection.Reconnected += async (error) =>
            {
                try
                {
                    isconnected = true;
                    Random rnd = new Random();
                    tReInit.Interval = 5000 + rnd.Next(1, 10000); //randomize ReInit intervall

                    if (string.IsNullOrEmpty(xAgent.Signature))
                        await connection.SendAsync("Init", Hostname);
                    else
                    {
                        await connection.SendAsync("InitCert", Hostname, xAgent.Signature);
                    }
                }
                catch {
                    isconnected = false;
                }
            };

            connection.Closed += async (error) =>
            {
                isconnected = false;
                if (!isstopping)
                {
                    try
                    {
                        Random rnd = new Random();
                        tReInit.Interval = 5000 + rnd.Next(1, 10000); //randomize ReInit intervall
                        Trace.WriteLine(DateTime.Now.ToString() + "\tWarning: Connection lost... " );
                        Trace.Flush();
                    }
                    catch { }
                    //try
                    //{
                    //    await Task.Delay(new Random().Next(0, 5) * 1000); // wait 0-5s
                    //    await connection.StartAsync();
                    //    isconnected = true;
                    //    Console.WriteLine("Connected with " + Uri);
                    //    Properties.Settings.Default.LastConnection = DateTime.Now;
                    //    Properties.Settings.Default.ConnectionErrors = 0;
                    //    Properties.Settings.Default.Save();
                    //    Properties.Settings.Default.Reload();
                    //    Connect();

                    //    //Send cached AV Threats
                    //    string sDir = Path.Combine(Environment.ExpandEnvironmentVariables("%TEMP%"), "AVDetection");
                    //    if(Directory.Exists(sDir))
                    //    {
                    //        foreach(string sFile in Directory.GetFiles(sDir, "*.json"))
                    //        {
                    //            try
                    //            {
                    //                if (isconnected)
                    //                {
                    //                    string sThreat = File.ReadAllText(sFile);
                    //                    AzureLog.Post(sThreat, "Defender");
                    //                    File.Delete(sFile);
                    //                }
                    //            }
                    //            catch { }
                    //        }
                    //    }
                    //}
                    //catch (Exception ex)
                    //{
                    //    isconnected = false;
                    //    Console.WriteLine(ex.Message);
                    //    Trace.WriteLine("\tError: " + ex.Message + " " + DateTime.Now.ToString());
                    //    Random rnd = new Random();
                    //    tReInit.Interval = 10000 + rnd.Next(1, 90000); //randomize ReInit intervall
                    //    Program.MinimizeFootprint();
                    //}
                }
                else
                {
                    return; //service is stopping...
                }
            };

            try
            {
                if (IsConnectedToInternet())
                {
                    if (connection.StartAsync().Wait(5000))
                    {
                        isconnected = true;
                        Console.WriteLine("Connected with " + Uri);
                        Trace.WriteLine(DateTime.Now.ToString() + "\tConnected with " + Uri);
                        Properties.Settings.Default.LastConnection = DateTime.Now;
                        Properties.Settings.Default.ConnectionErrors = 0;
                        Properties.Settings.Default.Save();
                        Properties.Settings.Default.Reload();

                        Task.Run(() => Connect());

                        //Send cached AV Threats
                        string sDir = Path.Combine(Environment.ExpandEnvironmentVariables("%TEMP%"), "AVDetection");
                        if (Directory.Exists(sDir))
                        {
                            foreach (string sFile in Directory.GetFiles(sDir, "*.json"))
                            {
                                try
                                {
                                    if (isconnected)
                                    {
                                        string sThreat = File.ReadAllText(sFile);
                                        AzureLog.Post(sThreat, "Defender");
                                        File.Delete(sFile);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    else
                    {
                        isconnected = false;
                        Properties.Settings.Default.ConnectionErrors++;
                        if (Properties.Settings.Default.ConnectionErrors > 50)
                            throw new Exception("Too many connection Errors...");
                    }
                }
                else
                {
                    isconnected = false;
                }
            }
            catch (Exception ex)
            {
                isconnected = false;
                Console.WriteLine(ex.Message);
                Trace.WriteLine(DateTime.Now.ToString() + "\tError: " + ex.Message + " " );

                Properties.Settings.Default.ConnectionErrors++;

                Trace.WriteLine(DateTime.Now.ToString() + "\tError Count: " + Properties.Settings.Default.ConnectionErrors.ToString() + " ");

                //Only fallback if we have internet...
                if (IsConnectedToInternet())
                {
                    if (Properties.Settings.Default.ConnectionErrors > 5)
                    {
                        string sDeviceID = Properties.Settings.Default.HardwareID;
                        if (string.IsNullOrEmpty(sDeviceID))
                        {
                            //Get DeviceID from PSStatus-Script
                            string sResult = "{}";
                            using (PowerShell PowerShellInstance = PowerShell.Create())
                            {
                                try
                                {
                                    PowerShellInstance.AddScript(Properties.Settings.Default.PSStatus);
                                    var PSResult = PowerShellInstance.Invoke();
                                    if (PSResult.Count() > 0)
                                    {
                                        sResult = PSResult.Last().BaseObject.ToString();
                                        sResult = sResult.Replace(Environment.MachineName, Hostname);
                                        JObject jRes = JObject.Parse(sResult);

                                        if (Properties.Settings.Default.HardwareID != jRes["id"].Value<string>())
                                        {
                                            Properties.Settings.Default.HardwareID = jRes["id"].Value<string>();
                                            Properties.Settings.Default.Save();
                                            Properties.Settings.Default.Reload();
                                            sDeviceID = jRes["id"].Value<string>();
                                        }
                                    }
                                }
                                catch (Exception er)
                                {
                                    Console.WriteLine(" There was an error: {0}", er.Message);
                                }
                            }
                        }

                        xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID);

                        if (!string.IsNullOrEmpty(xAgent.EndpointURL))
                        {
                            Uri = xAgent.EndpointURL;

                            if (Properties.Settings.Default.Endpoint != Uri)
                            {
                                Properties.Settings.Default.Endpoint = Uri;
                                Properties.Settings.Default.Save();
                            }
                        }
                    }
                    //Fallback to default endpoint after 1Days and 15 Errors
                    if (((DateTime.Now - Properties.Settings.Default.LastConnection).TotalDays >= 1) && (Properties.Settings.Default.ConnectionErrors >= 15))
                    {
                        if (!string.IsNullOrEmpty(xAgent.FallbackURL))
                            Uri = xAgent.FallbackURL;
                        else
                            Uri = Properties.Settings.Default.FallbackEndpoint;

                        Hostname = Environment.MachineName + "_BAD";
                    }
                }
                else
                {
                    //No Internet, lets ignore connection errors...
                    Properties.Settings.Default.ConnectionErrors = 0;
                    //Properties.Settings.Default.Save();
                    //Properties.Settings.Default.Reload();
                }

                Properties.Settings.Default.Save();
                Properties.Settings.Default.Reload();

                Random rnd = new Random();
                tReInit.Interval = 120000 + rnd.Next(1, 30000); //randomize ReInit intervall
                Program.MinimizeFootprint();
            }

            Task.Run(() => NamedPipeServer("devcdrsig"));
            Task.Run(() => NamedPipeServer("devcdrep"));
            Task.Run(() => NamedPipeServer("devcdrid"));
            Task.Run(() => NamedPipeServer("comcheck"));

            base.OnStart(args);
        }

        protected void OFF(bool uninstall = false)
        {
            //Stop status
            tReCheck.Enabled = false;
            tReInit.Enabled = false;
            isstopping = true;

            try
            {
                System.Environment.SetEnvironmentVariable("DevCDRSig", "", EnvironmentVariableTarget.Machine);
                System.Environment.SetEnvironmentVariable("DevCDREP", "", EnvironmentVariableTarget.Machine);
                System.Environment.SetEnvironmentVariable("DevCDRId", "", EnvironmentVariableTarget.Machine);
            }
            catch { }

            //Cleanup RuckZuck CustomerID
            try
            {
                var RZPol = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Policies\\RuckZuck", true);
                RZPol.DeleteValue("CustomerID", false);
                RZPol.DeleteValue("Broadcast", false);
            }
            catch { }

            //Cleanup ROMAWO policies and settings
            try
            {
                var RZPol = Registry.LocalMachine.OpenSubKey("SOFTWARE", true);
                RZPol.DeleteSubKeyTree("romawo", false);
            }
            catch { }

            try
            {
                //Trace.WriteLine(DateTime.Now.ToString() + "\t Set Customer: " + s1);
                //set Customer
                string sConfig = Assembly.GetExecutingAssembly().Location + ".config";
                XmlDocument doc = new XmlDocument();
                doc.Load(sConfig);
                doc.SelectSingleNode("/configuration/applicationSettings/DevCDRAgent.Properties.Settings/setting[@name='CustomerID']/value").InnerText = "";
                doc.Save(sConfig);
            }
            catch { }

            //Update Advanced Installer Persistent Properties
            try
            {
                RegistryKey myKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Zander Tools\\{54F5CC06-300A-4DD4-94D9-0E18B2BE8DF1}", true);
                if (myKey != null)
                {
                    myKey.DeleteValue("CUSTOMER");
                    myKey.Close();
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError(DateTime.Now.ToString() + "\t Error: " + ex.Message);
            }

            if(uninstall)
            {
                //Delete existing Settings
                try
                {
                    RegistryKey myKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Zander Tools", true);
                    myKey.DeleteSubKey("{54F5CC06-300A-4DD4-94D9-0E18B2BE8DF1}");
                }
                catch { }

                //Delete Scheduled Task
                try
                {
                    TimeSpan timeout = new TimeSpan(0, 5, 0); //default timeout = 5min
                    DateTime dStart = DateTime.Now;
                    TimeSpan dDuration = DateTime.Now - dStart;

                    using (PowerShell PowerShellInstance = PowerShell.Create())
                    {

                        Trace.WriteLine(DateTime.Now.ToString() + "\t remove Agent... ");
                        try
                        {
                            PowerShellInstance.AddScript("Get-ScheduledTask DevCDR | Unregister-ScheduledTask -Confirm:$False");
                            PSDataCollection <PSObject> outputCollection = new PSDataCollection<PSObject>();

                            outputCollection.DataAdding += ConsoleOutput;
                            PowerShellInstance.Streams.Error.DataAdding += ConsoleError;

                            IAsyncResult async = PowerShellInstance.BeginInvoke<PSObject, PSObject>(null, outputCollection);
                            while (async.IsCompleted == false && dDuration < timeout)
                            {
                                Thread.Sleep(200);
                                dDuration = DateTime.Now - dStart;
                            }

                            if (tReInit.Interval > 5000)
                                tReInit.Interval = 2000;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("There was an error: {0}", ex.Message);
                        }
                    }
                }
                catch { }

                //Stop and Uninstall
                try
                {
                    //string sParam = "sleep 3; iex (((Get-ItemProperty HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\* | ? { $_.DisplayName -eq 'DevCDRAgentCore'}).UninstallString).Replace('/X{', '/x ''{') + ' /qb'); remove-item c:\\remove-me.txt -force"; string sParam = "sleep 3; iex (((Get-ItemProperty HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\* | ? { $_.DisplayName -eq 'DevCDRAgentCore'}).UninstallString).Replace('/X{', '/x ''{') + ' /qb'); remove-item c:\\remove-me.txt -force";
                    File.Delete(@"c:\remove-me.txt");
                    //Process.Start(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\system32\WindowsPowerShell\v1.0\powershell.exe"), "-noexit -command sleep 5; &'c:\\Program Files\\DevCDRAgentCore\\DevCDRAgentCore.exe' --uninstall");

                    RegistryKey myKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall", false);
                    foreach (var subkey in myKey.GetSubKeyNames())
                    {
                        try
                        {
                            var oSub = myKey.OpenSubKey(subkey);
                            if (oSub.GetValue("DisplayName", "", RegistryValueOptions.None) as string == "DevCDRAgentCore")
                            {
                                string uninst = oSub.GetValue("UninstallString", "", RegistryValueOptions.None) as string;
                                Process.Start("msiexec", uninst.Split(' ')[1].Replace("/X{", "/x \"{") + "\" /qn");
                                break;
                            }
                        }
                        catch { }
                    }

                    Stop();
                }
                catch { }
            }
        }

        private void Connect()
        {
            try
            {
                connection.On<string, string>("returnPS", (s1, s2) =>
                {
                    lock (_locker)
                    {
                        TimeSpan timeout = new TimeSpan(0, 5, 0); //default timeout = 5min
                        DateTime dStart = DateTime.Now;
                        TimeSpan dDuration = DateTime.Now - dStart;

                        using (PowerShell PowerShellInstance = PowerShell.Create())
                        {
                            Trace.WriteLine(DateTime.Now.ToString() + "\t run PS... " + s1);
                            try
                            {
                                PowerShellInstance.AddScript(s1);
                                PSDataCollection<PSObject> outputCollection = new PSDataCollection<PSObject>();

                                outputCollection.DataAdding += ConsoleOutput;
                                PowerShellInstance.Streams.Error.DataAdding += ConsoleError;

                                IAsyncResult async = PowerShellInstance.BeginInvoke<PSObject, PSObject>(null, outputCollection);
                                while (async.IsCompleted == false && dDuration < timeout)
                                {
                                    Thread.Sleep(200);
                                    dDuration = DateTime.Now - dStart;
                                    //if (tReInit.Interval > 5000)
                                    //    tReInit.Interval = 2000;
                                }

                                if (tReInit.Interval > 5000)
                                    tReInit.Interval = 2000;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("There was an error: {0}", ex.Message);
                            }
                        }

                        Program.MinimizeFootprint();
                    }
                });

                //New 0.9.0.6
                connection.On<string, string>("returnPSAsync", (s1, s2) =>
                {
                    if ((DateTime.Now - tLastPSAsync).TotalSeconds >= 2)
                    {
                        lock (_locker)
                        {
                            Trace.WriteLine(DateTime.Now.ToString() + "\t run PS async... " + s1);
                            tLastPSAsync = DateTime.Now;
                            var tSWScan = Task.Run(() =>
                            {
                                using (PowerShell PowerShellInstance = PowerShell.Create())
                                {
                                    try
                                    {
                                        PowerShellInstance.AddScript(s1);
                                        var PSResult = PowerShellInstance.Invoke();
                                        if (PSResult.Count() > 0)
                                        {
                                            string sResult = PSResult.Last().BaseObject.ToString();

                                            if (!string.IsNullOrEmpty(sResult)) //Do not return empty results
                                            {
                                                if (sResult != sScriptResult)
                                                {
                                                    sScriptResult = sResult;
                                                    Random rnd = new Random();
                                                    tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max Xs to ReInit
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine("There was an error: {0}", ex.Message);
                                    }
                                }

                                Program.MinimizeFootprint();
                            });
                        }
                    }
                });

                connection.On<string>("init", (s1) =>
                {
                    try
                    {
                        Trace.Write(DateTime.Now.ToString() + "\t Agent init... ");
                        lock (_locker) //prevent parallel status
                        {
                            if (string.IsNullOrEmpty(xAgent.Signature))
                            {
                                connection.SendAsync("Init", Hostname).ContinueWith(task1 =>
                                {
                                });
                            }
                            else
                            {
                                connection.SendAsync("InitCert", Hostname, xAgent.Signature).ContinueWith(task1 =>
                                {
                                });
                            }
                        }
                        Trace.WriteLine(" done.");
                    }
                    catch { }
                    try
                    {
                        if (string.IsNullOrEmpty(xAgent.Signature))
                        {
                            foreach (string sGroup in Properties.Settings.Default.Groups.Split(';'))
                            {
                                connection.SendAsync("JoinGroup", sGroup).ContinueWith(task1 =>
                                {
                                });
                            }
                        }
                        else
                        {
                            connection.InvokeAsync("JoinGroupCert2", xAgent.Signature).ContinueWith(task2 =>
                            {
                            });
                        }

                        Program.MinimizeFootprint();
                    }
                    catch { }
                });

                connection.On<string>("reinit", (s1) =>
                {
                    try
                    {
                        //Properties.Settings.Default.InventorySuccess = new DateTime();
                        //Properties.Settings.Default.HealthCheckSuccess = new DateTime();
                        //Properties.Settings.Default.Save();

                        Random rnd = new Random();
                        tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                    }
                    catch { }
                });

                connection.On<string>("status", (s1) =>
                {
                    try
                    {
                        lock (_locker) //prevent parallel status
                        {
                            if (lConnectCount == 0)
                                tLastStatus = DateTime.Now;

                            lConnectCount++;

                            if ((DateTime.Now - tLastStatus).TotalSeconds <= 60)
                            {
                                if (lConnectCount >= 20) //max 20 status per minute
                                {
                                    Trace.Write(DateTime.Now.ToString() + "\t restarting service as Agent is looping...");
                                    RestartService();
                                    return;
                                }
                            }
                            else
                            {
                                tLastStatus = DateTime.Now;
                                lConnectCount = 0;
                            }

                            Trace.Write(DateTime.Now.ToString() + "\t send status...");
                            string sResult = "{}";

                            using (PowerShell PowerShellInstance = PowerShell.Create())
                            {
                                try
                                {
                                    PowerShellInstance.AddScript(Properties.Settings.Default.PSStatus);
                                    var PSResult = PowerShellInstance.Invoke();
                                    if (PSResult.Count() > 0)
                                    {
                                        sResult = PSResult.Last().BaseObject.ToString();
                                        sResult = sResult.Replace(Environment.MachineName, Hostname);
                                        JObject jRes = JObject.Parse(sResult);

                                        if (Properties.Settings.Default.HardwareID != jRes["id"].Value<string>())
                                        {
                                            Properties.Settings.Default.HardwareID = jRes["id"].Value<string>();
                                            Properties.Settings.Default.Save();
                                            Properties.Settings.Default.Reload();

                                            xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID);
                                        }

                                        jRes.Add("ScriptResult", sScriptResult);
                                        if (string.IsNullOrEmpty(xAgent.Signature))
                                            jRes.Add("Groups", Properties.Settings.Default.Groups);
                                        else
                                        {
                                            jRes.Add("Groups", xAgent.IssuingCA);
                                            jRes.Add("Customer", xAgent.CustomerID);
                                        }

                                        sResult = jRes.ToString();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(" There was an error: {0}", ex.Message);
                                    Trace.TraceError(DateTime.Now.ToString() + "\t ERROR..." + ex.Message);
                                }
                            }

                            if (string.IsNullOrEmpty(xAgent.Signature))
                                connection.InvokeAsync("Status", Hostname, sResult).Wait(1000);
                            else
                                connection.InvokeAsync("Status2", Hostname, sResult, xAgent.Signature).Wait(1000);
                            Trace.WriteLine(" done.");
                            Program.MinimizeFootprint();
                        }
                    }
                    catch (Exception ex)
                    {
                        lConnectCount++;
                        Trace.Write(DateTime.Now.ToString() + " ERROR: " + ex.Message);
                    }
                });

                connection.On<string>("version", (s1) =>
                {
                    try
                    {
                        lock (_locker)
                        {
                            Trace.Write(DateTime.Now.ToString() + "\t Get Version... ");
                            //Get File-Version
                            sScriptResult = (FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location)).FileVersion.ToString();
                            Trace.WriteLine(sScriptResult);

                            Random rnd = new Random();
                            tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                        }
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
                        //lock (_locker)
                        //{
                        //    if (!string.IsNullOrEmpty(s1))
                        //    {
                        //        string sConfig = Assembly.GetExecutingAssembly().Location + ".config";
                        //        XmlDocument doc = new XmlDocument();
                        //        doc.Load(sConfig);
                        //        doc.SelectSingleNode("/configuration/applicationSettings/DevCDRAgent.Properties.Settings/setting[@name='Instance']/value").InnerText = s1;
                        //        doc.Save(sConfig);


                        //        //Update Advanced Installer Persistent Properties
                        //        RegistryKey myKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Zander Tools\\{54F5CC06-300A-4DD4-94D9-0E18B2BE8DF1}", true);
                        //        if (myKey != null)
                        //        {
                        //            myKey.SetValue("INSTANCE", s1.Trim(), RegistryValueKind.String);
                        //            myKey.Close();
                        //        }

                        //        RestartService();
                        //    }
                        //}
                    }
                    catch { }
                });

                connection.On<string>("setcustomer", (s1) =>
                {
                    Trace.WriteLine(DateTime.Now.ToString() + "\t Set customer: " + s1);
                    try
                    {
                        lock (_locker)
                        {
                            if (!string.IsNullOrEmpty(s1))
                            {
                                //Stop status
                                tReCheck.Enabled = false;
                                tReInit.Enabled = false;

                                Trace.WriteLine(DateTime.Now.ToString() + "\t Set Customer: " + s1);
                                //set Customer
                                string sConfig = Assembly.GetExecutingAssembly().Location + ".config";
                                XmlDocument doc = new XmlDocument();
                                doc.Load(sConfig);
                                doc.SelectSingleNode("/configuration/applicationSettings/DevCDRAgent.Properties.Settings/setting[@name='CustomerID']/value").InnerText = s1;
                                doc.Save(sConfig);

                                //Update Advanced Installer Persistent Properties
                                try
                                {
                                    RegistryKey myKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Zander Tools\\{54F5CC06-300A-4DD4-94D9-0E18B2BE8DF1}", true);
                                    if (myKey != null)
                                    {
                                        myKey.SetValue("CUSTOMER", s1.Trim(), RegistryValueKind.String);
                                        myKey.Close();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Trace.TraceError(DateTime.Now.ToString() + "\t Error: " + ex.Message);
                                }

                                //remove all Certificates
                                try
                                {
                                    X509Store store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                                    store.Open(OpenFlags.ReadWrite);
                                    foreach (X509Certificate2 cert in store.Certificates.Find(X509FindType.FindBySubjectName, Properties.Settings.Default.HardwareID, false))
                                    {
                                        store.Remove(cert);
                                    }


                                    //remove IssuingCA
                                    store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                                    store.Open(OpenFlags.ReadWrite);
                                    foreach (X509Certificate2 cert in store.Certificates.Find(X509FindType.FindBySubjectName, xAgent.IssuingCA, false))
                                    {
                                        if (cert.Issuer.Split('=')[1] == "DeviceCommander")
                                            store.Remove(cert);
                                        if (cert.Issuer.Split('=')[1] == "ROMAWO")
                                            store.Remove(cert);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Trace.TraceError(DateTime.Now.ToString() + "\t Error: " + ex.Message);
                                }

                                Properties.Settings.Default.AgentSignature = "";
                                Properties.Settings.Default.HealthCheckSuccess = new DateTime();
                                Properties.Settings.Default.Save();

                                RestartService();
                            }
                        }
                    }
                    catch { }
                });

                connection.On<string>("setendpoint", (s1) =>
                {

                    try
                    {
                        lock (_locker)
                        {
                            if (!string.IsNullOrEmpty(s1))
                            {
                                if (s1.StartsWith("https://"))
                                {
                                    Trace.WriteLine(DateTime.Now.ToString() + "\t Set Endpoint: " + s1);
                                    Properties.Settings.Default.Endpoint = s1;
                                    Properties.Settings.Default.Save();

                                    //Update Advanced Installer Persistent Properties
                                    RegistryKey myKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Zander Tools\\{54F5CC06-300A-4DD4-94D9-0E18B2BE8DF1}", true);
                                    if (myKey != null)
                                    {
                                        myKey.SetValue("ENDPOINT", s1.Trim(), RegistryValueKind.String);
                                        myKey.Close();
                                    }

                                    RestartService();
                                }
                                else
                                {
                                    if (s1.ToUpper() == "OFF") //switch to legacy mode
                                    {
                                        OFF(false);
                                    }
                                    else
                                    {
                                        //Stop status
                                        tReCheck.Enabled = false;
                                        tReInit.Enabled = false;

                                        Trace.WriteLine(DateTime.Now.ToString() + "\t Set Customer: " + s1);
                                        //set Customer
                                        string sConfig = Assembly.GetExecutingAssembly().Location + ".config";
                                        XmlDocument doc = new XmlDocument();
                                        doc.Load(sConfig);
                                        doc.SelectSingleNode("/configuration/applicationSettings/DevCDRAgent.Properties.Settings/setting[@name='CustomerID']/value").InnerText = s1;
                                        doc.Save(sConfig);

                                        //Update Advanced Installer Persistent Properties
                                        try
                                        {
                                            RegistryKey myKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Zander Tools\\{54F5CC06-300A-4DD4-94D9-0E18B2BE8DF1}", true);
                                            if (myKey != null)
                                            {
                                                myKey.SetValue("CUSTOMER", s1.Trim(), RegistryValueKind.String);
                                                myKey.Close();
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Trace.TraceError(DateTime.Now.ToString() + "\t Error: " + ex.Message);
                                        }
                                    }

                                    //remove all Certificates
                                    try
                                    {
                                        X509Store store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                                        store.Open(OpenFlags.ReadWrite);
                                        foreach (X509Certificate2 cert in store.Certificates.Find(X509FindType.FindBySubjectName, Properties.Settings.Default.HardwareID, false))
                                        {
                                            store.Remove(cert);
                                        }


                                        //remove IssuingCA
                                        store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                                        store.Open(OpenFlags.ReadWrite);
                                        foreach (X509Certificate2 cert in store.Certificates.Find(X509FindType.FindBySubjectName, xAgent.IssuingCA, false))
                                        {
                                            try
                                            {
                                                if (cert.Issuer.Split('=')[1] == "DeviceCommander")
                                                    store.Remove(cert);
                                                if (cert.Issuer.Split('=')[1] == "ROMAWO")
                                                    store.Remove(cert);
                                            }
                                            catch { }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Trace.TraceError(DateTime.Now.ToString() + "\t Error: " + ex.Message);
                                    }

                                    Properties.Settings.Default.AgentSignature = "";
                                    Properties.Settings.Default.HealthCheckSuccess = new DateTime();
                                    Properties.Settings.Default.Save();

                                    RestartService();
                                }
                            }
                        }
                    }
                    catch { }
                });

                connection.On<string>("setgroups", (s1) =>
                {
                    Trace.WriteLine(DateTime.Now.ToString() + "\t Set Groups: " + s1);
                    try
                    {
                        lock (_locker)
                        {
                            if (!string.IsNullOrEmpty(s1))
                            {
                                try
                                {
                                    if (string.IsNullOrEmpty(xAgent.Signature))
                                    {
                                        string sConfig = Assembly.GetExecutingAssembly().Location + ".config";
                                        XmlDocument doc = new XmlDocument();
                                        doc.Load(sConfig);
                                        doc.SelectSingleNode("/configuration/applicationSettings/DevCDRAgent.Properties.Settings/setting[@name='Groups']/value").InnerText = s1;
                                        doc.Save(sConfig);

                                        //Update Advanced Installer Persistent Properties
                                        RegistryKey myKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Zander Tools\\{54F5CC06-300A-4DD4-94D9-0E18B2BE8DF1}", true);
                                        if (myKey != null)
                                        {
                                            myKey.SetValue("GROUPS", s1.Trim(), RegistryValueKind.String);
                                            myKey.Close();
                                        }
                                    }
                                }
                                catch { }

                                sScriptResult = "restart Agent...";
                                RestartService();
                            }
                            else
                            {
                                sScriptResult = "restart Agent...";
                                RestartService();
                            }
                        }
                    }
                    catch { }
                });

                connection.On<string>("getgroups", (s1) =>
                {
                    try
                    {
                        lock (_locker)
                        {
                            if (!string.IsNullOrEmpty(s1))
                            {
                                sScriptResult = Properties.Settings.Default.Groups;

                                Random rnd = new Random();
                                tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                            }
                        }
                    }
                    catch { }
                });

                connection.On<string>("restartservice", (s1) =>
                {
                    try
                    {
                        sScriptResult = "restart Agent...";
                        RestartService();
                    }
                    catch { }
                });

                connection.On<string>("rzinstall", (s1) =>
                {
                    RZInst(s1);
                });

                connection.On<string>("rzupdate", (s1) =>
                {
                    var tSWScan = Task.Run(() =>
                    {
                        try
                        {
                            sScriptResult = "Detecting RZ updates...";
                            Random rnd = new Random();
                            tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit

                            RZRestAPIv2.sURL = ""; //Enforce reloading RZ URL
                            RZUpdater oUpdate = new RZUpdater();
                            RZScan oScan = new RZScan(false, false);

                            lock (_locker)
                            {
                                oScan.GetSWRepository().Wait(60000);
                                oScan.SWScanAsync().Wait(60000);
                                oScan.CheckUpdatesAsync(null).Wait(60000);
                            }


                            if (string.IsNullOrEmpty(s1))
                            {
                                sScriptResult = oScan.NewSoftwareVersions.Count.ToString() + " RZ updates found";
                                rnd = new Random();
                                tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                            }

                            List<string> lSW = new List<string>();
                            foreach (var oSW in oScan.NewSoftwareVersions)
                            {
                                if (string.IsNullOrEmpty(s1) || s1 == "HUB")
                                {
                                    RZInst(oSW.ShortName);
                                }
                                else
                                {
                                    var SWList = s1.Split(';');
                                    if (SWList.Contains(oSW.ShortName))
                                        RZInst(oSW.ShortName);
                                }
                            }
                        }
                        catch { }
                    });
                });

                connection.On<string>("rzscan", (s1) =>
                {

                    var tSWScan = Task.Run(() =>
                    {

                        try
                        {
                            lock (_locker)
                            {
                                sScriptResult = "Detecting updates...";
                                Random rnd = new Random();
                                tReInit.Interval = rnd.Next(2000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit

                                RZRestAPIv2.sURL = ""; //Enforce reloading RZ URL
                                RZUpdater oUpdate = new RZUpdater();
                                RZScan oScan = new RZScan(false, false);


                                oScan.GetSWRepository().Wait(30000);
                                oScan.SWScanAsync().Wait(30000);
                                oScan.CheckUpdatesAsync(null).Wait(30000);


                                List<string> lSW = new List<string>();
                                foreach (var SW in oScan.NewSoftwareVersions)
                                {
                                    lSW.Add(SW.ShortName + " " + SW.ProductVersion + " (old:" + SW.MSIProductID + ")");
                                }

                                sScriptResult = JsonConvert.SerializeObject(lSW);
                                rnd = new Random();
                                tReInit.Interval = rnd.Next(2000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                            }
                        }
                        catch { }

                    });

                });

                connection.On<string>("inject", (s1) =>
                {
                    var tSWScan = Task.Run(() =>
                    {
                        try
                        {
                            sScriptResult = "Inject external code...";
                            try
                            {
                                ManagedInjection.Inject(s1);
                                sScriptResult = "External code executed.";
                            }
                            catch (Exception ex)
                            {
                                sScriptResult = "Injection error:" + ex.Message;
                            }
                        }
                        catch { }
                    });
                });

                connection.On<string, string>("userprocess", (cmd, arg) =>
                {
                    var tSWScan = Task.Run(() =>
                    {
                        if (string.IsNullOrEmpty(cmd))
                        {
                            cmd = Assembly.GetExecutingAssembly().Location;
                            arg = Environment.MachineName + ":" + "%USERNAME%";
                        }

                        try
                        {
                            if (string.IsNullOrEmpty(arg))
                            {
                                ProcessExtensions.StartProcessAsCurrentUser(cmd, null, null, false);
                            }
                            else
                            {
                                ProcessExtensions.StartProcessAsCurrentUser(null, cmd + " " + arg, null, false);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }
                    });

                });

                connection.On<string>("setAgentSignature", (s1) =>
                {
                    lock (_locker)
                    {
                        Trace.WriteLine(DateTime.Now.ToString() + "\t Set AgentSignature " + s1);
                        try
                        {
                            if (string.IsNullOrEmpty(Properties.Settings.Default.CustomerID) && !string.IsNullOrEmpty(s1))
                            {
                                //Properties.Settings.Default.CustomerID = s1.Trim();
                                Trace.WriteLine(DateTime.Now.ToString() + "\t New CustomerID ?! " + s1.Trim());
                            }

                            if (!string.IsNullOrEmpty(Properties.Settings.Default.CustomerID)) //CustomerID is required !!!
                            {
                                if (!string.IsNullOrEmpty(Properties.Settings.Default.HardwareID))
                                {
                                    xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID);

                                    if (xAgent.Certificate == null)
                                    {
                                        //request machine cert...
                                        if ((DateTime.Now - tLastCertReq).TotalMinutes >= 5)
                                        {
                                            Trace.WriteLine(DateTime.Now.ToString() + "\t Requesting Machine Certificate... ");
                                            connection.InvokeAsync("GetMachineCert", Properties.Settings.Default.CustomerID.Trim(), Properties.Settings.Default.HardwareID).Wait(30000); //MachineCert
                                            tLastCertReq = DateTime.Now;
                                            return;
                                            //xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID);
                                        }
                                        else
                                        {
                                            Thread.Sleep(5000);
                                            xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID);
                                        }
                                    }

                                    if (xAgent.Certificate != null)
                                    {
                                        if (xAgent.Exists && xAgent.Valid && xAgent.HasPrivateKey && !string.IsNullOrEmpty(xAgent.Signature))
                                        {
                                            //If the root has changed...
                                            //if (string.IsNullOrEmpty(xAgent.RootCA))
                                            //{
                                            //    Trace.WriteLine(DateTime.Now.ToString() + "\t Requesting Public Key for:" + Properties.Settings.Default.RootCA);
                                            //    connection.InvokeAsync("GetCert", Properties.Settings.Default.RootCA, false).Wait(5000); //request root cert
                                            //    Thread.Sleep(2500);
                                            //}

                                            //If the IssuingCA has changed...
                                            if (string.IsNullOrEmpty(xAgent.IssuingCA))
                                            {
                                                Trace.WriteLine(DateTime.Now.ToString() + "\t Requesting Public Key for:" + xAgent.Certificate.Issuer.Split('=')[1]);
                                                connection.InvokeAsync("GetCert", xAgent.Certificate.Issuer.Split('=')[1], false).Wait(5000); //request issuer cert
                                                Thread.Sleep(2500);
                                            }

                                            xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID);

                                            if (!string.IsNullOrEmpty(xAgent.EndpointURL))
                                            {
                                                if (Uri != xAgent.EndpointURL)
                                                {
                                                    Uri = xAgent.EndpointURL;
                                                    Trace.WriteLine(DateTime.Now.ToString() + "\t Endpoint URL to:" + xAgent.EndpointURL);
                                                    Properties.Settings.Default.Endpoint = xAgent.EndpointURL;
                                                    Properties.Settings.Default.Save();
                                                }
                                            }

                                            if (!string.IsNullOrEmpty(xAgent.FallbackURL))
                                            {
                                                if (Properties.Settings.Default.FallbackEndpoint != xAgent.FallbackURL)
                                                {
                                                    Trace.WriteLine(DateTime.Now.ToString() + "\t Fallback URL to:" + xAgent.FallbackURL);
                                                    Properties.Settings.Default.FallbackEndpoint = xAgent.FallbackURL;
                                                    Properties.Settings.Default.Save();
                                                }
                                            }

                                            if (Properties.Settings.Default.AgentSignature != xAgent.Signature)
                                            {
                                                Trace.WriteLine(DateTime.Now.ToString() + "\t Updating Signature... ");
                                                Properties.Settings.Default.AgentSignature = xAgent.Signature;
                                                Properties.Settings.Default.Save();
                                            }
                                        }
                                        else
                                        {
                                            if (xAgent.Expired)
                                            {
                                                //request machine cert...
                                                if ((DateTime.Now - tLastCertReq).TotalMinutes >= 5)
                                                {
                                                    Trace.WriteLine(DateTime.Now.ToString() + "\t Requesting Machine Certificate... ");
                                                    connection.InvokeAsync("GetMachineCert", Properties.Settings.Default.CustomerID.Trim(), Properties.Settings.Default.HardwareID).Wait(30000); //MachineCert
                                                    tLastCertReq = DateTime.Now;
                                                    //xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID);
                                                    return;
                                                    
                                                }

                                                if (!string.IsNullOrEmpty(xAgent.Signature))
                                                {
                                                    Properties.Settings.Default.AgentSignature = xAgent.Signature;
                                                    Properties.Settings.Default.Save();
                                                }
                                            }
                                            try
                                            {
                                                //if (string.IsNullOrEmpty(xAgent.RootCA))
                                                //{
                                                //    Trace.WriteLine(DateTime.Now.ToString() + "\t Requesting Public Key for: " + Properties.Settings.Default.RootCA);
                                                //    connection.InvokeAsync("GetCert", Properties.Settings.Default.RootCA, false).Wait(5000); //request root cert
                                                //    Thread.Sleep(2500);
                                                //}

                                                if (string.IsNullOrEmpty(xAgent.IssuingCA) || string.IsNullOrEmpty(xAgent.RootCA))
                                                {
                                                    Trace.WriteLine(DateTime.Now.ToString() + "\t Requesting Public Key for: " + xAgent.Certificate.Issuer.Split('=')[1]);
                                                    connection.InvokeAsync("GetCert", xAgent.Certificate.Issuer.Split('=')[1], false).Wait(5000); //request issuer cert
                                                    Thread.Sleep(2500);
                                                }
                                            }
                                            catch { }

                                            if (!xAgent.Valid)
                                            {
                                                Trace.WriteLine(DateTime.Now.ToString() + "\t Clearing Signature... ");
                                                Properties.Settings.Default.AgentSignature = "";
                                                Properties.Settings.Default.Save();
                                            }
                                        }
                                    }
                                    else
                                    {
                                        //request machine cert...
                                        Thread.Sleep(2000);
                                        if ((DateTime.Now - tLastCertReq).TotalMinutes >= 5)
                                        {
                                            Trace.WriteLine(DateTime.Now.ToString() + "\t Requesting Machine Certificate... ");
                                            connection.InvokeAsync("GetMachineCert", Properties.Settings.Default.CustomerID.Trim(), Properties.Settings.Default.HardwareID).Wait(30000); //MachineCert
                                            tLastCertReq = DateTime.Now;
                                            return;
                                        }
                                    }

                                    xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID);

                                    if (xAgent.Exists && xAgent.Valid)
                                    {
                                        if (!string.IsNullOrEmpty(xAgent.Signature))
                                        {
                                            if (Properties.Settings.Default.AgentSignature != xAgent.Signature)
                                            {
                                                Properties.Settings.Default.AgentSignature = xAgent.Signature;
                                                Properties.Settings.Default.Endpoint = xAgent.EndpointURL;
                                                Properties.Settings.Default.Save();
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Properties.Settings.Default.AgentSignature = "";
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(AzureLog.WorkspaceId))
                            {
                                JObject jLog = new JObject();
                                jLog.Add(new JProperty("Computer", Environment.MachineName));
                                jLog.Add(new JProperty("EventID", 0100));
                                jLog.Add(new JProperty("Description", "EndpointURL:" + xAgent.EndpointURL));
                                jLog.Add(new JProperty("CustomerID", xAgent.CustomerID));

                                AzureLog.Post(jLog.ToString());
                            }

                            Random rnd = new Random();
                            tReInit.Interval = rnd.Next(2000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                        }
                        catch (Exception ex)
                        {
                            Trace.TraceError(DateTime.Now.ToString() + "\t ERROR 1296: " + ex.Message);

                            if (!string.IsNullOrEmpty(AzureLog.WorkspaceId))
                            {
                                JObject jLog = new JObject();
                                jLog.Add(new JProperty("Computer", Environment.MachineName));
                                jLog.Add(new JProperty("EventID", 9100));
                                jLog.Add(new JProperty("Description", "setAgentSignature Error:" + ex.Message));
                                jLog.Add(new JProperty("CustomerID", xAgent.CustomerID ?? ""));

                                AzureLog.Post(jLog.ToString());
                            }
                        }
                    }
                });

                connection.On<string>("setCert", (s1) =>
                {
                    if (s1.Length > 512)
                    {
                        Trace.WriteLine(DateTime.Now.ToString() + "\t Certificate received... ");
                    }
                    else
                    {
                        Trace.WriteLine(DateTime.Now.ToString() + "\t invalid Certificate: " + s1);
                    }
                    //Trace.WriteLine(DateTime.Now.ToString() + "\t Cert:" + s1);
                    if (!string.IsNullOrEmpty(s1))
                    {
                        X509Certificate2 cert = new X509Certificate2();
                        try
                        {
                            cert = new X509Certificate2(Convert.FromBase64String(s1));
                        }
                        catch
                        {
                            try
                            {
                                cert = new X509Certificate2(Convert.FromBase64String(s1), Properties.Settings.Default.HardwareID, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                            }
                            catch { }
                        }

                        if (cert.HasPrivateKey)
                        {
                            SignatureVerification.addCertToStore(cert, StoreName.My, StoreLocation.LocalMachine);
                            Trace.WriteLine(DateTime.Now.ToString() + "\t Machine Certificate Installed... ");
                            if(!cert.Verify())
                            {
                                try
                                {
                                    Trace.WriteLine(DateTime.Now.ToString() + "\t Requesting Public Key for:" + cert.Issuer.Split('=')[1]);
                                    connection.InvokeAsync("GetCert", cert.Issuer.Split('=')[1], false).Wait(1000); //request root cert
                                    Thread.Sleep(1500);
                                }
                                catch { }
                            }
                        }
                        else
                        {
                            SignatureVerification.addCertToStore(cert, StoreName.Root, StoreLocation.LocalMachine);
                            if (!cert.Verify())
                            {
                                try
                                {
                                    Trace.WriteLine(DateTime.Now.ToString() + "\t Requesting Public Key for:" + cert.Issuer.Split('=')[1]);
                                    connection.InvokeAsync("GetCert", cert.Issuer.Split('=')[1], false).Wait(1000); //request root cert
                                    Thread.Sleep(1500);
                                }
                                catch { }

                            }
                        }

                        xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID);

                        if (xAgent.Exists && xAgent.Valid)
                        {
                            if (!string.IsNullOrEmpty(xAgent.Signature))
                            {
                                if (Properties.Settings.Default.AgentSignature != xAgent.Signature)
                                {
                                    Trace.WriteLine(DateTime.Now.ToString() + "\t Updating Agent Signature and URL... ");
                                    Properties.Settings.Default.AgentSignature = xAgent.Signature;
                                    Properties.Settings.Default.HealthCheckSuccess = new DateTime(); //reset Health check
                                    Properties.Settings.Default.Endpoint = xAgent.EndpointURL;

                                    try
                                    {
                                        //Update Advanced Installer Persistent Properties
                                        RegistryKey myKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Zander Tools\\{54F5CC06-300A-4DD4-94D9-0E18B2BE8DF1}", true);
                                        if (myKey != null)
                                        {
                                            myKey.SetValue("ENDPOINT", xAgent.EndpointURL, RegistryValueKind.String);
                                            myKey.Close();
                                        }
                                    }
                                    catch { }

                                    Properties.Settings.Default.Save();
                                    Thread.Sleep(1000);
                                    RestartService();
                                }
                            }
                        }

                        Random rnd = new Random();
                        tReInit.Interval = rnd.Next(5000, Properties.Settings.Default.StatusDelay * 2); //wait max 10s to ReInit
                    } 
                    else
                    {
                        //no valid cert received
                        Hostname = Environment.MachineName + "_NOCERT";
                        Trace.WriteLine(DateTime.Now.ToString() + "\t Certificate missing... Starting legacy mode.");
                        Properties.Settings.Default.AgentSignature = xAgent.Signature;
                        Properties.Settings.Default.Save();
                        //Legacy Init
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
                                        foreach (string sGroup in Properties.Settings.Default.Groups.Split(';'))
                                        {
                                            connection.InvokeAsync("JoinGroup", sGroup).ContinueWith(task2 =>
                                            {
                                            });
                                        }
                                        Program.MinimizeFootprint();
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        });

                    }
                });

                connection.On<string>("checkInventoryAsync", (s1) =>
                {
                    if (string.IsNullOrEmpty(xAgent.Signature))
                    {
                        Trace.WriteLine(DateTime.Now.ToString() + "\t starting Inventory...");
                        connection.SendAsync("Inventory", Hostname);
                    }
                    else
                    {
                        Trace.WriteLine(DateTime.Now.ToString() + "\t run inventory (cert) ... ");
                        tLastPSAsync = DateTime.Now;
                        xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID);
                        string sEndPoint = xAgent.EndpointURL.Replace("/chat", "");
                        string sCommand = "";

                        //recommended way
                        using (HttpClient oWebClient = new HttpClient())
                        {
                            sCommand = oWebClient.GetStringAsync(sEndPoint + "/devcdr/getfile?filename=inventory2.ps1&signature=" + xAgent.Signature).Result;
                        }
                        //alternative way
                        if (string.IsNullOrEmpty(sCommand))
                        {
                            Trace.WriteLine(DateTime.Now.ToString() + "\t unable to get inventory2.ps1... ");
                            sCommand = "Invoke-RestMethod -Uri '" + sEndPoint + "/devcdr/getfile?filename=inventory2.ps1&signature=" + xAgent.Signature + "' | IEX";
                        }

                        var tSWScan = Task.Run(() =>
                        {
                            using (PowerShell PowerShellInstance = PowerShell.Create())
                            {
                                try
                                {
                                    PowerShellInstance.AddScript(sCommand);
                                    var PSResult = PowerShellInstance.Invoke();
                                    if (PSResult.Count() > 0)
                                    {
                                        string sResult2 = PSResult.Last().BaseObject.ToString();

                                        if (!string.IsNullOrEmpty(sResult2)) //Do not return empty results
                                        {
                                            using (HttpClient oWebClient = new HttpClient())
                                            {
                                                HttpContent oCont = new StringContent(sResult2);
                                                var sRes = oWebClient.PostAsync(sEndPoint + "/devcdr/PutFile?signature=" + xAgent.Signature, oCont).Result;
                                                Trace.WriteLine(DateTime.Now.ToString() + "\t PutFile Status code: " + sRes.StatusCode.ToString());
                                                //Trace.WriteLine(DateTime.Now.ToString() + "\t URL: " + sEndPoint + "/devcdr/PutFile?signature=" + xAgent.Signature);
                                            }

                                            sScriptResult = "Inventory completed...";
                                            Random rnd2 = new Random();
                                            tReInit.Interval = rnd2.Next(200, Properties.Settings.Default.StatusDelay); //wait max Xs to ReInit
                                            Properties.Settings.Default.InventorySuccess = DateTime.Now;
                                            Trace.WriteLine(DateTime.Now.ToString() + "\t Inventory completed.");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("There was an error: {0}", ex.Message);
                                }
                            }
                        });
                    }
                });

                connection.On<string>("checkComplianceAsync", (s1) =>
                {
                    if (string.IsNullOrEmpty(xAgent.Signature))
                    {
                        Trace.WriteLine(DateTime.Now.ToString() + " starting HealthCheck...");
                        connection.SendAsync("HealthCheck", Hostname);
                    }
                    else
                    {
                        //Update PowerShell Module
                        if (!string.IsNullOrEmpty(xAgent.Signature))
                        {
                            try
                            {
                                string sEP = xAgent.EndpointURL.Replace("/chat", "");
                                string sModule;
                                using (var wc = new System.Net.WebClient())
                                    sModule = wc.DownloadString(sEP + "/devcdr/getfile?filename=compliance.psm1&signature=" + xAgent.Signature);

                                if (!Directory.Exists(Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\WindowsPowerShell\Modules\Compliance")))
                                {
                                    Directory.CreateDirectory(Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\WindowsPowerShell\Modules\Compliance"));
                                }

                                File.WriteAllText(Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\WindowsPowerShell\Modules\Compliance\compliance.psm1"), sModule, new UTF8Encoding(true));
                            }
                            catch (Exception ex)
                            {
                                Trace.TraceError(DateTime.Now.ToString() + "\t" + ex.Message);
                            }
                        }

                        Trace.WriteLine(DateTime.Now.ToString() + "\t run compliance (cert) check... ");
                        tLastPSAsync = DateTime.Now;
                        xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID);
                        string sEndPoint = xAgent.EndpointURL.Replace("/chat", "");
                        string sCommand = "Invoke-RestMethod -Uri '" + sEndPoint + "/devcdr/getfile?filename=compliance.ps1&signature=" + xAgent.Signature + "' | IEX;";

                        var tSWScan = Task.Run(() =>
                        {
                            using (PowerShell PowerShellInstance = PowerShell.Create())
                            {
                                try
                                {
                                    PowerShellInstance.AddScript(sCommand);
                                    var PSResult = PowerShellInstance.Invoke();
                                    if (PSResult.Count() > 0)
                                    {
                                        string sResult2 = PSResult.Last().BaseObject.ToString();

                                        if (!string.IsNullOrEmpty(sResult2)) //Do not return empty results
                                        {
                                            sScriptResult = "Compliance check completed";
                                            Properties.Settings.Default.HealthCheckSuccess = DateTime.Now;

                                            connection.InvokeAsync("UpdateComplianceCert2", sResult2, xAgent.Signature).Wait(2000);
                                            Trace.WriteLine(DateTime.Now.ToString() + "\t compliance check completed.");

                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("There was an error: {0}", ex.Message);
                                }
                            }
                        });
                    }
                });

                //Get HardwareID
                if (string.IsNullOrEmpty(Properties.Settings.Default.HardwareID))
                {
                    lock (_locker)
                    {
                        //Get DeviceID from PSStatus-Script
                        string sResult = "{}";
                        using (PowerShell PowerShellInstance = PowerShell.Create())
                        {
                            try
                            {
                                PowerShellInstance.AddScript(Properties.Settings.Default.PSStatus);
                                var PSResult = PowerShellInstance.Invoke();
                                if (PSResult.Count() > 0)
                                {
                                    sResult = PSResult.Last().BaseObject.ToString();
                                    sResult = sResult.Replace(Environment.MachineName, Hostname);
                                    JObject jRes = JObject.Parse(sResult);

                                    if (Properties.Settings.Default.HardwareID != jRes["id"].Value<string>())
                                    {
                                        Properties.Settings.Default.HardwareID = jRes["id"].Value<string>();
                                        Properties.Settings.Default.Save();
                                        Properties.Settings.Default.Reload();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(" There was an error: {0}", ex.Message);
                            }
                        }
                    }
                }

                xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID.Trim());

                if (string.IsNullOrEmpty(Properties.Settings.Default.CustomerID.Trim()))
                {
                    if (xAgent.Exists && xAgent.Valid && !string.IsNullOrEmpty(xAgent.Signature))
                    {
                        Properties.Settings.Default.AgentSignature = xAgent.Signature;
                        Hostname = Environment.MachineName + "_ORPHANED";
                    }
                }

                //initial initialization...
                if (!string.IsNullOrEmpty(Properties.Settings.Default.CustomerID.Trim())) //CustomerID is required !!!
                {
                    if (!string.IsNullOrEmpty(Properties.Settings.Default.HardwareID))
                    {
                        xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID.Trim());

                        if (xAgent.Certificate == null)
                        {
                            //request machine cert...
                            if ((DateTime.Now - tLastCertReq).TotalMinutes >= 2)
                            {
                                Thread.Sleep(2000);
                                Trace.WriteLine(DateTime.Now.ToString() + "\t Requesting Machine Certificate.... ");
                                Trace.WriteLine(DateTime.Now.ToString() + "\t CustomerID: " + Properties.Settings.Default.CustomerID.Trim());
                                Trace.WriteLine(DateTime.Now.ToString() + "\t DeviceID: " + Properties.Settings.Default.HardwareID);
                                connection.InvokeAsync("GetMachineCert", Properties.Settings.Default.CustomerID.Trim(), Properties.Settings.Default.HardwareID).Wait(30000); //MachineCert
                                tLastCertReq = DateTime.Now;
                                Trace.WriteLine(DateTime.Now.ToString() + "\t Machine Certificate requested.... ");
                                Thread.Sleep(30000);
                            }
                            else
                            {
                                Trace.WriteLine(DateTime.Now.ToString() + "\t Requesting Machine Certificate skipped.... ");
                            }
                            Thread.Sleep(5000);
                            xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID.Trim());

                            if (xAgent.Exists && xAgent.Valid)
                            {
                                if (!string.IsNullOrEmpty(xAgent.Signature))
                                {
                                    if (Properties.Settings.Default.AgentSignature != xAgent.Signature)
                                    {
                                        Properties.Settings.Default.AgentSignature = xAgent.Signature;
                                        Properties.Settings.Default.Endpoint = xAgent.EndpointURL;
                                        Properties.Settings.Default.Save();
                                    }
                                }
                            }
                        }

                        if (xAgent.Certificate != null && !xAgent.Expired)
                        {
                            if (xAgent.Exists && xAgent.Valid && xAgent.HasPrivateKey && !string.IsNullOrEmpty(xAgent.Signature))
                            {
                                if (Properties.Settings.Default.AgentSignature != xAgent.Signature)
                                {
                                    lock (_locker)
                                    {
                                        Trace.WriteLine(DateTime.Now.ToString() + "\t Updating Signature... ");
                                        Properties.Settings.Default.AgentSignature = xAgent.Signature;
                                        Properties.Settings.Default.Save();
                                        Properties.Settings.Default.Reload();
                                    }
                                }

                                if (!string.IsNullOrEmpty(xAgent.EndpointURL))
                                {
                                    if (Uri != xAgent.EndpointURL)
                                    {
                                        Uri = xAgent.EndpointURL;
                                        Trace.WriteLine(DateTime.Now.ToString() + "\t Endpoint URL to:" + xAgent.EndpointURL);
                                        Properties.Settings.Default.Endpoint = xAgent.EndpointURL;
                                        Properties.Settings.Default.Save();
                                    }
                                }

                                if (!string.IsNullOrEmpty(xAgent.FallbackURL))
                                {
                                    if (Properties.Settings.Default.FallbackEndpoint != xAgent.FallbackURL)
                                    {
                                        Trace.WriteLine(DateTime.Now.ToString() + "\t Fallback URL to:" + xAgent.FallbackURL);
                                        Properties.Settings.Default.FallbackEndpoint = xAgent.FallbackURL;
                                        Properties.Settings.Default.Save();
                                    }
                                }
                            }
                            else
                            {
                                try
                                {
                                    Trace.WriteLine(DateTime.Now.ToString() + "\t Certificate is valid:" + xAgent.Valid.ToString());
                                    Trace.WriteLine(DateTime.Now.ToString() + "\t Certificate has PrivateKey:" + xAgent.HasPrivateKey.ToString());
                                    Trace.WriteLine(DateTime.Now.ToString() + "\t Signature:" + xAgent.Signature);
                                    Trace.WriteLine(DateTime.Now.ToString() + "\t Requesting Public Key for:" + xAgent.Certificate.Issuer.Split('=')[1]);
                                    connection.InvokeAsync("GetCert", xAgent.Certificate.Issuer.Split('=')[1], false).Wait(1000); //request issuer cert
                                    Thread.Sleep(1500);

                                    //Trace.WriteLine(DateTime.Now.ToString() + "\t Requesting Public Key for:" + Properties.Settings.Default.RootCA);
                                    //connection.InvokeAsync("GetCert", Properties.Settings.Default.RootCA, false).Wait(1000); //request root cert
                                    //Thread.Sleep(1500);
                                }
                                catch (Exception ex)
                                {
                                    Trace.TraceError(DateTime.Now.ToString() + "\t Error: " + ex.Message);
                                }

                                Trace.WriteLine(DateTime.Now.ToString() + "\t Clearing Signature... ");
                                Properties.Settings.Default.AgentSignature = "";
                                Properties.Settings.Default.Save();
                            }
                        }
                        else
                        {
                            //request machine cert...
                            //Thread.Sleep(5000);
                            if ((DateTime.Now - tLastCertReq).TotalMinutes >= 5)
                            {
                                Trace.WriteLine(DateTime.Now.ToString() + "\t Requesting Machine Certificate... ");
                                connection.InvokeAsync("GetMachineCert", Properties.Settings.Default.CustomerID.Trim(), Properties.Settings.Default.HardwareID).Wait(30000); //MachineCert
                                tLastCertReq = DateTime.Now;
                                return;
                            }

                            xAgent = new X509AgentCert(Properties.Settings.Default.HardwareID, Properties.Settings.Default.CustomerID.Trim());

                            if (xAgent.Exists && xAgent.Valid)
                            {
                                if (!string.IsNullOrEmpty(xAgent.Signature))
                                {
                                    if (Properties.Settings.Default.AgentSignature != xAgent.Signature)
                                    {
                                        Properties.Settings.Default.AgentSignature = xAgent.Signature;
                                        Properties.Settings.Default.Endpoint = xAgent.EndpointURL;
                                        Properties.Settings.Default.Save();
                                    }
                                }
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(Properties.Settings.Default.AgentSignature.Trim() + Properties.Settings.Default.CustomerID.Trim()))
                {
                    Trace.WriteLine(DateTime.Now.ToString() + "\t AgentSignature and CustomerID missing... Starting legacy mode.");
                    Console.WriteLine("AgentSignature and CustomerID missing... Starting legacy mode.");

                    //Legacy Init
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
                                    foreach (string sGroup in Properties.Settings.Default.Groups.Split(';'))
                                    {
                                        connection.InvokeAsync("JoinGroup", sGroup).ContinueWith(task2 =>
                                        {
                                        });
                                    }
                                    Program.MinimizeFootprint();
                                }
                                catch { }
                            }
                        }
                        catch { }
                    });

                    return;
                }

                if (string.IsNullOrEmpty(Properties.Settings.Default.AgentSignature.Trim()))
                {
                    Trace.WriteLine(DateTime.Now.ToString() + "\t AgentSignature missing... Initializing Certificate handshake.");
                    Console.WriteLine("AgentSignature missing... Initializing Certificate handshake.");
                    connection.InvokeAsync("InitCert", Hostname, Properties.Settings.Default.AgentSignature); //request root and issuing cert
                }
                else
                {
                    Console.WriteLine("AgentSignature exists... Starting Signature verification.");
                    Trace.WriteLine(DateTime.Now.ToString() + "\t AgentSignature exists... Starting Signature verification.");
                    connection.InvokeAsync("InitCert", Hostname, xAgent.Signature).ContinueWith(task1 =>
                    {
                        //try
                        //{
                        //    if (task1.IsFaulted)
                        //    {
                        //        Trace.WriteLine($"There was an error calling send: {task1.Exception.GetBaseException()}");
                        //        Console.WriteLine("There was an error calling send: {0}", task1.Exception.GetBaseException());
                        //    }
                        //    else
                        //    {
                        //        try
                        //        {
                        //            Trace.WriteLine(DateTime.Now.ToString() + "\t JoiningGroup...");
                        //            connection.InvokeAsync("JoinGroupCert2", xAgent.Signature).ContinueWith(task2 =>
                        //            {
                        //            });

                        //            Program.MinimizeFootprint();
                        //        }
                        //        catch { }
                        //    }
                        //}
                        //catch { }
                    });
                }

                //Update PowerShell Module
                if (!string.IsNullOrEmpty(xAgent.Signature))
                {
                    try
                    {
                        string sEndPoint = xAgent.EndpointURL.Replace("/chat", "");
                        string sModule;
                        using (var wc = new System.Net.WebClient())
                            sModule = wc.DownloadString(sEndPoint + "/devcdr/getfile?filename=compliance.psm1&signature=" + xAgent.Signature);

                        if (!Directory.Exists(Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\WindowsPowerShell\Modules\Compliance")))
                        {
                            Directory.CreateDirectory(Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\WindowsPowerShell\Modules\Compliance"));
                        }

                        File.WriteAllText(Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\WindowsPowerShell\Modules\Compliance\compliance.psm1"), sModule, new UTF8Encoding(true));
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError(DateTime.Now.ToString() + "\t" + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("There was an error: {0}", ex.Message);
            }
        }

        public void RZInst(string s1)
        {
            try
            {
                Random rnd = new Random();
                RZRestAPIv2.sURL = ""; //Enforce reloading RZ URL
                RZUpdater oRZSW = new RZUpdater();
                oRZSW.SoftwareUpdate = new SWUpdate(s1);
                
                if (string.IsNullOrEmpty(oRZSW.SoftwareUpdate.SW.ProductName))
                {
                    sScriptResult = "'" + s1 + "' is NOT available in RuckZuck...!";
                    rnd = new Random();
                    tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                }

                if(string.IsNullOrEmpty(oRZSW.SoftwareUpdate.SW.PSInstall))
                {
                    oRZSW.SoftwareUpdate.GetInstallType();
                }

                foreach (string sPreReq in oRZSW.SoftwareUpdate.SW.PreRequisites)
                {
                    if (!string.IsNullOrEmpty(sPreReq))
                    {
                        RZUpdater oRZSWPreReq = new RZUpdater();
                        oRZSWPreReq.SoftwareUpdate = new SWUpdate(sPreReq);

                        sScriptResult = "..downloading dependencies (" + oRZSWPreReq.SoftwareUpdate.SW.ShortName + ")";
                        rnd = new Random();
                        tReInit.Interval = rnd.Next(2000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit

                        if (oRZSWPreReq.SoftwareUpdate.Download().Result)
                        {
                            sScriptResult = "..installing dependencies (" + oRZSWPreReq.SoftwareUpdate.SW.ShortName + ")";
                            rnd = new Random();
                            tReInit.Interval = rnd.Next(2000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit

                            if (oRZSWPreReq.SoftwareUpdate.Install(false, true).Result)
                            {
                                if (!string.IsNullOrEmpty(AzureLog.WorkspaceId))
                                {
                                    JObject jLog = new JObject();
                                    jLog.Add(new JProperty("Computer", Environment.MachineName));
                                    jLog.Add(new JProperty("EventID", 2000));
                                    jLog.Add(new JProperty("Description", "RuckZuck updating:" + oRZSWPreReq.SoftwareUpdate.SW.ShortName));
                                    jLog.Add(new JProperty("CustomerID", xAgent.CustomerID));

                                    AzureLog.Post(jLog.ToString());
                                }
                            }
                            else
                            {
                                sScriptResult = oRZSWPreReq.SoftwareUpdate.SW.ShortName + " failed.";
                                rnd = new Random();
                                tReInit.Interval = rnd.Next(2000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                            }
                        }
                    }

                }

                sScriptResult = "..downloading " + oRZSW.SoftwareUpdate.SW.ShortName;
                rnd = new Random();
                tReInit.Interval = rnd.Next(2000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit

                if (oRZSW.SoftwareUpdate.Download().Result)
                {
                    sScriptResult = "..installing " + oRZSW.SoftwareUpdate.SW.ShortName;
                    rnd = new Random();
                    tReInit.Interval = rnd.Next(2000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit

                    if (oRZSW.SoftwareUpdate.Install(false, true).Result)
                    {
                        sScriptResult = "Installed: " + oRZSW.SoftwareUpdate.SW.ShortName;
                        
                        if (!string.IsNullOrEmpty(AzureLog.WorkspaceId))
                        {
                            JObject jLog = new JObject();
                            jLog.Add(new JProperty("Computer", Environment.MachineName));
                            jLog.Add(new JProperty("EventID", 2000));
                            jLog.Add(new JProperty("Description", "RuckZuck updating:" + oRZSW.SoftwareUpdate.SW.ShortName));
                            jLog.Add(new JProperty("CustomerID", xAgent.CustomerID));

                            AzureLog.Post(jLog.ToString());
                        }

                        rnd = new Random();
                        tReInit.Interval = rnd.Next(3000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                    }
                    else
                    {
                        sScriptResult = "Failed: " + oRZSW.SoftwareUpdate.SW.ShortName;
                        rnd = new Random();
                        tReInit.Interval = rnd.Next(3000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                    }
                }
            }
            catch (Exception ex)
            {
                sScriptResult = s1 + " : " + ex.Message;
                Random rnd = new Random();
                tReInit.Interval = rnd.Next(3000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
            }
        }

        public void Start(string[] args)
        {
            Task.Run(() => OnStart(args));
        }

        public void NamedPipeServer(string name)
        {
            //$pipe = new- object System.IO.Pipes.NamedPipeClientStream '.','devcdrsig','In'
            //$pipe.Connect()
            //$sr = new- object System.IO.StreamReader $pipe
            //while (($data = $sr.ReadLine()) -ne $null) { $sig = $data }
            //$sr.Dispose()
            //$pipe.Dispose()
            //$sig
            
            //Trace.WriteLine(DateTime.Now.ToString() + "\t Starting NamedPipeServer " + name + " ... ");
            while (true)
            {
                try
                {
                    using (NamedPipeServerStream pipeServer =
                    new NamedPipeServerStream(name, PipeDirection.InOut))
                    {
                        pipeServer.WaitForConnection();

                        Trace.WriteLine(DateTime.Now.ToString() + "\t NamedPipe client connected on " + name + "... " );
                        try
                        {

                            
                            if (xAgent != null)
                            {
                                if (name == "devcdrsig")
                                {
                                    using (StreamWriter sw = new StreamWriter(pipeServer))
                                    {
                                        sw.AutoFlush = true;
                                        sw.WriteLine(xAgent.Signature);
                                    }
                                }
                                if (name == "devcdrep")
                                {
                                    using (StreamWriter sw = new StreamWriter(pipeServer))
                                    {
                                        sw.AutoFlush = true;
                                        sw.WriteLine(xAgent.EndpointURL.Replace("/chat", ""));
                                    }
                                    
                                }
                                if (name == "devcdrid")
                                {
                                    using (StreamWriter sw = new StreamWriter(pipeServer))
                                    {
                                        sw.AutoFlush = true;
                                        sw.WriteLine(xAgent.CustomerID);
                                    }
                                    
                                }

                                if (name == "comcheck")
                                {
                                    using (StreamReader sr = new StreamReader(pipeServer))
                                    {
                                        try
                                        {
                                            string sin = sr.ReadLine();
                                            HealthCheckMinutes = int.Parse(sin);
                                        }
                                        catch { }
                                    }

                                }

                            }
                        }

                        // Catch the IOException that is raised if the pipe is broken
                        // or disconnected.
                        catch (IOException e)
                        {
                            Console.WriteLine("NP ERROR: {0}", e.Message);
                        }

                    }
                }
                catch(Exception ex)
                {
                    if (ex.Message.ToLower().StartsWith("all pipe instances are busy"))
                    {
                        return;
                    }
                    else
                    {
                        Thread.Sleep(5000);
                    }
                }
            }
        }

        public void EventLogEventRead(object obj, EventRecordWrittenEventArgs arg)
        {
            try
            {
                // Make sure there was no error reading the event.
                if (arg.EventRecord != null && arg.EventRecord.RecordId != iLastEventId)
                {
                    try
                    {
                        bool bVirus = false;
                        bool bConfig = false;
                        bool bASR = false;
                        bool bNEP = false;
                        iLastEventId = arg.EventRecord.RecordId ?? 0; //prevent duplicate reports
                        switch (arg.EventRecord.Id)
                        {
                            case 1006:
                                bVirus = true;
                                break;
                            case 1015:
                                bVirus = true;
                                break;
                            case 1116:
                                bVirus = true;
                                break;
                            case 1117:
                                bVirus = true;
                                break;
                            case 1118:
                                bVirus = true;
                                break;
                            case 1119:
                                bVirus = true;
                                break;
                            case 1121: //ASR Block
                                bASR = true;
                                break;
                            case 1122: //ASR Audit
                                bASR = true;
                                break;
                            case 1125: //Network protection Audit
                                bNEP = true;
                                break;
                            case 1126: //Network protection Block
                                bNEP = true;
                                break;
                            case 5007:
                                bConfig = true;
                                break;
                        }

                        if (bVirus)
                        {
                            if (!string.IsNullOrEmpty(AzureLog.WorkspaceId))
                            {
                                JObject jLog = new JObject();

                                string sCustomerID = "";
                                try
                                {
                                    sCustomerID = xAgent.CustomerID;
                                }
                                catch { }

                                try
                                {
                                    Trace.WriteLine(DateTime.Now.ToString() + "\t Virus detected (" + arg.EventRecord.Properties[7].Value + ")... !!!");
                                    jLog.Add(new JProperty("Computer", Environment.MachineName));
                                    jLog.Add(new JProperty("DeviceID", Properties.Settings.Default.HardwareID));
                                    jLog.Add(new JProperty("EventID", 1000));
                                    jLog.Add(new JProperty("DefenderEventID", arg.EventRecord.Id));
                                    jLog.Add(new JProperty("EventRecordID", arg.EventRecord.RecordId));
                                    jLog.Add(new JProperty("Description", arg.EventRecord.Properties[7].Value));
                                    jLog.Add(new JProperty("DetectionID", arg.EventRecord.Properties[2].Value));
                                    jLog.Add(new JProperty("DetectionTime", arg.EventRecord.Properties[3].Value));
                                    jLog.Add(new JProperty("CustomerID", sCustomerID));
                                    jLog.Add(new JProperty("ThreatID", arg.EventRecord.Properties[6].Value));
                                    jLog.Add(new JProperty("ThreatName", arg.EventRecord.Properties[7].Value));
                                    jLog.Add(new JProperty("SeverityID", arg.EventRecord.Properties[8].Value));
                                    jLog.Add(new JProperty("CategoryID", arg.EventRecord.Properties[10].Value));
                                    jLog.Add(new JProperty("FWLink", arg.EventRecord.Properties[12].Value));
                                    jLog.Add(new JProperty("SourceID", arg.EventRecord.Properties[16].Value));
                                    jLog.Add(new JProperty("Process", arg.EventRecord.Properties[18].Value));
                                    jLog.Add(new JProperty("User", arg.EventRecord.Properties[19].Value));
                                    jLog.Add(new JProperty("Resource", arg.EventRecord.Properties[21].Value));
                                    jLog.Add(new JProperty("ActionID", arg.EventRecord.Properties[29].Value));
                                    jLog.Add(new JProperty("ErrorDescription", arg.EventRecord.Properties[33].Value));
                                    jLog.Add(new JProperty("ActionString", arg.EventRecord.Properties[37].Value));
                                }
                                catch { }

                                if (isconnected)
                                {
                                    try
                                    {
                                        AzureLog.Post(jLog.ToString(), "Defender");

                                        using (HttpClient oWebClient = new HttpClient())
                                        {
                                            string sEndPoint = xAgent.EndpointURL.Replace("/chat", "");
                                            HttpContent oCont = new StringContent(jLog.ToString());
                                            oWebClient.PostAsync(sEndPoint + "/devcdr/createalert?signature=" + xAgent.Signature, oCont).Wait(3000);
                                            Trace.WriteLine(DateTime.Now.ToString() + "\t Alert created:" + sEndPoint + ";" + jLog.ToString());
                                        }
                                    }
                                    catch(Exception ex)
                                    {
                                        Trace.WriteLine(DateTime.Now.ToString() + "\t ERROR: unable to create alert.. " + ex.Message);
                                    }
                                }
                                else
                                {
                                    //Store Alert as JSON File...
                                    if (!Directory.Exists(Path.Combine(Environment.ExpandEnvironmentVariables("%TEMP%"), "AVDetection")))
                                        Directory.CreateDirectory(Path.Combine(Environment.ExpandEnvironmentVariables("%TEMP%"), "AVDetection"));

                                    File.WriteAllText(Path.Combine(Environment.ExpandEnvironmentVariables("%TEMP%"), "AVDetection", Path.GetRandomFileName() + ".json"), jLog.ToString());
                                }

                                Trace.WriteLine(DateTime.Now.ToString() + "\t Threat Alert send to Azure Logs...");
                            }

                            //Enforce Inventory and Compliance check
                            Properties.Settings.Default.InventorySuccess = new DateTime().AddDays(1);
                            Properties.Settings.Default.HealthCheckSuccess = new DateTime().AddDays(1);
                            Random rnd2 = new Random();
                            tReInit.Interval = rnd2.Next(200, Properties.Settings.Default.StatusDelay); //wait max Xs to ReInit

                        }

                        if (bConfig)
                        {
                            if (!string.IsNullOrEmpty(AzureLog.WorkspaceId))
                            {
                                JObject jLog = new JObject();

                                string sCustomerID = "";
                                try
                                {
                                    sCustomerID = xAgent.CustomerID;
                                }
                                catch { }

                                try
                                {
                                    Trace.WriteLine(DateTime.Now.ToString() + "\t Configuration change detected... !!!");
                                    jLog.Add(new JProperty("Computer", Environment.MachineName));
                                    jLog.Add(new JProperty("DeviceID", Properties.Settings.Default.HardwareID));
                                    jLog.Add(new JProperty("CustomerID", sCustomerID));
                                    jLog.Add(new JProperty("EventID", 1001));
                                    jLog.Add(new JProperty("DefenderEventID", arg.EventRecord.Id));
                                    jLog.Add(new JProperty("EventRecordID", arg.EventRecord.RecordId));
                                    jLog.Add(new JProperty("Description", arg.EventRecord.Properties[2].Value));
                                    jLog.Add(new JProperty("ActionString", arg.EventRecord.Properties[3].Value));
                                }
                                catch { }

                                if (isconnected)
                                {
                                    AzureLog.Post(jLog.ToString(), "Defender");
                                }
                            }

                            //Enforce Inventory and Compliance check
                            Properties.Settings.Default.InventorySuccess.AddHours(-2);
                            //Properties.Settings.Default.HealthCheckSuccess = new DateTime();
                            //Random rnd2 = new Random();
                            //tReInit.Interval = rnd2.Next(200, Properties.Settings.Default.StatusDelay * 2); //wait max Xs to ReInit
                        }

                        if (bASR)
                        {
                            if (!string.IsNullOrEmpty(AzureLog.WorkspaceId))
                            {
                                JObject jLog = new JObject();

                                string sCustomerID = "";
                                try
                                {
                                    sCustomerID = xAgent.CustomerID;
                                }
                                catch { }

                                try
                                {
                                    Trace.WriteLine(DateTime.Now.ToString() + "\t ASR Event detected... !!!");
                                    jLog.Add(new JProperty("Computer", Environment.MachineName));
                                    jLog.Add(new JProperty("DeviceID", Properties.Settings.Default.HardwareID));
                                    jLog.Add(new JProperty("CustomerID", sCustomerID));
                                    jLog.Add(new JProperty("EventID", 1002));
                                    jLog.Add(new JProperty("DefenderEventID", arg.EventRecord.Id));
                                    jLog.Add(new JProperty("EventRecordID", arg.EventRecord.RecordId));
                                    jLog.Add(new JProperty("Description", "ASR activity"));
                                    jLog.Add(new JProperty("Process", arg.EventRecord.Properties[7].Value)); //Blocked File
                                    jLog.Add(new JProperty("Resource", arg.EventRecord.Properties[3].Value)); //ASR Rule ID
                                    //jLog.Add(new JProperty("ErrorDescription", "ASR blocked activity")); //

                                }
                                catch { }

                                if (isconnected)
                                {
                                    AzureLog.Post(jLog.ToString(), "Defender");
                                }
                                else
                                {
                                    //Store Alert as JSON File...
                                    if (!Directory.Exists(Path.Combine(Environment.ExpandEnvironmentVariables("%TEMP%"), "AVDetection")))
                                        Directory.CreateDirectory(Path.Combine(Environment.ExpandEnvironmentVariables("%TEMP%"), "AVDetection"));

                                    File.WriteAllText(Path.Combine(Environment.ExpandEnvironmentVariables("%TEMP%"), "AVDetection", Path.GetRandomFileName() + ".json"), jLog.ToString());
                                }

                                Trace.WriteLine(DateTime.Now.ToString() + "\t Threat Alert send to Azure Logs...");
                            }

                            //Properties.Settings.Default.InventorySuccess = new DateTime();
                            //Properties.Settings.Default.HealthCheckSuccess = new DateTime();
                            //Random rnd2 = new Random();
                            //tReInit.Interval = rnd2.Next(200, Properties.Settings.Default.StatusDelay * 2); //wait max Xs to ReInit
                        }

                        if (bNEP)
                        {
                            if (!string.IsNullOrEmpty(AzureLog.WorkspaceId))
                            {
                                JObject jLog = new JObject();

                                string sCustomerID = "";
                                try
                                {
                                    sCustomerID = xAgent.CustomerID;
                                }
                                catch { }

                                try
                                {
                                    Trace.WriteLine(DateTime.Now.ToString() + "\t NEP Event detected... !!!");
                                    jLog.Add(new JProperty("Computer", Environment.MachineName));
                                    jLog.Add(new JProperty("DeviceID", Properties.Settings.Default.HardwareID));
                                    jLog.Add(new JProperty("CustomerID", sCustomerID));
                                    jLog.Add(new JProperty("EventID", 1003));
                                    jLog.Add(new JProperty("DefenderEventID", arg.EventRecord.Id));
                                    jLog.Add(new JProperty("EventRecordID", arg.EventRecord.RecordId));
                                    jLog.Add(new JProperty("Description", "NEP activity"));
                                    jLog.Add(new JProperty("Process", arg.EventRecord.Properties[7].Value)); //Blocked File
                                    jLog.Add(new JProperty("Resource", arg.EventRecord.Properties[3].Value)); //ASR Rule ID
                                    //jLog.Add(new JProperty("ErrorDescription", "ASR blocked activity")); //

                                }
                                catch { }

                                if (isconnected)
                                {
                                    AzureLog.Post(jLog.ToString(), "Defender");
                                }
                                else
                                {
                                    //Store Alert as JSON File...
                                    if (!Directory.Exists(Path.Combine(Environment.ExpandEnvironmentVariables("%TEMP%"), "AVDetection")))
                                        Directory.CreateDirectory(Path.Combine(Environment.ExpandEnvironmentVariables("%TEMP%"), "AVDetection"));

                                    File.WriteAllText(Path.Combine(Environment.ExpandEnvironmentVariables("%TEMP%"), "AVDetection", Path.GetRandomFileName() + ".json"), jLog.ToString());
                                }

                                Trace.WriteLine(DateTime.Now.ToString() + "\t Threat Alert send to Azure Logs...");
                            }

                            //Enforce HealthCheck
                            //Properties.Settings.Default.InventorySuccess = new DateTime();
                            //Properties.Settings.Default.HealthCheckSuccess = new DateTime();
                            //Random rnd2 = new Random();
                            //tReInit.Interval = rnd2.Next(200, Properties.Settings.Default.StatusDelay * 2); //wait max Xs to ReInit
                        }

                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine(DateTime.Now.ToString() + "\t Error on AV detection: " + ex.Message);
                        Properties.Settings.Default.InventorySuccess = new DateTime().AddDays(1);
                        Properties.Settings.Default.HealthCheckSuccess = new DateTime().AddDays(1);
                    }
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine("E2181: " + ex.Message);
            }
        }

        protected override void OnStop()
        {
            try
            {
                isstopping = true;
                tReCheck.Enabled = false;
                tReInit.Enabled = false;

                tReCheck.Stop();
                tReInit.Stop();

                Trace.WriteLine(DateTime.Now.ToString() + "\t stopping DevCDRAgent...");
                Trace.Flush();
                Trace.Close();

                Thread.Sleep(1000);

                connection.StopAsync().Wait(3000);
                connection.DisposeAsync().Wait(1000);
            }
            catch { }

            base.OnStop();
        }

        public void RestartService()
        {
            try
            {
                isstopping = true;
                tReCheck.Enabled = false;
                tReInit.Enabled = false;

                Trace.WriteLine(DateTime.Now.ToString() + "\t restarting Service..");
                Trace.Flush();
                //Trace.Close();

                using (PowerShell PowerShellInstance = PowerShell.Create())
                {
                    try
                    {
                        PowerShellInstance.AddScript("powershell.exe -command stop-service DevCDRAgentCore -Force;sleep 5;start-service DevCDRAgentCore");
                        var PSResult = PowerShellInstance.Invoke();
                    }
                    catch { }
                }

                Thread.Sleep(5000);

                //In case of restart failed...
                tReCheck.Enabled = true;
                tReInit.Enabled = true;
                Random rnd = new Random();
                tReInit.Interval = rnd.Next(3000, Properties.Settings.Default.StatusDelay * 5); //wait max 5s to ReInit
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private void ConsoleError(object sender, DataAddingEventArgs e)
        {
            if (e.ItemAdded != null)
            {
                sScriptResult = "ERROR: " + e.ItemAdded.ToString();
                Trace.WriteLine("ERROR: " + e.ItemAdded.ToString());
            }
        }

        private void ConsoleOutput(object sender, DataAddingEventArgs e)
        {
            if (e.ItemAdded != null)
            {
                sScriptResult = e.ItemAdded.ToString();
                Trace.WriteLine(e.ItemAdded.ToString());

                if (tReInit.Interval > 5000)
                    tReInit.Interval = 2000;
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
