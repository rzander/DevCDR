using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using DevCDR;
using System.IO;
using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Extensions;

namespace DevCDRServer.Controllers
{
    [Authorize]
    public class DevCDRController : Controller
    {
        private readonly IHubContext<Default> _hubContext;
        private readonly IHostingEnvironment _env;
        private IMemoryCache _cache;

        public DevCDRController(IHubContext<Default> hubContext, IHostingEnvironment env, IMemoryCache memoryCache)
        {
            _hubContext = hubContext;
            _env = env;
            _cache = memoryCache;
        }

        [AllowAnonymous]
        public ActionResult Dashboard()
        {
            ViewBag.Title = "Dashboard " + Environment.GetEnvironmentVariable("INSTANCETITLE");
            ViewBag.Instance = Environment.GetEnvironmentVariable("INSTANCENAME");
            ViewBag.appVersion = typeof(DevCDRController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
            ViewBag.Route = "/Chat";

            int itotalDeviceCount = -1;
            
           itotalDeviceCount = new JainDBController(_env, _cache).totalDeviceCount(Path.Combine(_env.WebRootPath, "JainDB\\_Chain"));

            int iDefault = ClientCount("Default");
            int iOnlineCount = iDefault;
            int iOfflineCount = itotalDeviceCount - iOnlineCount;

            if (iOfflineCount < 0)
                iOfflineCount = 0;

            if (iOnlineCount > itotalDeviceCount)
                itotalDeviceCount = iOnlineCount;

            ViewBag.TotalDeviceCount = itotalDeviceCount;
            ViewBag.OnlineDeviceCount = iOnlineCount;
            ViewBag.OfflineDeviceCount = iOfflineCount;
            ViewBag.TotalDefault = iDefault;
            return View();
        }

#if DEBUG
        [AllowAnonymous]
#endif
        [Authorize]
        public ActionResult Default()
        {
            ViewBag.Title = Environment.GetEnvironmentVariable("INSTANCETITLE") ?? "Default Environment";
            ViewBag.Instance = Environment.GetEnvironmentVariable("INSTANCENAME") ?? "Default";
            ViewBag.appVersion = typeof(DevCDRController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
            ViewBag.Route = "/chat";
            return View();
        }

        [AllowAnonymous]
        public ActionResult About()
        {
            ViewBag.Message = "Device Commander details...";
            ViewBag.appVersion = typeof(DevCDRController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;

            return View();
        }

        [AllowAnonymous]
        public ActionResult Contact()
        {
            ViewBag.Message = "Device Commander Contact....";
            ViewBag.appVersion = typeof(DevCDRController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;

            return View();
        }

        [AllowAnonymous]
        public ActionResult GetData(string Instance)
        {
            JArray jData = new JArray();
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MemberInfo[] memberInfos = xType.GetMember("jData", BindingFlags.Public | BindingFlags.Static);

                jData = ((FieldInfo)memberInfos[0]).GetValue(new JArray()) as JArray;
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

        public int ClientCount(string Instance)
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

        [AllowAnonymous]
        public ActionResult Groups(string Instance)
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

        [AllowAnonymous]
        public ActionResult GetRZCatalog(string Instance)
        {
            List<string> lRZCat = new List<string>();
            try
            {
                string sCat = SWResults("");
                JArray oCat = JArray.Parse(sCat);
                lRZCat = JArray.Parse(sCat).SelectTokens("..Shortname").Values<string>().OrderBy(t => t).ToList();
            }
            catch { }

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(lRZCat, Formatting.None),
                ContentType = "application/json"
                //ContentEncoding = Encoding.UTF8
            };
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
            }
            catch { }


            return new ContentResult();
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

        internal void Reload(string Instance)
        {
            string sID = "";
            try
            {
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

#if DEBUG
        [AllowAnonymous]
#endif
        [Authorize]
        [HttpPost]
        public object Command()
        {
            string sParams = "";
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                sParams = reader.ReadToEnd();
            JObject oParams = JObject.Parse(sParams);

            string sCommand = oParams.SelectToken(@"$.command").Value<string>(); //get command name
            string sInstance = oParams.SelectToken(@"$.instance").Value<string>(); //get instance name
            string sArgs = oParams.SelectToken(@"$.args").Value<string>(); //get parameters

            if (string.IsNullOrEmpty(sInstance)) //Skip if instance is null
                return new ContentResult();

            List<string> lHostnames = new List<string>();
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
                    string sEndPoint = Request.GetDisplayUrl().ToLower().Split("/devcdr")[0];
                    RunCommand(lHostnames, "Invoke-RestMethod -Uri '" + sEndPoint + "/jaindb/getps' | IEX;'Inventory complete..'", sInstance, sCommand);
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
                    SetGroups(lHostnames, sInstance, sArgs);
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
                    SetInstance(lHostnames, sInstance, sArgs);
                    break;
                case "SetEndpoint":
                    SetEndpoint(lHostnames, sInstance, sArgs);
                    break;
                case "DevCDRUser":
                    runUserCmd(lHostnames, sInstance, "", "");
                    break;
                case "WOL":
                    sendWOL(lHostnames, sInstance, sArgs.Split(',').ToList());
                    break;
            }

            return new ContentResult();
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
                    _hubContext.Clients.Client(sID).SendAsync("returnPS", sCommand, "Host");
                }
            }
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

        internal void SetGroups(List<string> Hostnames, string sInstance, string Args)
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
                    _hubContext.Clients.Client(sID).SendAsync("setgroups", Args);
                }
            }
        }

        internal void AgentVersion(List<string> Hostnames, string sInstance)
        {
            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "get AgentVersion"); //Update Status
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
                    _hubContext.Clients.Client(sID).SendAsync("version", "HUB");
                }
            }
        }

        internal void SetInstance(List<string> Hostnames, string sInstance, string Args)
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

        internal void SetEndpoint(List<string> Hostnames, string sInstance, string Args)
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
                    _hubContext.Clients.Client(sID).SendAsync("setendpoint", Args);
                }
            }
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
                    _hubContext.Clients.Client(sID).SendAsync("restartservice","HUB");
                }
            }
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
                    _hubContext.Clients.Client(sID).SendAsync("rzinstall" , Args);
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
                    _hubContext.Clients.Client(sID).SendAsync("rzupdate" ,Args);
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
                    _hubContext.Clients.Client(sID).SendAsync("userprocess", cmd, args);
                }
            }
        }

        internal void sendWOL(List<string> Hostnames, string sInstance, List<string> MAC)
        {
            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "WakeUp devices..."); //Update Status
            }
            _hubContext.Clients.Group("web").SendAsync("newData", "HUB", "sendWOL"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    foreach (string sMAC in MAC)
                        _hubContext.Clients.Client(sID).SendAsync("wol", sMAC);
                }
            }
        }

