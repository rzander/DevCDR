using jaindb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DevCDRServer.Controllers
{
    [Route("jaindb")]
    public class JainDBController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private IMemoryCache _cache;

        public JainDBController(IWebHostEnvironment env, IMemoryCache memoryCache)
        {
            _env = env;
            _cache = memoryCache;
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("")]
        [Route("About")]
        public ActionResult About()
        {
            ViewBag.appVersion = typeof(JainDBController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
            ViewBag.Message = "JainDB running on Device Commander";
            return View("About");
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("upload/{param}")]
        public string Upload(string param, string blockType = "INV")
        {

            _cache.Remove("totalDeviceCount");

            jDB.FilePath = Path.Combine(_env.WebRootPath, "jaindb");

            string sParams = "";
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                sParams = reader.ReadToEndAsync().Result;

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JainDBURL")))
            {
                return jDB.UploadFull(sParams, param, blockType);
            }
            else
            {
                using (HttpClient oClient = new HttpClient())
                {
                    oClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    HttpContent oCont = new StringContent(sParams);
                    var response = oClient.PostAsync((Environment.GetEnvironmentVariable("JainDBURL") + "/upload/" + param), oCont );
                    response.Wait(180000);
                    if (response.IsCompleted)
                    {
                        return response.Result.Content.ReadAsStringAsync().Result;
                    }
                }
            }

            return "";
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("GetPS")]
        [Route("GetPS/{filename}")]
        public string GetPS(string filename = "")
        {
            string sResult = "";
            string spath = Path.Combine(_env.WebRootPath, "jaindb");
            jaindb.jDB.FilePath = spath;
            if (string.IsNullOrEmpty(filename))
            {
                Request.ToString();
                string sLocalURL = Request.GetEncodedUrl().Replace("/getps", "");
                if (System.IO.File.Exists(Path.Combine(spath, "inventory.ps1")))
                {

                    string sFile = System.IO.File.ReadAllText(spath + "/inventory.ps1");
                    sResult = sFile.Replace("%LocalURL%", sLocalURL).Replace(":%WebPort%", "");

                    return sResult;
                }
            }
            else
            {
                string sLocalURL = Request.GetDisplayUrl().Substring(0, Request.GetDisplayUrl().IndexOf("/getps"));
                if (System.IO.File.Exists(spath + "/" + filename))
                {
                    string sFile = System.IO.File.ReadAllText(spath + "/" + filename);
                    sResult = sFile.Replace("%LocalURL%", sLocalURL).Replace(":%WebPort%", "");

                    return sResult;
                }
            }

            return sResult;

        }

        [HttpGet]
        [BasicAuthenticationAttribute()]
        [Route("full")]
        public JObject Full(string blockType = "INV")
        {
            //string sPath = this.Request.Path;
            string spath = Path.Combine(_env.WebRootPath, "jaindb");
            jDB.FilePath = spath;

            string sQuery = this.Request.QueryString.ToString();

            var query = System.Web.HttpUtility.ParseQueryString(sQuery);
            string sKey = query["id"];

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JainDBURL")))
            {
                if (string.IsNullOrEmpty(sKey))
                    sKey = jDB.LookupID(query.Keys[0], query.GetValues(0)[0]);
                //int index = -1;
                if (!int.TryParse(query["index"], out int index))
                    index = -1;
                if (!string.IsNullOrEmpty(sKey))
                    return jDB.GetFull(sKey, index, blockType);
                else
                    return null;
            }
            else
            {
                if (!string.IsNullOrEmpty(sKey))
                {
                    using (HttpClient oClient = new HttpClient())
                    {
                        oClient.DefaultRequestHeaders.Clear();
                        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REPORTUSER")))
                        {
                            var byteArray = Encoding.ASCII.GetBytes(Environment.GetEnvironmentVariable("REPORTUSER") + ":" + Environment.GetEnvironmentVariable("REPORTPASSWORD"));
                            oClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                        }
                        var response = oClient.GetStringAsync(Environment.GetEnvironmentVariable("JainDBURL") + "/full?id=" + sKey);
                        response.Wait(180000);
                        if (response.IsCompleted)
                        {
                            return JObject.Parse(response.Result);
                        }
                    }
                }
                else
                {
                    using (HttpClient oClient = new HttpClient())
                    {
                        var response = oClient.GetStringAsync(Environment.GetEnvironmentVariable("JainDBURL") + "/full?" + query.Keys[0] + "=" + query.GetValues(0)[0]);
                        response.Wait(15000);
                        if (response.IsCompleted)
                        {
                            return JObject.Parse(response.Result);
                        }
                    }
                }
            }

            return null;
        }

#if DEBUG
        [AllowAnonymous]
#endif
        [HttpGet]
        [BasicAuthenticationAttribute()]
        [Route("query")]
        public async System.Threading.Tasks.Task<JArray> Query(string sParams = "")
        {
            DateTime dStart = DateTime.Now;

            string sPath = Path.Combine(_env.WebRootPath, "jaindb");
            jDB.FilePath = sPath;

            string sQuery = sParams;

            if (string.IsNullOrEmpty(sParams))
                sQuery = this.Request.QueryString.ToString();
                        
            if (sPath != "/favicon.ico")
            {
                //string sUri = Microsoft.AspNetCore.Http.Extensions.UriHelper.GetDisplayUrl(Request);
                var query = System.Web.HttpUtility.ParseQueryString(sQuery);

                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JainDBURL")))
                {
                    string qpath = (query[null] ?? "").Replace(',', ';');
                    string qsel = (query["$select"] ?? "").Replace(',', ';');
                    string qexc = (query["$exclude"] ?? "").Replace(',', ';');
                    string qwhe = (query["$where"] ?? "").Replace(',', ';');
                    return await jDB.QueryAsync(qpath, qsel, qexc, qwhe);
                }
                else
                {
                    using (HttpClient oClient = new HttpClient())
                    {
                        oClient.DefaultRequestHeaders.Clear();
                        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REPORTUSER")))
                        {
                            var byteArray = Encoding.ASCII.GetBytes(Environment.GetEnvironmentVariable("REPORTUSER") + ":" + Environment.GetEnvironmentVariable("REPORTPASSWORD"));
                            oClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                        }
                        var response = oClient.GetStringAsync(Environment.GetEnvironmentVariable("JainDBURL") + "/query?" + sQuery );
                        response.Wait(15000);
                        if (response.IsCompleted)
                        {
                            return JArray.Parse(response.Result);
                        }
                    }
                }
                    
            }
            return null;
        }

        [HttpGet]
        [BasicAuthenticationAttribute()]
        [Route("totalDeviceCount")]
        public int totalDeviceCount(string sPath = "")
        {
            int iCount = -1;

            //Check if MemoryCache is initialized
            if (_cache == null)
            {
                _cache = new MemoryCache(new MemoryCacheOptions());
            }

            //Check in MemoryCache
            if (_cache.TryGetValue("totalDeviceCount", out iCount))
            {
                return iCount;
            }

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JainDBURL")))
            {
                return jDB.totalDeviceCount(sPath);
            }
            else
            {
                using (HttpClient oClient = new HttpClient())
                {
                    oClient.DefaultRequestHeaders.Clear();
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REPORTUSER")))
                    {
                        var byteArray = Encoding.ASCII.GetBytes(Environment.GetEnvironmentVariable("REPORTUSER") + ":" + Environment.GetEnvironmentVariable("REPORTPASSWORD"));
                        oClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                    }
                    var response = oClient.GetStringAsync(Environment.GetEnvironmentVariable("JainDBURL") + "/totalDeviceCount");
                    response.Wait(15000);
                    if (response.IsCompleted)
                    {
                        iCount = int.Parse(response.Result);

                        var cacheEntryOptions = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromSeconds(60)); //cache ID for 60s
                        _cache.Set("totalDeviceCount", iCount, cacheEntryOptions);

                        return iCount;

                    }
                }
            }

            return -1;
        }

#if DEBUG
        [AllowAnonymous]
#endif
        [Authorize]
        [Route("inv")]
        [HttpGet]
        public ActionResult Inv(string id, string name = "", int index = -1, string blockType = "INV")
        {
            ViewBag.appVersion = typeof(DevCDRController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;

            JObject oInv = new JObject();
            string spath = Path.Combine(_env.WebRootPath, "jaindb");

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JainDBURL")))
            {
                jaindb.jDB.FilePath = spath;

                if (!string.IsNullOrEmpty(name))
                    id = jDB.LookupID("name", name);

                if (string.IsNullOrEmpty(id))
                    return Redirect("../DevCdr/Dashboard");

                oInv = jDB.GetFull(id, index, blockType);

            }
            else
            {
                using (HttpClient oClient = new HttpClient())
                {
                    oClient.DefaultRequestHeaders.Clear();
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REPORTUSER")))
                    {
                        var byteArray = Encoding.ASCII.GetBytes(Environment.GetEnvironmentVariable("REPORTUSER") + ":" + Environment.GetEnvironmentVariable("REPORTPASSWORD"));
                        oClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                    }


                    var response = oClient.GetStringAsync(Environment.GetEnvironmentVariable("JainDBURL") + "/full?id=" + id);
                    response.Wait(180000);
                    if (response.IsCompleted)
                    {
                        oInv = JObject.Parse(response.Result);
                    }
                }
            }

            if (oInv.ToString() != (new JObject()).ToString())
            {
                ViewBag.Id = id;

                try
                {
                    string sInstance = "Default"; // oInv["DevCDRInstance"].ToString();
                    try
                    {
                        TimeSpan tDiff = DateTime.Now.ToUniversalTime() - (DateTime)oInv["_date"];
                        if (tDiff.TotalDays >= 2)
                            ViewBag.LastInv = ((int)tDiff.TotalDays).ToString() + " days";
                        else
                        {
                            if ((tDiff.TotalHours >= 1))
                                ViewBag.LastInv = ((int)tDiff.TotalHours).ToString() + " hours";
                            else
                                ViewBag.LastInv = ((int)tDiff.TotalMinutes).ToString() + " minutes";
                        }
                    }
                    catch
                    {
                        ViewBag.LastInv = oInv["_date"];
                    }

                    try
                    {
                        ViewBag.Id = id;
                        ViewBag.Index = oInv["_index"];
                        ViewBag.idx = index;
                        ViewBag.Type = oInv["_type"] ?? "INV";
                        ViewBag.OS = oInv["OS"]["Caption"];
                        ViewBag.Name = oInv["Computer"].SelectTokens("..#Name").FirstOrDefault().ToString();
                        ViewBag.Title = oInv["Computer"].SelectTokens("..#Name").FirstOrDefault().ToString();
                        ViewBag.UserName = (oInv["Computer"].SelectTokens("..@UserName").FirstOrDefault() ?? "").ToString();
                        ViewBag.Vendor = (oInv["Computer"].SelectTokens("..Manufacturer").FirstOrDefault() ?? oInv["BIOS"].SelectTokens("..Manufacturer").FirstOrDefault() ?? "unknown").ToString();
                        ViewBag.Serial = (oInv["Computer"].SelectTokens("..#SerialNumber").FirstOrDefault() ?? oInv["BIOS"].SelectTokens("..#SerialNumber").FirstOrDefault() ?? "unknown").ToString();
                        ViewBag.Version = oInv["OS"]["Version"];
                        ViewBag.InstDate = oInv["OS"]["#InstallDate"];
                        ViewBag.LastBoot = oInv["OS"]["@LastBootUpTime"];
                        ViewBag.Model = (oInv["Computer"].SelectTokens("..Model").FirstOrDefault() ?? "unknown").ToString();
                        ViewBag.Language = oInv["OS"]["OSLanguage"].ToString();
                        ViewBag.Arch = oInv["OS"]["OSArchitecture"];
                        try
                        {
                            if(oInv["Processor"].Type == JTokenType.Object)
                                ViewBag.CPU = oInv["Processor"]["Name"];

                            if (oInv["Processor"].Type == JTokenType.Array)
                                ViewBag.CPU = oInv["Processor"][0]["Name"];
                        }
                        catch { }

                        ViewBag.Memory = Convert.ToInt32((long)(oInv["Computer"].SelectTokens("..TotalPhysicalMemory").FirstOrDefault() ?? 0) / 1000 / 1000 / 1000);
                        switch (ViewBag.Memory)
                        {
                            case 17:
                                ViewBag.Memory = 16;
                                break;
                            case 34:
                                ViewBag.Memory = 32;
                                break;
                            case 35:
                                ViewBag.Memory = 32;
                                break;
                            case 68:
                                ViewBag.Memory = 64;
                                break;
                            case 69:
                                ViewBag.Memory = 64;
                                break;
                        }
                    }
                    catch { }

                    if (!int.TryParse((oInv["Computer"].SelectTokens("..ChassisTypes").FirstOrDefault() ?? "")[0].ToString() ?? "2", out int chassis))
                        chassis = 0;

                    var oSW = oInv["Software"];
                    var oIndSW = oInv.DeepClone()["Software"];

                    List<string> lCoreApps = new List<string>();
                    List<string> lIndSW = new List<string>();
                    List<string> lUnknSW = new List<string>();

                    //Check if device has all required SW installed
                    #region CoreApplications 
                    ViewBag.CoreStyle = "alert-danger";
                    try
                    {
                        var aCoreApps = System.IO.File.ReadAllLines(Path.Combine(spath, "CoreApps_" + sInstance + ".txt"));
                        ViewBag.CoreStyle = "alert-success";
                        foreach (string sCoreApp in aCoreApps)
                        {
                            try
                            {
                                var oRes = oInv.SelectTokens(sCoreApp);
                                if (oRes.Count() > 0)
                                {
                                    lCoreApps.Add("<p class=\"col-xs-offset-1 alert-success\">" + oRes.First()["DisplayName"].ToString() + "</p>");

                                    foreach (var oItem in oRes.ToList())
                                    {
                                        oItem.Remove();
                                    }
                                }
                                else
                                {
                                    ViewBag.CoreStyle = "alert-warning";
                                    lCoreApps.Add("<p class=\"col-xs-offset-1 alert-warning\">" + sCoreApp + "</p>");
                                }
                            }
                            catch
                            {
                                ViewBag.CoreStyle = "alert-warning";
                                lCoreApps.Add("<p class=\"col-xs-offset-1 alert-warning\">" + sCoreApp + "</p>");
                            }
                        }
                    }
                    catch { }
                    #endregion

                    #region IndividualSW 
                    try
                    {
                        var aIndApps = System.IO.File.ReadAllLines(Path.Combine(spath, "IndSW_" + sInstance + ".txt")).ToList();
                        foreach (var xSW in oInv["Software"])
                        {
                            if (aIndApps.Contains(xSW["DisplayName"].ToString().TrimEnd()))
                            {
                                lIndSW.Add("<p class=\"col-xs-offset-1 alert-info\">" + xSW["DisplayName"].ToString().TrimEnd() + " ; " + (xSW["DisplayVersion"] ?? "").ToString().Trim() + "</p>");
                            }
                            else
                            {
                                lUnknSW.Add("<p class=\"col-xs-offset-1 alert-danger\">" + xSW["DisplayName"].ToString().TrimEnd() + " ; " + (xSW["DisplayVersion"] ?? "").ToString().Trim() + "</p>");
                            }
                        }
                    }
                    catch { }


                    #endregion

                    ViewBag.CoreSW = lCoreApps.Distinct().OrderBy(t => t);
                    ViewBag.IndSW = lIndSW.Distinct().OrderBy(t => t);
                    ViewBag.UnknSW = lUnknSW.Distinct().OrderBy(t => t);
                    ViewBag.IndSWc = lIndSW.Distinct().OrderBy(t => t).Count();
                    ViewBag.UnknSWc = lUnknSW.Distinct().OrderBy(t => t).Count();

                    //https://docs.microsoft.com/en-us/previous-versions/tn-archive/ee156537(v=technet.10)
                    switch (chassis)
                    {
                        case 1:
                            ViewBag.Type = "Other";
                            break;
                        case 2:
                            ViewBag.Type = "Unknwon";
                            break;
                        case 3:
                            ViewBag.Type = "Dekstop";
                            break;
                        case 4:
                            ViewBag.Type = "Low Profile Desktop";
                            break;
                        case 6:
                            ViewBag.Type = "Mini Tower";
                            break;
                        case 7:
                            ViewBag.Type = "Tower";
                            break;
                        case 9:
                            ViewBag.Type = "Laptop";
                            break;
                        case 10:
                            ViewBag.Type = "Notebook";
                            break;
                        case 13:
                            ViewBag.Type = "All in One";
                            break;
                        case 14:
                            ViewBag.Type = "Sub Notebook";
                            break;
                        case 30:
                            ViewBag.Type = "Tablet";
                            break;
                        case 31:
                            ViewBag.Type = "Convertible";
                            break;
                        default:
                            ViewBag.Type = "Other";
                            break;
                    }

                    switch (ViewBag.Language)
                    {
                        case "1033":
                            ViewBag.Language = "English - United States";
                            break;
                        case "2057":
                            ViewBag.Language = "English - Great Britain";
                            break;
                        case "1031":
                            ViewBag.Language = "German";
                            break;
                        case "1036":
                            ViewBag.Language = "French";
                            break;
                        case "1040":
                            ViewBag.Language = "Italian";
                            break;
                        case "1034":
                            ViewBag.Language = "Spanish";
                            break;
                    }

                    if (ViewBag.Model == "Virtual Machine")
                        ViewBag.Type = "VM";

                    if (((string)ViewBag.Vendor).ToLower() == "lenovo")
                        ViewBag.Model = oInv["Computer"]["SystemFamily"] ?? "unknown";
                }
                catch { }

                return View();
            }

            return Redirect("../DevCdr/Dashboard");
        }

        [TokenAuthentication]
        [Route("invFrame")]
        [HttpGet]
        public ActionResult InvFrame(string id, string name = "", int index = -1, string blockType = "INV")
        {
            return Inv(id, name, index, blockType);
        }

#if DEBUG
        [AllowAnonymous]
#endif
        [Authorize]
        [Route("diff")]
        [HttpGet]
        public ActionResult Diff(string id, int l = -1, int r = -1)
        {
            ViewBag.appVersion = typeof(DevCDRController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
            string spath = Path.Combine(_env.WebRootPath, "jaindb");
            jaindb.jDB.FilePath = spath;
            JObject oL = new JObject();
            JObject oR = new JObject();

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JainDBURL")))
            {
                oR = jDB.GetFull(id, r);
                if (l == -1)
                {
                    try
                    {
                        l = (int)oR["_index"] - 1;
                    }
                    catch { }
                }
                oL = jDB.GetFull(id, l);
            }
            else
            {
                using (HttpClient oClient = new HttpClient())
                {
                    oClient.DefaultRequestHeaders.Clear();
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REPORTUSER")))
                    {
                        var byteArray = Encoding.ASCII.GetBytes(Environment.GetEnvironmentVariable("REPORTUSER") + ":" + Environment.GetEnvironmentVariable("REPORTPASSWORD"));
                        oClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                    }
                    var response = oClient.GetStringAsync(Environment.GetEnvironmentVariable("JainDBURL") + "/full?id=" + id + "&index=" + r.ToString());
                    response.Wait(180000);
                    if (response.IsCompleted)
                    {
                        oR = JObject.Parse(response.Result);
                    }

                    if (l == -1)
                    {
                        try
                        {
                            l = (int)oR["_index"] - 1;
                        }
                        catch { }
                    }

                    response = oClient.GetStringAsync(Environment.GetEnvironmentVariable("JainDBURL") + "/full?id=" + id + "&index=" + l.ToString());
                    response.Wait(15000);
                    if (response.IsCompleted)
                    {
                        oL = JObject.Parse(response.Result);
                    }
                }
            }

            if (!string.IsNullOrEmpty(id))
            {
                //remove all @ attributes
                foreach (var oKey in oL.Descendants().Where(t => t.Type == JTokenType.Property && ((JProperty)t).Name.StartsWith("@")).ToList())
                {
                    try
                    {
                        oKey.Remove();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                    }
                }

                //remove all @ attributes
                foreach (var oKey in oR.Descendants().Where(t => t.Type == JTokenType.Property && ((JProperty)t).Name.StartsWith("@")).ToList())
                {
                    try
                    {
                        oKey.Remove();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                    }
                }

                ViewBag.jsonR = oR.ToString(Formatting.Indented);
                ViewBag.jsonL = oL.ToString(Formatting.Indented);
                ViewBag.History = GetHistory(id).ToString(Formatting.Indented);
                ViewBag.Id = id;
            }
            return View("Diff");
        }

        [TokenAuthentication]
        [Route("diffFrame")]
        [HttpGet]
        public ActionResult DiffFrame(string id, int l = -1, int r = -1)
        {
            ViewBag.appVersion = typeof(DevCDRController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
            string spath = Path.Combine(_env.WebRootPath, "jaindb");
            jaindb.jDB.FilePath = spath;
            JObject oL = new JObject();
            JObject oR = new JObject();

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JainDBURL")))
            {
                oR = jDB.GetFull(id, r);
                if (l == -1)
                {
                    try
                    {
                        l = (int)oR["_index"] - 1;
                    }
                    catch { }
                }
                oL = jDB.GetFull(id, l);
            }
            else
            {
                using (HttpClient oClient = new HttpClient())
                {
                    oClient.DefaultRequestHeaders.Clear();
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REPORTUSER")))
                    {
                        var byteArray = Encoding.ASCII.GetBytes(Environment.GetEnvironmentVariable("REPORTUSER") + ":" + Environment.GetEnvironmentVariable("REPORTPASSWORD"));
                        oClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                    }
                    var response = oClient.GetStringAsync(Environment.GetEnvironmentVariable("JainDBURL") + "/full?id=" + id + "&index=" + r.ToString());
                    response.Wait(180000);
                    if (response.IsCompleted)
                    {
                        oR = JObject.Parse(response.Result);
                    }

                    if (l == -1)
                    {
                        try
                        {
                            l = (int)oR["_index"] - 1;
                        }
                        catch { }
                    }

                    response = oClient.GetStringAsync(Environment.GetEnvironmentVariable("JainDBURL") + "/full?id=" + id + "&index=" + l.ToString());
                    response.Wait(15000);
                    if (response.IsCompleted)
                    {
                        oL = JObject.Parse(response.Result);
                    }
                }
            }

            if (!string.IsNullOrEmpty(id))
            {
                //remove all @ attributes
                foreach (var oKey in oL.Descendants().Where(t => t.Type == JTokenType.Property && ((JProperty)t).Name.StartsWith("@")).ToList())
                {
                    try
                    {
                        oKey.Remove();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                    }
                }

                //remove all @ attributes
                foreach (var oKey in oR.Descendants().Where(t => t.Type == JTokenType.Property && ((JProperty)t).Name.StartsWith("@")).ToList())
                {
                    try
                    {
                        oKey.Remove();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                    }
                }

                ViewBag.jsonR = oR.ToString(Formatting.Indented);
                ViewBag.jsonL = oL.ToString(Formatting.Indented);
                ViewBag.History = GetHistory(id).ToString(Formatting.Indented);
                ViewBag.Id = id;
            }
            return View("DiffFrame");
        }

#if DEBUG
        [AllowAnonymous]
#endif
        [Authorize]
        [Route("invjson")]
        [HttpGet]
        public ActionResult InvJson(string id, int l = -1, string blockType = "INV")
        {
            ViewBag.appVersion = typeof(DevCDRController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
            string spath = Path.Combine(_env.WebRootPath, "jaindb");
            jaindb.jDB.FilePath = spath;

            if (!string.IsNullOrEmpty(id))
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JainDBURL")))
                {
                    var oL = jDB.GetFull(id, l, blockType);
                    ViewBag.jsonL = oL.ToString(Formatting.None);
                }
                else
                {
                    using (HttpClient oClient = new HttpClient())
                    {
                        oClient.DefaultRequestHeaders.Clear();
                        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REPORTUSER")))
                        {
                            var byteArray = Encoding.ASCII.GetBytes(Environment.GetEnvironmentVariable("REPORTUSER") + ":" + Environment.GetEnvironmentVariable("REPORTPASSWORD"));
                            oClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                        }
                        var response = oClient.GetStringAsync(Environment.GetEnvironmentVariable("JainDBURL") + "/full?id=" + id + "&index=" + l);
                        response.Wait(180000);
                        if (response.IsCompleted)
                        {
                            var oL = JObject.Parse(response.Result);
                            ViewBag.jsonL = oL.ToString(Formatting.None);
                        }
                    }
                }


            }
            return View("InvJson");
        }

        [TokenAuthentication]
        [Route("invjsonframe")]
        [HttpGet]
        public ActionResult InvJsonFrame(string id, int l = -1, string blockType = "INV")
        {
            ViewBag.appVersion = typeof(DevCDRController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
            string spath = Path.Combine(_env.WebRootPath, "jaindb");
            jaindb.jDB.FilePath = spath;

            if (!string.IsNullOrEmpty(id))
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JainDBURL")))
                {
                    var oL = jDB.GetFull(id, l, blockType);
                    ViewBag.jsonL = oL.ToString(Formatting.None);
                }
                else
                {
                    using (HttpClient oClient = new HttpClient())
                    {
                        oClient.DefaultRequestHeaders.Clear();
                        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REPORTUSER")))
                        {
                            var byteArray = Encoding.ASCII.GetBytes(Environment.GetEnvironmentVariable("REPORTUSER") + ":" + Environment.GetEnvironmentVariable("REPORTPASSWORD"));
                            oClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                        }
                        var response = oClient.GetStringAsync(Environment.GetEnvironmentVariable("JainDBURL") + "/full?id=" + id + "&index=" + l);
                        response.Wait(180000);
                        if (response.IsCompleted)
                        {
                            var oL = JObject.Parse(response.Result);
                            ViewBag.jsonL = oL.ToString(Formatting.None);
                        }
                    }
                }


            }
            return View("InvJsonFrame");
        }

        [Authorize]
        [HttpGet]
        [Route("GetHistory/{id}")]
        public JArray GetHistory(string id, string blockType = "INV")
        {
            string spath = Path.Combine(_env.WebRootPath, "jaindb");
            jaindb.jDB.FilePath = spath;

            string sQuery = this.Request.QueryString.ToString();

            var query = System.Web.HttpUtility.ParseQueryString(sQuery);
            string sKey = query["id"] ?? id;

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JainDBURL")))
            {
                if (!string.IsNullOrEmpty(sKey))
                    return jDB.GetJHistory(sKey, blockType);
            }
            else
            {
                using (HttpClient oClient = new HttpClient())
                {
                    oClient.DefaultRequestHeaders.Clear();
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REPORTUSER")))
                    {
                        var byteArray = Encoding.ASCII.GetBytes(Environment.GetEnvironmentVariable("REPORTUSER") + ":" + Environment.GetEnvironmentVariable("REPORTPASSWORD"));
                        oClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                    }
                    var response = oClient.GetStringAsync(Environment.GetEnvironmentVariable("JainDBURL") + "/GetHistory?id=" + sKey);
                    response.Wait(180000);
                    if (response.IsCompleted)
                    {
                        return JArray.Parse(response.Result);
                    }
                }
            }

            return null;
        }


#if DEBUG
        [AllowAnonymous]
#endif
        [HttpGet]
        [BasicAuthenticationAttribute()]
        [Route("queryAll")]
        public JArray QueryAll()
        {
            string sPath = Path.Combine(_env.WebRootPath, "jaindb");
            jDB.FilePath = sPath;

            string sQuery = this.Request.QueryString.ToString();

            var query = System.Web.HttpUtility.ParseQueryString(sQuery);

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JainDBURL")))
            {
                string qpath = (query[null] ?? "").Replace(',', ';');
                string qsel = (query["$select"] ?? "").Replace(',', ';');
                string qexc = (query["$exclude"] ?? "").Replace(',', ';');
                string qwhe = (query["$where"] ?? "").Replace(',', ';');
                return jDB.QueryAll(qpath, qsel, qexc, qwhe);
            }
            else
            {
                using (HttpClient oClient = new HttpClient())
                {
                    oClient.DefaultRequestHeaders.Clear();
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REPORTUSER")))
                    {
                        var byteArray = Encoding.ASCII.GetBytes(Environment.GetEnvironmentVariable("REPORTUSER") + ":" + Environment.GetEnvironmentVariable("REPORTPASSWORD"));
                        oClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                    }
                    var response = oClient.GetStringAsync(Environment.GetEnvironmentVariable("JainDBURL") + "/queryAll?" + sQuery);
                    response.Wait();
                    if (response.IsCompleted)
                    {
                        return JArray.Parse(response.Result);
                    }
                }
            }

            return null;
        }


        //[BasicAuthenticationAttribute()]
        //[HttpGet]
        //[Route("export")]
        //public JObject Export()
        //{
        //    string sPath = ((Microsoft.AspNetCore.Http.Internal.DefaultHttpRequest)this.Request).Path;
        //    string sQuery = ((Microsoft.AspNetCore.Http.Internal.DefaultHttpRequest)this.Request).QueryString.ToString();
        //    try
        //    {
        //        var query = QueryHelpers.ParseQuery(sQuery);
        //        string sTarget = query.FirstOrDefault(t => t.Key.ToLower() == "url").Value;

        //        string sRemove = query.FirstOrDefault(t => t.Key.ToLower() == "remove").Value;

        //        if (!string.IsNullOrEmpty(sTarget))
        //            jDB.Export(sTarget, sRemove ?? "");
        //        else
        //            jDB.Export("http://localhost:5000", sRemove ?? "");
        //    }
        //    catch { }

        //    return new JObject();
        //}

        private static readonly HttpClient client = new HttpClient();
        private static async Task putFileAsync(string customerid, string deviceid, string data)
        {
            try
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                {
                    string sURL = Environment.GetEnvironmentVariable("fnDevCDR");
                    sURL = sURL.Replace("{fn}", "fnUploadFile");

                    StringContent oData = new StringContent(data, Encoding.UTF8, "application/json");
                    await client.PostAsync($"{sURL}&deviceid={deviceid}&customerid={customerid}", oData);
                }
            }
            catch (Exception ex)
            {
                ex.Message.ToString();
            }
        }
    }


    public class BasicAuthenticationAttribute : ActionFilterAttribute
    {
        public string BasicRealm { get; set; }
        protected string Username { get; set; }
        protected string Password { get; set; }

        public BasicAuthenticationAttribute()
        {
            this.Username = Environment.GetEnvironmentVariable("REPORTUSER") ?? "DEMO";
            this.Password = Environment.GetEnvironmentVariable("REPORTPASSWORD") ?? "password"; ;
        }

        public BasicAuthenticationAttribute(string username, string password)
        {
            this.Username = username;
            this.Password = password;
        }

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
#if DEBUG
            return;
#endif
            var req = filterContext.HttpContext.Request;
            string auth = req.Headers["Authorization"];
            if (!String.IsNullOrEmpty(auth))
            {
                var cred = System.Text.ASCIIEncoding.ASCII.GetString(Convert.FromBase64String(auth.Substring(6))).Split(':');
                var user = new { Name = cred[0], Pass = cred[1] };

                if (user.Name == Username && user.Pass == Password) return;
            }
            filterContext.HttpContext.Response.Headers.Add("WWW-Authenticate", String.Format("Basic realm=\"{0}\"", BasicRealm ?? "devcdr"));
            /// thanks to eismanpat for this line: http://www.ryadel.com/en/http-basic-authentication-asp-net-mvc-using-custom-actionfilter/#comment-2507605761
            filterContext.Result = new UnauthorizedResult();
        }
    }
}