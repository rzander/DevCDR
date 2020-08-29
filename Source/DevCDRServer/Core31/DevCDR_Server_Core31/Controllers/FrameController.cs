using DevCDR.Extensions;
using jaindb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace DevCDRServer.Controllers
{
    [TokenAuthentication]
    public class FrameController : Controller
    {
        public DevCDR.Extensions.AzureLogAnalytics AzureLog = new DevCDR.Extensions.AzureLogAnalytics("", "", "");
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<Default> _hubContext;
        private IMemoryCache _cache;
        public FrameController(IHubContext<Default> hubContext, IWebHostEnvironment env, IMemoryCache memoryCache)
        {
            _hubContext = hubContext;
            _env = env;
            _cache = memoryCache;

            try
            {
                Type xType = Type.GetType("DevCDRServer.Default");

                MemberInfo[] memberInfos = xType.GetMember("wwwrootPath", BindingFlags.Public | BindingFlags.Static);
                ((FieldInfo)memberInfos[0]).SetValue(new string(""), _env.WebRootPath);
            }
            catch { }


            if (string.IsNullOrEmpty(AzureLog.WorkspaceId))
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Log-WorkspaceID")) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Log-SharedKey")))
                {
                    AzureLog = new DevCDR.Extensions.AzureLogAnalytics(Environment.GetEnvironmentVariable("Log-WorkspaceID"), Environment.GetEnvironmentVariable("Log-SharedKey"), "DevCDR_" + (Environment.GetEnvironmentVariable("INSTANCENAME") ?? "Default"));
                    //AzureLog.PostAsync(new { Computer = Environment.MachineName, EventID = 0001, Description = "DevCDRController started" });
                }
            }
        }

        public int ClientCount(string Instance = "Default")
        {
            int iCount = 0;
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MemberInfo[] memberInfos = xType.GetMember("ClientCount", BindingFlags.Public | BindingFlags.Static);

                var oCount = ((PropertyInfo)memberInfos[0]).GetValue(new int());

                if (oCount == null)
                    iCount = 0;
                else
                    iCount = (int)oCount;
            }
            catch { }

            return iCount;
        }

        [TokenAuthentication]
        //[Authorize]
        [HttpPost]
        public object Command()
        {
            string sParams = "";
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                sParams = reader.ReadToEndAsync().Result;
            JObject oParams = JObject.Parse(sParams);

            string sCommand = oParams.SelectToken(@"$.command").Value<string>(); //get command name
            string sInstance = "Default"; //= oParams.SelectToken(@"$.instance").Value<string>(); //get instance name
            string sArgs = oParams.SelectToken(@"$.args").Value<string>(); //get parameters

            if (string.IsNullOrEmpty(sInstance)) //Skip if instance is null
                return new ContentResult();

            List<string> lHostnames = new List<string>();
            
            string customerid = Request.Query["customerid"];

            foreach (var oRow in oParams["rows"])
            {
                try
                {
                    lHostnames.Add(oRow.Value<string>("Hostname"));
                }
                catch { }
            }

            switch (sCommand)
            {
                case "AgentVersion":
                    AgentVersion(lHostnames, sInstance);
                    break;
                case "Inv":
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                    {
                        CheckInventory(lHostnames);
                    }
                    else
                    {
                        string sEndPoint = Environment.GetEnvironmentVariable("DevCDRUrl") ?? Request.GetEncodedUrl().ToLower().Split("/devcdr/")[0];
                        string inventoryFile = Environment.GetEnvironmentVariable("ScriptInventory") ?? "inventory.ps1";
                        RunCommand(lHostnames, "Invoke-RestMethod -Uri '" + sEndPoint + "/jaindb/getps?filename=" + inventoryFile + "' | IEX;'Inventory complete..'", sInstance, sCommand);
                    }
                    break;
                case "Restart":
                    RunCommand(lHostnames, "restart-computer -force", sInstance, sCommand);
                    break;
                case "Shutdown":
                    RunCommand(lHostnames, "stop-computer -force", sInstance, sCommand);
                    break;
                case "Logoff":
                    RunCommand(lHostnames, "(gwmi win32_operatingsystem).Win32Shutdown(4);'Logoff enforced..'", sInstance, sCommand);
                    break;
                case "Init":
                    Reload(sInstance);
                    break;
                case "GetRZUpdates":
                    RZScan(lHostnames, sInstance);
                    break;
                case "InstallRZUpdates":
                    RZUpdate(lHostnames, sInstance, sArgs);
                    break;
                case "InstallRZSW":
                    InstallRZSW(lHostnames, sInstance, sArgs);
                    break;
                case "GetGroups":
                    GetGroups(lHostnames, sInstance);
                    break;
                case "SetGroups":
                    //SetGroups(lHostnames, sInstance, sArgs);
                    break;
                case "GetUpdates":
                    RunCommand(lHostnames, "(Get-WUList -MicrosoftUpdate) | select Title | ConvertTo-Json", sInstance, sCommand);
                    break;
                case "InstallUpdates":
                    RunCommand(lHostnames, "Install-WindowsUpdate -MicrosoftUpdate -IgnoreReboot -AcceptAll -Install;installing Updates...", sInstance, sCommand);
                    break;
                case "RestartAgent":
                    RestartAgent(lHostnames, sInstance);
                    break;
                case "SetInstance":
                    //SetInstance(lHostnames, sInstance, sArgs);
                    break;
                case "SetEndpoint":
                    //SetEndpoint(lHostnames, sInstance, sArgs);
                    break;
                case "DevCDRUser":
                    runUserCmd(lHostnames, sInstance, "", "");
                    break;
                case "WOL":
                    sendWOL(lHostnames, sInstance, GetAllMACAddresses(), customerid);
                    break;
                case "Compliance":
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                    {
                        CheckCompliance(lHostnames);
                    }
                    else
                    {
                        string sEndPoint2 = Environment.GetEnvironmentVariable("DevCDRUrl") ?? Request.GetEncodedUrl().ToLower().Split("/devcdr/")[0];
                        string complianceFile = Environment.GetEnvironmentVariable("ScriptCompliance") ?? "compliance.ps1";
                        RunCommand(lHostnames, "Invoke-RestMethod -Uri '" + sEndPoint2 + "/jaindb/getps?filename=" + complianceFile + "' | IEX;'Compliance check complete..'", sInstance, sCommand);
                    }
                    break;
            }

            return new ContentResult();
        }

        [TokenAuthentication]
        //[Authorize]
        public ActionResult GetData(string customerid = "", string exp = "", string token = "", string Instance = "Default")
        {
            JArray jData = new JArray();
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MemberInfo[] memberInfos = xType.GetMember("jData", BindingFlags.Public | BindingFlags.Static);

                jData = ((FieldInfo)memberInfos[0]).GetValue(new JArray()) as JArray;
                if (!string.IsNullOrEmpty(customerid))
                    jData = new JArray(jData.SelectTokens($"$.[?(@.Customer=='{ customerid }')]"));
            }
            catch { }

            JObject oObj = new JObject
            {
                { "data", jData }
            };

            return new ContentResult
            {
                Content = oObj.ToString(Newtonsoft.Json.Formatting.None),
                ContentType = "application/json"
                //ContentEncoding = Encoding.UTF8
            };
        }

        [TokenAuthentication]
        //[Authorize]
        public ActionResult GetRZCatalog(string Instance = "Default")
        {
            List<string> lRZCat = new List<string>();
            try
            {
                string sCat = SWResults("");
                JArray oCat = JArray.Parse(sCat);
                lRZCat = JArray.Parse(sCat).SelectTokens("..ShortName").Values<string>().OrderBy(t => t).ToList();
            }
            catch { }

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(lRZCat, Formatting.None),
                ContentType = "application/json"
                //ContentEncoding = Encoding.UTF8
            };
        }

        [AllowAnonymous]
        public ActionResult GetVersion()
        {
            return Content(typeof(FrameController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version);
        }

        [TokenAuthentication]
        //[Authorize]
        public ActionResult Groups(string Instance = "Default")
        {
            List<string> lGroups = new List<string>();
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MemberInfo[] memberInfos = xType.GetMember("lGroups", BindingFlags.Public | BindingFlags.Static);

                lGroups = ((FieldInfo)memberInfos[0]).GetValue(new List<string>()) as List<string>;
            }
            catch { }

            lGroups.Remove("web");
            lGroups.Remove("Devices");

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(lGroups, Formatting.None),
                ContentType = "application/json"
                //ContentEncoding = Encoding.UTF8
            };
        }

        [TokenAuthentication]
        public ActionResult Index()
        {
            ViewBag.Title = Environment.GetEnvironmentVariable("INSTANCETITLE") ?? "Default Environment";
            ViewBag.Instance = Environment.GetEnvironmentVariable("INSTANCENAME") ?? "Default";
            ViewBag.appVersion = typeof(FrameController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
            ViewBag.Endpoint = Request.GetEncodedUrl().Split("/DevCDR/Default")[0] + "/chat";
            ViewBag.Customer = Environment.GetEnvironmentVariable("CUSTOMERID") ?? "DEMO";

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
            {
                if (System.IO.File.Exists(Path.Combine(_env.WebRootPath, "DevCDRAgentCoreNew.msi")))
                    ViewBag.InstallCMD = $"&msiexec -i { Request.GetEncodedUrl().Split("/DevCDR/Default")[0] + "/DevCDRAgentCoreNew.msi" } CUSTOMER={ViewBag.Customer} ENDPOINT={Request.GetEncodedUrl().Split("/DevCDR/Default")[0] + "/chat"}  /qn REBOOT=REALLYSUPPRESS";
                else
                    ViewBag.InstallCMD = $"&msiexec -i https://devcdrcore.azurewebsites.net/DevCDRAgentCoreNew.msi CUSTOMER={ViewBag.Customer} ENDPOINT={Request.GetEncodedUrl().Split("/DevCDR/Default")[0] + "/chat"} /qn REBOOT=REALLYSUPPRESS";
            }
            else
            {
                if (System.IO.File.Exists(Path.Combine(_env.WebRootPath, "DevCDRAgentCoreNew.msi")))
                    ViewBag.InstallCMD = $"&msiexec -i { Request.GetEncodedUrl().Split("/DevCDR/Default")[0] + "/DevCDRAgentCoreNew.msi" } ENDPOINT={Request.GetEncodedUrl().Split("/DevCDR/Default")[0] + "/chat"} /qn REBOOT=REALLYSUPPRESS";
                else
                    ViewBag.InstallCMD = $"&msiexec -i https://devcdrcore.azurewebsites.net/DevCDRAgentCoreNew.msi ENDPOINT={Request.GetEncodedUrl().Split("/DevCDR/Default")[0] + "/chat"} /qn REBOOT=REALLYSUPPRESS";
            }
            ViewBag.Route = "/chat";

            var sRoot = Directory.GetCurrentDirectory();
            if (System.IO.File.Exists(Path.Combine(sRoot, "wwwroot/plugin_ContextMenu.cshtml")))
            {
                ViewBag.Menu = System.IO.File.ReadAllText(Path.Combine(sRoot, "wwwroot/plugin_ContextMenu.cshtml"));
                ViewBag.ExtMenu = true;
            }

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IP2LocationURL")))
            {
                ViewBag.Location = "Internal IP";
            }
            else
            {
                ViewBag.Location = "Location";
            }
            return View();
        }
        [TokenAuthentication]
        //[Authorize]
        [HttpPost]
        public object RunPSAsync()
        {
            string sParams = "";
            //Load response
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                sParams = reader.ReadToEndAsync().Result;

            if (string.IsNullOrEmpty(sParams))
                return new ContentResult(); ;

            //Parse response as JSON
            JObject oParams = JObject.Parse(sParams);

            string sCommand = System.Uri.UnescapeDataString(oParams.SelectToken(@"$.psscript").Value<string>()); //get command
            string sInstance = "Default"; // oParams.SelectToken(@"$.instance").Value<string>(); //get instance name
            string sTitle = oParams.SelectToken(@"$.title").Value<string>(); //get title

            if (string.IsNullOrEmpty(sInstance)) //Skip if instance is null
                return new ContentResult();

            List<string> lHostnames = new List<string>();

            foreach (var oRow in oParams["rows"])
            {
                string sHost = oRow.Value<string>("Hostname");
                SetResult(sInstance, sHost, "triggered:" + sTitle); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "RunPSAsync"); //Enforce PageUpdate

            foreach (var oRow in oParams["rows"])
            {
                try
                {
                    //Get Hostname from Row
                    string sHost = oRow.Value<string>("Hostname");

                    if (string.IsNullOrEmpty(sHost))
                        continue;

                    //Get ConnectionID from HostName
                    string sID = GetID(sInstance, sHost);

                    if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                    {
                        AzureLog.PostAsync(new { Computer = sHost, EventID = 2051, Description = $"Run PSAsync: {sCommand}" });
                        _hubContext.Clients.Client(sID).SendAsync("returnPSAsync", sCommand, "Host");
                    }
                }
                catch { }
            }

            return new ContentResult();
        }

        [TokenAuthentication]
        //[Authorize]
        [HttpPost]
        public object RunPSCommand()
        {
            string sParams = "";
            //Load response
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                sParams = reader.ReadToEndAsync().Result;

            if (string.IsNullOrEmpty(sParams))
                return new ContentResult(); ;

            //Parse response as JSON
            JObject oParams = JObject.Parse(sParams);

            string sCommand = System.Uri.UnescapeDataString(oParams.SelectToken(@"$.psscript").Value<string>()); //get command
            string sInstance = "Default"; // oParams.SelectToken(@"$.instance").Value<string>(); //get instance name
            string sTitle = oParams.SelectToken(@"$.title").Value<string>(); //get title

            if (string.IsNullOrEmpty(sInstance)) //Skip if instance is null
                return new ContentResult();

            List<string> lHostnames = new List<string>();

            foreach (var oRow in oParams["rows"])
            {
                string sHost = oRow.Value<string>("Hostname");
                SetResult(sInstance, sHost, "triggered:" + sTitle); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "RunPS"); //Enforce PageUpdate

            foreach (var oRow in oParams["rows"])
            {
                try
                {
                    //Get Hostname from Row
                    string sHost = oRow.Value<string>("Hostname");

                    if (string.IsNullOrEmpty(sHost))
                        continue;

                    //Get ConnectionID from HostName
                    string sID = GetID(sInstance, sHost);

                    if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                    {
                        AzureLog.PostAsync(new { Computer = sHost, EventID = 2050, Description = $"Run PS: {sCommand}" });
                        _hubContext.Clients.Client(sID).SendAsync("returnPS", sCommand, "Host");
                    }
                }
                catch { }
            }

            return new ContentResult();
        }

        [TokenAuthentication]
        //[Authorize]
        [HttpPost]
        public object RunPSFile()
        {
            string sParams = "";
            //Load response
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                sParams = reader.ReadToEndAsync().Result;

            if (string.IsNullOrEmpty(sParams))
                return new ContentResult(); ;

            //Parse response as JSON
            JObject oParams = JObject.Parse(sParams);

            string sFile = System.Uri.UnescapeDataString(oParams.SelectToken(@"$.file").Value<string>()); //get command
            string sInstance = "Default"; // oParams.SelectToken(@"$.instance").Value<string>(); //get instance name
            string sTitle = oParams.SelectToken(@"$.title").Value<string>(); //get title

            string sFilePath = Path.Combine(_env.WebRootPath, sFile);

            if (System.IO.File.Exists(sFilePath))
            {
                if (!sFilePath.StartsWith(Path.Combine(_env.WebRootPath, "PSScripts")))
                    return new ContentResult();

                string sCommand = System.IO.File.ReadAllText(sFilePath);

                if (string.IsNullOrEmpty(sInstance)) //Skip if instance is null
                    return new ContentResult();

                List<string> lHostnames = new List<string>();

                foreach (var oRow in oParams["rows"])
                {
                    string sHost = oRow.Value<string>("Hostname");
                    SetResult(sInstance, sHost, "triggered:" + sTitle); //Update Status
                }
                _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "RunPSFile"); //Enforce PageUpdate

                foreach (var oRow in oParams["rows"])
                {
                    try
                    {
                        //Get Hostname from Row
                        string sHost = oRow.Value<string>("Hostname");

                        if (string.IsNullOrEmpty(sHost))
                            continue;

                        //Get ConnectionID from HostName
                        string sID = GetID(sInstance, sHost);

                        if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                        {
                            AzureLog.PostAsync(new { Computer = sHost, EventID = 2052, Description = $"Run PSFile: {sFile}" });
                            _hubContext.Clients.Client(sID).SendAsync("returnPSAsync", sCommand, "Host");
                        }
                    }
                    catch { }
                }
            }

            return new ContentResult();
        }

        [TokenAuthentication]
        //[Authorize]
        [HttpPost]
        public object RunUserPS()
        {
            string sParams = "";
            //Load response
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                sParams = reader.ReadToEndAsync().Result;

            if (string.IsNullOrEmpty(sParams))
                return new ContentResult(); ;

            //Parse response as JSON
            JObject oParams = JObject.Parse(sParams);

            string sCommand = System.Uri.UnescapeDataString(oParams.SelectToken(@"$.psscript").Value<string>()); //get command
            string sInstance = "Default"; // oParams.SelectToken(@"$.instance").Value<string>(); //get instance name
            string sTitle = oParams.SelectToken(@"$.title").Value<string>(); //get title

            if (string.IsNullOrEmpty(sInstance)) //Skip if instance is null
                return new ContentResult();

            List<string> lHostnames = new List<string>();

            foreach (var oRow in oParams["rows"])
            {
                string sHost = oRow.Value<string>("Hostname");
                SetResult(sInstance, sHost, "triggered:" + sTitle); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "RunUserPS"); //Enforce PageUpdate

            foreach (var oRow in oParams["rows"])
            {
                try
                {
                    //Get Hostname from Row
                    string sHost = oRow.Value<string>("Hostname");

                    if (string.IsNullOrEmpty(sHost))
                        continue;

                    //Get ConnectionID from HostName
                    string sID = GetID(sInstance, sHost);

                    if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                    {
                        _hubContext.Clients.Client(sID).SendAsync("userprocess", "powershell.exe", "-command \"& { " + sCommand + " }\"");
                        //hubContext.Clients.Client(sID).returnPS(sCommand, "Host");
                    }
                }
                catch { }
            }

            return new ContentResult();
        }

        internal void _SetEndpoint(List<string> Hostnames, string sInstance, string Args)
        {
            if (string.IsNullOrEmpty(Args))
                return;

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "set Endpoint"); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "SetEndpoint"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    AzureLog.PostAsync(new { Computer = sHost, EventID = 2007, Description = $"set new Endpoint: {Args}" });
                    _hubContext.Clients.Client(sID).SendAsync("setendpoint", Args);
                }
            }
        }

        internal void _SetGroups(List<string> Hostnames, string sInstance, string Args)
        {
            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "set Groups"); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "SetGroups"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    AzureLog.PostAsync(new { Computer = sHost, EventID = 2009, Description = $"Set Agent Groups: {Args}" });
                    _hubContext.Clients.Client(sID).SendAsync("setgroups", Args);
                }
            }
        }

        internal void _SetInstance(List<string> Hostnames, string sInstance, string Args)
        {
            if (string.IsNullOrEmpty(Args))
                return;

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "set Instance"); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "SetInstance"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    _hubContext.Clients.Client(sID).SendAsync("setinstance", Args);
                }
            }
        }

        internal void AgentVersion(List<string> Hostnames, string sInstance = "Default")
        {
            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "Get AgentVersion"); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "AgentVersion"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    AzureLog.PostAsync(new { Computer = sHost, EventID = 2008, Description = $"get Agent version" });
                    _hubContext.Clients.Client(sID).SendAsync("version", "HUB");
                }
            }
        }

        internal void CheckCompliance(List<string> Hostnames, string sInstance = "Default")
        {
            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "Get Compliance..."); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "AgentVersion"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    AzureLog.PostAsync(new { Computer = sHost, EventID = 2012, Description = $"get Compliance data" });
                    _hubContext.Clients.Client(sID).SendAsync("checkComplianceAsync", "HUB");
                }
            }
        }

        internal void CheckInventory(List<string> Hostnames, string sInstance = "Default")
        {
            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "Get Inventory..."); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "AgentVersion"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    AzureLog.PostAsync(new { Computer = sHost, EventID = 2011, Description = $"get Inventory" });
                    _hubContext.Clients.Client(sID).SendAsync("checkInventoryAsync", "HUB");
                }
            }
        }

        internal List<string> GetAllMACAddresses()
        {
            string sURL = Environment.GetEnvironmentVariable("fnDevCDR");
            List<string> lResult = new List<string>();
            if (string.IsNullOrEmpty(sURL))
            {
                var tItems = new JainDBController(_env, _cache).Query("$select=@MAC");
                JArray jMacs = tItems.Result;

                foreach (var jTok in jMacs.SelectTokens("..@MAC"))
                {
                    if (jTok.Type == JTokenType.String)
                        lResult.Add(jTok.Value<string>());
                    if (jTok.Type == JTokenType.Array)
                        lResult.AddRange(jTok.Values<string>().ToList());
                }
            }

            return lResult;
        }

        internal void GetGroups(List<string> Hostnames, string sInstance)
        {
            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "get Groups"); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "GetGroups"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    _hubContext.Clients.Client(sID).SendAsync("getgroups", "Host");
                }
            }
        }

        internal string GetID(string Instance, string Host)
        {
            string sID = "";
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MethodInfo methodInfo = xType.GetMethod(
                                            "GetID",
                                            BindingFlags.Public | BindingFlags.Static
                                        );
                sID = methodInfo.Invoke(new object(), new object[] { Host }) as string;
            }
            catch { }

            return sID;
        }

        internal void InstallRZSW(List<string> Hostnames, string sInstance, string Args)
        {
            if (string.IsNullOrEmpty(Args))
                return;

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "Install SW"); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "InstallRZSW"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    AzureLog.PostAsync(new { Computer = sHost, EventID = 2005, Description = $"install RuckZuck software: {Args}" });
                    _hubContext.Clients.Client(sID).SendAsync("rzinstall", Args);
                }
            }
        }

        internal void Reload(string Instance = "Default")
        {
            string sID = "";
            try
            {
                AzureLog.PostAsync(new { Computer = Environment.MachineName, EventID = 1001, Description = $"Reloading {Instance}" });

                _hubContext.Clients.All.SendAsync("init", "init");
                _hubContext.Clients.Group("web").SendAsync("newData", "Hub", ""); //Enforce PageUpdate

                Type xType = Type.GetType("DevCDRServer." + Instance);

                MethodInfo methodInfo = xType.GetMethod(
                                            "Clear",
                                            BindingFlags.Public | BindingFlags.Static
                                        );
                sID = methodInfo.Invoke(new object(), new object[] { Instance }) as string;
            }
            catch { }
        }

        internal void RestartAgent(List<string> Hostnames, string sInstance)
        {
            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "restart Agent"); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "RestartAgent"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    AzureLog.PostAsync(new { Computer = sHost, EventID = 2006, Description = $"restart Agent" });
                    _hubContext.Clients.Client(sID).SendAsync("restartservice", "HUB");
                }
            }
        }

        internal void RunCommand(List<string> Hostnames, string sCommand, string sInstance, string CmdName)
        {
            //IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + CmdName); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "RunCommand"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    AzureLog.PostAsync(new { Computer = sHost, EventID = 2001, Description = $"PSCommand: {sCommand}" });
                    _hubContext.Clients.Client(sID).SendAsync("returnPS", sCommand, "Host");
                }
            }
        }

        internal void runUserCmd(List<string> Hostnames, string sInstance, string cmd, string args)
        {
            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "run command as User..."); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "runUserCmd"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    AzureLog.PostAsync(new { Computer = sHost, EventID = 2010, Description = $"Run USer Processs: {cmd} {args}" });
                    _hubContext.Clients.Client(sID).SendAsync("userprocess", cmd, args);
                }
            }
        }

        internal void RZScan(List<string> Hostnames, string sInstance)
        {
            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "get RZ Updates"); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "RZScan"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    AzureLog.PostAsync(new { Computer = sHost, EventID = 2003, Description = $"trigger RuckZuck scan" });
                    _hubContext.Clients.Client(sID).SendAsync("rzscan", "HUB");
                }
            }
        }

        internal void RZUpdate(List<string> Hostnames, string sInstance, string Args = "")
        {
            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "install RZ Updates"); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "RZUpdate"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    AzureLog.PostAsync(new { Computer = sHost, EventID = 2004, Description = $"trigger RuckZuck update" });
                    _hubContext.Clients.Client(sID).SendAsync("rzupdate", Args);
                }
            }
        }

        internal void sendWOL(List<string> Hostnames, string sInstance, List<string> MAC, string customerid = "")
        {
            string sURL = Environment.GetEnvironmentVariable("fnDevCDR");

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "WakeUp devices..."); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "sendWOL"); //Enforce PageUpdate

            JArray jWOL = new JArray();
            if (!string.IsNullOrEmpty(sURL))
            {
                sURL = sURL.Replace("{fn}", "fnGetFile");
                HttpClient oClient = new HttpClient();
                var stringWOL = oClient.GetStringAsync($"{sURL}&customerid={customerid}&file=WOLSummary.json").Result;
                if (!string.IsNullOrEmpty(stringWOL))
                {
                    try
                    {
                        jWOL = JArray.Parse(stringWOL);
                    }
                    catch { }
                }
            }

            foreach (string sHost in Hostnames)
            {
                try
                {
                    if (string.IsNullOrEmpty(sHost))
                        continue;

                    //Get ConnectionID from HostName
                    string sID = GetID(sInstance, sHost);

                    if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                    {

                        if (string.IsNullOrEmpty(sURL))
                        {
                            _hubContext.Clients.Client(sID).SendAsync("wol", string.Join(';', MAC));
                        }
                        else
                        {
                            string subnetid = (jWOL.Children<JObject>().Where(t => t["name"].Value<string>().ToLower() == sHost.ToLower()).First())["subnetid"].Value<string>();
                            _hubContext.Clients.Group("web").SendAsync("newData", "wol", subnetid); //Enforce PageUpdate
                            foreach (var oDevice in jWOL.Children<JObject>().Where(t => t["subnetid"].Value<string>() == subnetid))
                            {
                                try
                                {
                                    MAC.Add(oDevice["mac"].Value<string>());
                                }
                                catch { }
                            }

                            _hubContext.Clients.Client(sID).SendAsync("wol", string.Join(';', MAC));
                        }

                        AzureLog.PostAsync(new { Computer = sHost, EventID = 2002, Description = $"WakeUp all devices" });
                    }
                }
                catch { }
            }
        }

        internal ActionResult SetResult(string Instance, string Hostname, string Result)
        {
            JArray jData = new JArray();
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MemberInfo[] memberInfos = xType.GetMember("jData", BindingFlags.Public | BindingFlags.Static);

                jData = ((FieldInfo)memberInfos[0]).GetValue(new JArray()) as JArray;

                var tok = jData.SelectToken("[?(@.Hostname == '" + Hostname + "')].ScriptResult");
                tok = Result;
                jData.SelectToken("[?(@.Hostname == '" + Hostname + "')].ScriptResult").Replace(tok);

                ((FieldInfo)memberInfos[0]).SetValue(new JArray(), jData);

                //AzureLog.PostAsync(new { Computer = Hostname, EventID = 3000, Description = $"Result: {Result}" });
            }
            catch { }


            return new ContentResult();
        }
        internal string SWResults(string Searchstring)
        {
            string sCatFile = @"/App_Data/rzcat.json";
            string sResult = "";
            string sURL = "https://ruckzuck.tools";

            sCatFile = Path.Combine(_env.WebRootPath, "rzcat.json");

            try
            {
                if (string.IsNullOrEmpty(Searchstring))
                {
                    if (System.IO.File.Exists(sCatFile))
                    {
                        if (DateTime.Now.ToUniversalTime() - System.IO.File.GetCreationTime(sCatFile).ToUniversalTime() <= new TimeSpan(1, 0, 1))
                        {
                            sResult = System.IO.File.ReadAllText(sCatFile);
                            if (sResult.StartsWith("[") & sResult.Length > 64) //check if it's JSON
                            {
                                return sResult;
                            }
                        }
                    }
                }
                else
                {

                }

                HttpClient oClient = new HttpClient();
                oClient.DefaultRequestHeaders.Accept.Clear();
                oClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                var response = oClient.GetStringAsync(sURL + "/rest/v2/getcatalog");
                response.Wait(10000); //10s max.
                if (response.IsCompleted)
                {
                    sResult = response.Result;
                    if (sResult.StartsWith("[") & sResult.Length > 64)
                    {
                        if (string.IsNullOrEmpty(Searchstring))
                            System.IO.File.WriteAllText(sCatFile, sResult);

                        return sResult;
                    }
                }


            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            //return old File
            if (System.IO.File.Exists(sCatFile))
            {
                return System.IO.File.ReadAllText(sCatFile);
            }

            return "";
        }

    }

    public class TokenAuthenticationAttribute : ActionFilterAttribute
    {
        private IMemoryCache _cache;
        private WebClient client = new WebClient();
        private Controller controller;


        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            controller = filterContext.Controller as Controller;

            _cache = controller.HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache;

            string CustomerID = controller.Request.Query["customerid"];
            CustomerID = CustomerID ?? controller.Request.Query["amp;customerid"];
            string exp = controller.Request.Query["exp"];
            exp = exp ?? controller.Request.Query["amp;exp"];
            string token = controller.Request.Query["token"];
            token = token ?? controller.Request.Query["amp;token"];

            if (string.IsNullOrEmpty(CustomerID))
            {
                string sCust = controller.Request.Cookies["DEVCDRCUST"] ?? "";
                if (!string.IsNullOrEmpty(sCust))
                {
                    CustomerID = sCust;
                }
            }

            if (string.IsNullOrEmpty(exp))
            {
                string sExp = controller.Request.Cookies["DEVCDREXP"] ?? "";
                if (!string.IsNullOrEmpty(sExp))
                {
                    exp = sExp;
                }
            }

            if (string.IsNullOrEmpty(token))
            {
                string sTok = controller.Request.Cookies["DEVCDRTOK"] ?? "";
                if (!string.IsNullOrEmpty(sTok))
                {
                    token = sTok;
                }
            }

            if (ValidateToken(CustomerID, exp, token))
            {
                CookieOptions option = new CookieOptions();
                option.Expires = DateTime.Now.AddDays(1);
                option.SameSite = SameSiteMode.Strict;
                option.HttpOnly = true;
                option.Secure = true;

                controller.Response.Cookies.Append("DEVCDRCUST", CustomerID, option);
                controller.Response.Cookies.Append("DEVCDREXP", exp, option);
                controller.Response.Cookies.Append("DEVCDRTOK", token, option);

                return;
            }
            else
            {
                controller.Response.Cookies.Delete("DEVCDRCUST");
                controller.Response.Cookies.Delete("DEVCDREXP");
                controller.Response.Cookies.Delete("DEVCDRTOK");
                filterContext.Result = new UnauthorizedResult();
                return;
            }
        }

        private JObject GetCustomerInfo(string CustomerID)
        {
            JObject jResult = new JObject();

            try
            {
                if (_cache.TryGetValue("cust" + CustomerID, out jResult))
                {
                    return jResult;
                }

                string sURL = Environment.GetEnvironmentVariable("fnDevCDR");
                if (string.IsNullOrEmpty(sURL))
                    return null;
                sURL = sURL.Replace("{fn}", "fnGetSumData");

                string customerInfo = client.DownloadString($"{ sURL }&secretname=AzureDevCDRCustomersTable&customerid=" + CustomerID);
                JArray aObj = JArray.Parse(customerInfo);

                var cacheEntryOptions = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(10));
                _cache.Set("cust" + CustomerID, aObj[0] as JObject, cacheEntryOptions);

                return aObj[0] as JObject;
            }
            catch (Exception ex)
            {
                ex.Message.ToString();
            }

            return jResult;
        }

        private bool ValidateToken(string CustomerID, string Exp, string Token)
        {
            try
            {
                string validationresult = "";
                if (!_cache.TryGetValue("tok" + CustomerID + Exp + Token, out validationresult))
                {
                    string sURL = Environment.GetEnvironmentVariable("fnDevCDR");
                    sURL = sURL.Replace("{fn}", "fnValidateToken");
                    if (string.IsNullOrEmpty(sURL))
                        return false;

                    validationresult = client.DownloadString($"{ sURL }&customerid={ CustomerID }&exp={ Exp }&token={ Token }");
                    var cacheEntryOptions = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(10));
                    _cache.Set("tok" + CustomerID + Exp + Token, validationresult, cacheEntryOptions);
                }

                controller.ViewBag.customerid = CustomerID;
                controller.ViewBag.exp = Exp;
                controller.ViewBag.token = Token;

                if (validationresult == jaindb.Hash.CalculateMD5HashString("sec" + CustomerID + Exp + Token))
                {
                    controller.ViewBag.SecKey = true;
                    controller.ViewBag.AdminKey = false;
                    controller.ViewBag.PriKey = false;
                    return true;
                }

                if (validationresult == jaindb.Hash.CalculateMD5HashString("pri" + CustomerID + Exp + Token))
                {
                    controller.ViewBag.SecKey = false;
                    controller.ViewBag.AdminKey = false;
                    controller.ViewBag.PriKey = true;
                    return true;
                }

                if (validationresult == jaindb.Hash.CalculateMD5HashString("adm" + Exp + Token))
                {
                    controller.ViewBag.SecKey = false;
                    controller.ViewBag.AdminKey = true;
                    controller.ViewBag.PriKey = false;
                    return true;
                }
            }
            catch (Exception ex)
            {
                ex.Message.ToString();
            }

            return false;
        }
    }
}