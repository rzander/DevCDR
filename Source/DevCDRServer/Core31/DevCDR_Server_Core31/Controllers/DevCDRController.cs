using DevCDR.Extensions;
using jaindb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace DevCDRServer.Controllers
{
    [Authorize]
    public class DevCDRController : Controller
    {
        private readonly IHubContext<Default> _hubContext;
        private readonly IWebHostEnvironment _env;
        private IMemoryCache _cache;
        public DevCDR.Extensions.AzureLogAnalytics AzureLog = new DevCDR.Extensions.AzureLogAnalytics("","","");

        public DevCDRController(IHubContext<Default> hubContext, IWebHostEnvironment env, IMemoryCache memoryCache)
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

        [AllowAnonymous]
        public ActionResult Dashboard()
        {
            ViewBag.Title = "Dashboard " + Environment.GetEnvironmentVariable("INSTANCETITLE");
            ViewBag.Instance = Environment.GetEnvironmentVariable("INSTANCENAME");
            ViewBag.appVersion = typeof(DevCDRController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
            ViewBag.Route = "/chat";

            int itotalDeviceCount = -1;

            itotalDeviceCount = new JainDBController(_env, _cache).totalDeviceCount(Path.Combine(_env.WebRootPath, "jaindb\\_Chain"));

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

            if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IP2LocationURL")))
            {
                ViewBag.Location = "Internal IP";
            }
            else
            {
                ViewBag.Location = "Location";
            }
            return View();
        }

        [AllowAnonymous]
        public ActionResult Frame()
        {
            ViewBag.Title = Environment.GetEnvironmentVariable("INSTANCETITLE") ?? "Default Environment";
            ViewBag.Instance = Environment.GetEnvironmentVariable("INSTANCENAME") ?? "Default";
            ViewBag.appVersion = typeof(DevCDRController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
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

        [AllowAnonymous]
        public ActionResult About()
        {
            ViewBag.Message = "Device Commander details...";
            ViewBag.appVersion = typeof(DevCDRController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;

            return View();
        }

        [AllowAnonymous]
        public ActionResult GetVersion()
        {
            return Content(typeof(DevCDRController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version);
        }

        [AllowAnonymous]
        public ActionResult Contact()
        {
            ViewBag.Message = "Device Commander Contact....";
            ViewBag.appVersion = typeof(DevCDRController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;

            return View();
        }

        //Get a File and authenticate with signature
        [AllowAnonymous]
        public ActionResult GetFile(string filename, string signature)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
            {
                X509AgentCert oSig = new X509AgentCert(signature);

                try
                {
                    if (X509AgentCert.publicCertificates.Count == 0)
                        X509AgentCert.publicCertificates.Add(new X509Certificate2(Convert.FromBase64String(GetPublicCertAsync("DeviceCommander", false).Result))); //root

                    var xIssuing = new X509Certificate2(Convert.FromBase64String(GetPublicCertAsync(oSig.IssuingCA, false).Result));
                    if (!X509AgentCert.publicCertificates.Contains(xIssuing))
                        X509AgentCert.publicCertificates.Add(xIssuing); //Issuing

                    oSig.ValidateChain(X509AgentCert.publicCertificates);
                }
                catch { }

                if (oSig.Exists && oSig.Valid)
                {
                    string sScript = GetScriptAsync(oSig.CustomerID, filename).Result;
                    
                    if (string.IsNullOrEmpty(sScript))
                        sScript = GetScriptAsync("DEMO", filename).Result;

                    if (!string.IsNullOrEmpty(sScript))
                    {
                        //replace %ENDPOINTURL% with real Endpoint from Certificate...
                        sScript = sScript.Replace("%ENDPOINTURL%", oSig.EndpointURL.Replace("/chat", ""));
                        return new ContentResult()
                        {
                            Content = sScript,
                            ContentType = "text/plain"
                        };
                    }
                }
            }
            return null;
        }

        [AllowAnonymous]
        public ActionResult GetOSDFiles(string signature, string OS = "")
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
            {
                X509AgentCert oSig = new X509AgentCert(signature);

                try
                {
                    if (X509AgentCert.publicCertificates.Count == 0)
                        X509AgentCert.publicCertificates.Add(new X509Certificate2(Convert.FromBase64String(GetPublicCertAsync("DeviceCommander", false).Result))); //root

                    var xIssuing = new X509Certificate2(Convert.FromBase64String(GetPublicCertAsync(oSig.IssuingCA, false).Result));
                    if (!X509AgentCert.publicCertificates.Contains(xIssuing))
                        X509AgentCert.publicCertificates.Add(xIssuing); //Issuing

                    oSig.ValidateChain(X509AgentCert.publicCertificates);
                }
                catch { }

                if (oSig.Exists && oSig.Valid)
                {
                    string sScript = GetOSDAsync(oSig.CustomerID, OS).Result;

                    //if (string.IsNullOrEmpty(sScript))
                    //    sScript = GetOSDAsync("DEMO").Result;

                    if (!string.IsNullOrEmpty(sScript))
                    {
                        return new ContentResult()
                        {
                            Content = sScript,
                            ContentType = "text/plain"
                        };
                    }
                }
                else
                {
                    if(oSig.Expired) //allow if cert is expired in the last 7 days
                    {
                        if((DateTime.UtcNow - oSig.Certificate.NotAfter).TotalDays < 7)
                        {
                            string sScript = GetOSDAsync(oSig.CustomerID, OS).Result;

                            //if (string.IsNullOrEmpty(sScript))
                            //    sScript = GetOSDAsync("DEMO").Result;

                            if (!string.IsNullOrEmpty(sScript))
                            {
                                return new ContentResult()
                                {
                                    Content = sScript,
                                    ContentType = "text/plain"
                                };
                            }
                        }
                    }
                }
            }
            return BadRequest();
        }

        [AllowAnonymous]
        public ActionResult GetOS(string signature)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
            {
                X509AgentCert oSig = new X509AgentCert(signature);

                try
                {
                    if (X509AgentCert.publicCertificates.Count == 0)
                        X509AgentCert.publicCertificates.Add(new X509Certificate2(Convert.FromBase64String(GetPublicCertAsync("DeviceCommander", false).Result))); //root

                    var xIssuing = new X509Certificate2(Convert.FromBase64String(GetPublicCertAsync(oSig.IssuingCA, false).Result));
                    if (!X509AgentCert.publicCertificates.Contains(xIssuing))
                        X509AgentCert.publicCertificates.Add(xIssuing); //Issuing

                    oSig.ValidateChain(X509AgentCert.publicCertificates);
                }
                catch { }

                if (oSig.Exists && oSig.Valid)
                {
                    string sScript = GetOSAsync(oSig.CustomerID).Result;

                    if (!string.IsNullOrEmpty(sScript))
                    {
                        return new ContentResult()
                        {
                            Content = sScript,
                            ContentType = "text/plain"
                        };
                    }
                }
                else
                {
                    if (oSig.Expired) //allow if cert is expired in the last 7 days
                    {
                        if ((DateTime.UtcNow - oSig.Certificate.NotAfter).TotalDays < 7)
                        {
                            string sScript = GetOSAsync(oSig.CustomerID).Result;

                            if (!string.IsNullOrEmpty(sScript))
                            {
                                return new ContentResult()
                                {
                                    Content = sScript,
                                    ContentType = "text/plain"
                                };
                            }
                        }
                    }
                }
            }
            return BadRequest();
        }


        private async Task<string> GetPublicCertAsync(string CertName, bool useKey = true)
        {
            if (string.IsNullOrEmpty(CertName))
                return "";

            if (_cache == null)
                _cache = new MemoryCache(new MemoryCacheOptions());

            bool bCached = false;
            _cache.TryGetValue(CertName, out string Cert);
            if (string.IsNullOrEmpty(Cert))
            {
                try
                {
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                    {
                        using (HttpClient client = new HttpClient())
                        {
                            string sURL = Environment.GetEnvironmentVariable("fnDevCDR");
                            sURL = sURL.Replace("{fn}", "fnGetPublicCert");

                            if (useKey)
                            {
                                var stringTask = client.GetStringAsync($"{sURL}&key={CertName}");
                                Cert = await stringTask;
                            }
                            else
                            {
                                var stringTask = client.GetStringAsync($"{sURL}&name={CertName}");
                                Cert = await stringTask;
                            }
                        }
                    }
                }
                catch { return Cert; }
            }
            else
            {
                bCached = true;
            }

            if (!string.IsNullOrEmpty(Cert))
            {
                if (!bCached)
                {
                    var cacheEntryOptions = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(15)); //cache hash for x Seconds
                    _cache.Set(CertName, Cert, cacheEntryOptions);
                }
            }

            return Cert;
        }

        private async Task<string> GetScriptAsync(string customerid, string filename)
        {
            string Res = "";
            if (string.IsNullOrEmpty(filename))
                return null;
            if (string.IsNullOrEmpty(customerid))
                return null;

            if (_cache == null)
                _cache = new MemoryCache(new MemoryCacheOptions());

            _cache.TryGetValue(customerid + filename, out Res);

            if(!string.IsNullOrEmpty(Res))
            {
                return Res;
            }

            try
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                {
                    string sURL = Environment.GetEnvironmentVariable("fnDevCDR");
                    sURL = sURL.Replace("{fn}", "fnGetFile");

                    using (HttpClient client = new HttpClient())
                    {
                        Res = await client.GetStringAsync($"{sURL}&customerid={customerid}&file={filename}");

                        if(!string.IsNullOrEmpty(Res))
                        {
                            var cacheEntryOptions = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(5)); //cache hash for x Seconds
                            _cache.Set(customerid + filename, Res, cacheEntryOptions);
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                ex.Message.ToString();
                return Res;
            }


            return Res;
        }

        private async Task<string> GetOSDAsync(string customerid, string os = "")
        {
            string Res = "";
            if (string.IsNullOrEmpty(customerid))
                return null;

            if (_cache == null)
                _cache = new MemoryCache(new MemoryCacheOptions());

            _cache.TryGetValue("osd" + customerid + os, out Res);

            if (!string.IsNullOrEmpty(Res))
            {
                return Res;
            }

            try
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                {
                    string sURL = Environment.GetEnvironmentVariable("fnDevCDR");
                    sURL = sURL.Replace("{fn}", "fnGetOSDFiles");

                    using (HttpClient client = new HttpClient())
                    {
                        Res = await client.GetStringAsync($"{sURL}&customerid={customerid}&os={os}");

                        if (!string.IsNullOrEmpty(Res))
                        {
                            var cacheEntryOptions = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(15));
                            _cache.Set("osd" +customerid + os, Res, cacheEntryOptions);
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                ex.Message.ToString();
                return Res;
            }


            return Res;
        }

        private async Task<string> GetOSAsync(string customerid)
        {
            string Res = "";
            if (string.IsNullOrEmpty(customerid))
                return null;

            if (_cache == null)
                _cache = new MemoryCache(new MemoryCacheOptions());

            _cache.TryGetValue("os" + customerid, out Res);

            if (!string.IsNullOrEmpty(Res))
            {
                return Res;
            }

            try
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                {
                    string sURL = Environment.GetEnvironmentVariable("fnDevCDR");
                    sURL = sURL.Replace("{fn}", "fnGetSumData");

                    using (HttpClient client = new HttpClient())
                    {
                        Res = await client.GetStringAsync($"{sURL}&secretname=AzureDevCDRCustomersTable&customerid={customerid}");

                        if (!string.IsNullOrEmpty(Res))
                        {
                            try
                            {
                                JArray jRes = JArray.Parse(Res);
                                string sOS = jRes[0]["OS"].Value<string>();
                                var cacheEntryOptions = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(15));
                                _cache.Set("os" + customerid, sOS, cacheEntryOptions);

                                return sOS;
                            }
                            catch { }
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                return "";
            }


            return "";
        }

        [AllowAnonymous]
        [HttpPost]
        public async Task<string> PutFileAsync(string signature, string data = "")
        {
            X509AgentCert oSig = new X509AgentCert(signature);

            try
            {
                if (X509AgentCert.publicCertificates.Count == 0)
                    X509AgentCert.publicCertificates.Add(new X509Certificate2(Convert.FromBase64String(GetPublicCertAsync("DeviceCommander", false).Result))); //root

                var xIssuing = new X509Certificate2(Convert.FromBase64String(GetPublicCertAsync(oSig.IssuingCA, false).Result));
                if (!X509AgentCert.publicCertificates.Contains(xIssuing))
                    X509AgentCert.publicCertificates.Add(xIssuing); //Issuing

                oSig.ValidateChain(X509AgentCert.publicCertificates);
            }
            catch { }

            if (oSig.Exists && oSig.Valid)
            {
                try
                {
                    if (string.IsNullOrEmpty(data))
                    {
                        using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                            data = reader.ReadToEndAsync().Result;
                    }

                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                    {
                        string sURL = Environment.GetEnvironmentVariable("fnDevCDR");
                        sURL = sURL.Replace("{fn}", "fnUploadFile");

                        string sNewData = data;
                        try
                        {
                            if (data.StartsWith("{"))
                            {
                                var jObj = JObject.Parse(data);

                                jObj.Remove("OptionalFeature");
                                if (jObj.Remove("Services"))
                                {
                                    sNewData = jObj.ToString(Formatting.None);
                                }
                            }
                        }
                        catch { }

                        using (HttpClient client = new HttpClient())
                        {
                            StringContent oData = new StringContent(sNewData, Encoding.UTF8, "application/json");
                            await client.PostAsync($"{sURL}&deviceid={oSig.DeviceID}.json&customerid={oSig.CustomerID}", oData);
                        }

                        return jDB.UploadFull(data, oSig.DeviceID, "INV");

                    }
                }
                catch (Exception ex)
                {
                    ex.Message.ToString();
                    return null;
                }
            }

            return null;
        }

        //#if DEBUG
        //        [AllowAnonymous]
        //#endif
        [AllowAnonymous]
        public ActionResult GetData(string Group = "", string Instance = "Default")
        {
            JArray jData = new JArray();
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MemberInfo[] memberInfos = xType.GetMember("jData", BindingFlags.Public | BindingFlags.Static);

                jData = ((FieldInfo)memberInfos[0]).GetValue(new JArray()) as JArray;
                if(!string.IsNullOrEmpty(Group))
                    jData = new JArray(jData.SelectTokens($"$.[?(@.Groups=='{ Group }')]"));
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

        //#if DEBUG
        //        [AllowAnonymous]
        //#endif
        [AllowAnonymous]
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

        //#if DEBUG
        //        [AllowAnonymous]
        //#endif
        [AllowAnonymous]
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

        //#if DEBUG
        //        [AllowAnonymous]
        //#endif
        [AllowAnonymous]
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
                    sendWOL(lHostnames, sInstance, GetAllMACAddresses());
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

        internal List<string> GetAllMACAddresses()
        {
            List<string> lResult = new List<string>();
            var tItems = new JainDBController(_env, _cache).Query("$select=@MAC");
            JArray jMacs = tItems.Result;

            foreach(var jTok in jMacs.SelectTokens("..@MAC"))
            {
                if (jTok.Type == JTokenType.String)
                    lResult.Add(jTok.Value<string>());
                if (jTok.Type == JTokenType.Array)
                    lResult.AddRange(jTok.Values<string>().ToList());
            }

            return lResult;
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
                    AzureLog.PostAsync(new { Computer = sHost, EventID = 2009, Description = $"Set Agent Groups: {Args}" });
                    _hubContext.Clients.Client(sID).SendAsync("setgroups", Args);
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
                    AzureLog.PostAsync(new { Computer = sHost, EventID = 2007, Description = $"set new Endpoint: {Args}" });
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
                    AzureLog.PostAsync(new { Computer = sHost, EventID = 2006, Description = $"restart Agent" });
                    _hubContext.Clients.Client(sID).SendAsync("restartservice", "HUB");
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
                    AzureLog.PostAsync(new { Computer = sHost, EventID = 2005, Description = $"install RuckZuck software: {Args}" });
                    _hubContext.Clients.Client(sID).SendAsync("rzinstall", Args);
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
                    AzureLog.PostAsync(new { Computer = sHost, EventID = 2002, Description = $"WakeUp all devices" });
                    _hubContext.Clients.Client(sID).SendAsync("wol", string.Join(';', MAC));
                }
            }
        }

        //#if DEBUG
        //        [AllowAnonymous]
        //#endif
        [AllowAnonymous]
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

        //#if DEBUG
        //        [AllowAnonymous]
        //#endif
        [AllowAnonymous]
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

        //#if DEBUG
        //        [AllowAnonymous]
        //#endif
        [AllowAnonymous]
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

        //#if DEBUG
        //        [AllowAnonymous]
        //#endif
        [AllowAnonymous]
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
}