#if DEBUG
        [AllowAnonymous]
#endif
        [Authorize]
        [HttpPost]
        public object RunPS()
        {
            string sParams = "";
            //Load response
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                sParams = reader.ReadToEnd();

            if (string.IsNullOrEmpty(sParams))
                return new ContentResult(); ;

            //Parse response as JSON
            JObject oParams = JObject.Parse(sParams);

            string sCommand = System.Uri.UnescapeDataString(oParams.SelectToken(@"$.psscript").Value<string>()); //get command
            string sInstance = oParams.SelectToken(@"$.instance").Value<string>(); //get instance name
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

                        _hubContext.Clients.Client(sID).SendAsync("returnPS", sCommand, "Host");
                    }
                }
                catch { }
            }

            return new ContentResult();
        }

#if DEBUG
        [AllowAnonymous]
#endif
        [Authorize]
        [HttpPost]
        public object RunUserPS()
        {
            string sParams = "";
            //Load response
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                sParams = reader.ReadToEnd();

            if (string.IsNullOrEmpty(sParams))
                return new ContentResult(); ;

            //Parse response as JSON
            JObject oParams = JObject.Parse(sParams);

            string sCommand = System.Uri.UnescapeDataString(oParams.SelectToken(@"$.psscript").Value<string>()); //get command
            string sInstance = oParams.SelectToken(@"$.instance").Value<string>(); //get instance name
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

#if DEBUG
        [AllowAnonymous]
#endif
        [Authorize]
        [HttpPost]
        public object RunPSAsync()
        {
            string sParams = "";
            //Load response
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                sParams = reader.ReadToEnd();

            if (string.IsNullOrEmpty(sParams))
                return new ContentResult(); ;

            //Parse response as JSON
            JObject oParams = JObject.Parse(sParams);

            string sCommand = System.Uri.UnescapeDataString(oParams.SelectToken(@"$.psscript").Value<string>()); //get command
            string sInstance = oParams.SelectToken(@"$.instance").Value<string>(); //get instance name
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

                        _hubContext.Clients.Client(sID).SendAsync("returnPSAsync", sCommand, "Host");
                    }
                }
                catch { }
            }

            return new ContentResult();
        }

#if DEBUG
        [AllowAnonymous]
#endif
        [Authorize]
        [HttpPost]
        public object RunPSFile()
        {
            string sParams = "";
            //Load response
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                sParams = reader.ReadToEnd();

            if (string.IsNullOrEmpty(sParams))
                return new ContentResult(); ;

            //Parse response as JSON
            JObject oParams = JObject.Parse(sParams);

            string sFile = System.Uri.UnescapeDataString(oParams.SelectToken(@"$.file").Value<string>()); //get command
            string sInstance = oParams.SelectToken(@"$.instance").Value<string>(); //get instance name
            string sTitle = oParams.SelectToken(@"$.title").Value<string>(); //get title
            
            string sFilePath = Path.Combine(_env.WebRootPath, sFile);

            if (System.IO.File.Exists(sFilePath))
            {
                if (!sFilePath.StartsWith(Path.Combine(_env.WebRootPath, "PSScripts/")))
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
                            _hubContext.Clients.Client(sID).SendAsync("returnPSAsync", sCommand, "Host");
                        }
                    }
                    catch { }
                }
            }

            return new ContentResult();
        }

        internal string SWResults(string Searchstring)
        {
            string sCatFile = @"/App_Data/rzcat.json";
            string sResult = "";
            string sURL = "https://ruckzuck.azurewebsites.net/wcf/RZService.svc";

            sCatFile = Path.Combine(_env.WebRootPath, "rzcat.json");
            //sCatFile = HttpContext.Server.MapPath("~/App_Data/rzcat.json");
            //if (_cache.TryGetValue("SWResult-" + Searchstring, out sResult))
            //{
            //    return sResult;
            //}
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
                var response = oClient.GetStringAsync(sURL + "/rest/SWResults?search=" + Searchstring);
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
}