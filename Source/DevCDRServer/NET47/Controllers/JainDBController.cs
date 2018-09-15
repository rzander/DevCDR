using jaindb;
using Microsoft.AspNet.SignalR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Runtime.Caching;
using System.Web.Caching;

namespace DevCDRServer.Controllers
{
    public class JainDBController : Controller
    {
        [AllowAnonymous]
        [HttpGet]
        public ActionResult Index()
        {
            ViewBag.Message = "JainDB running on Device Commander";
            return View("About");
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("upload/{param}")]
        public string Upload(string param)
        {
            jDB.FilePath = HttpContext.Server.MapPath("~/App_Data/JainDB");
            Stream req = Request.InputStream;
            req.Seek(0, System.IO.SeekOrigin.Begin);
            string sParams = new StreamReader(req).ReadToEnd();
            param = Request.Path.Substring(Request.Path.LastIndexOf('/') + 1);
            return jDB.UploadFull(sParams, param);
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("GetPS")]
        public string GetPS()
        {
            string sResult = "";
            string spath = HttpContext.Server.MapPath("~/App_Data/JainDB");
            jaindb.jDB.FilePath = spath;
            string sLocalURL = Request.Url.AbsoluteUri.Replace("/getps", "");
            if (System.IO.File.Exists(spath + "/inventory.ps1"))
            {
                string sFile = System.IO.File.ReadAllText(spath + "/inventory.ps1");
                sResult = sFile.Replace("%LocalURL%", sLocalURL).Replace(":%WebPort%", "");

                return sResult;
            }

            return sResult;

        }

        [HttpGet]
        [BasicAuthenticationAttribute("DEMO", "password")]
        [Route("full")]
        public JObject Full()
        {
            //string sPath = this.Request.Path;
            string spath = HttpContext.Server.MapPath("~/App_Data/JainDB");
            jaindb.jDB.FilePath = spath;

            string sQuery = this.Request.QueryString.ToString();

            var query = System.Web.HttpUtility.ParseQueryString(sQuery);
            string sKey = query["id"];

            if (string.IsNullOrEmpty(sKey))
                sKey = jDB.LookupID(query.Keys[0], query.GetValues(0)[0]);
            //int index = -1;
            if (!int.TryParse(query["index"], out int index))
                index = -1;
            if (!string.IsNullOrEmpty(sKey))
                return jDB.GetFull(sKey, index);
            else
                return null;
        }

        [HttpGet]
        [BasicAuthenticationAttribute("DEMO", "password")]
        [Route("query")]
        public async System.Threading.Tasks.Task<JArray> Query()
        {
            DateTime dStart = DateTime.Now;

            string sPath = HttpContext.Server.MapPath("~/App_Data/JainDB");
            jaindb.jDB.FilePath = sPath;

            string sQuery = this.Request.QueryString.ToString();
            if (sPath != "/favicon.ico")
            {
                //string sUri = Microsoft.AspNetCore.Http.Extensions.UriHelper.GetDisplayUrl(Request);
                var query = System.Web.HttpUtility.ParseQueryString(sQuery);
                string qpath = (query[null] ?? "").Replace(',',';');
                string qsel = (query["$select"] ?? "").Replace(',', ';');
                string qexc = (query["$exclude"] ?? "").Replace(',', ';');
                string qwhe = (query["$where"] ?? "").Replace(',', ';');
                return await jDB.QueryAsync(qpath, qsel, qexc, qwhe);
            }
            return null;
        }

        public int totalDeviceCount(string sPath = "")
        {
            if(string.IsNullOrEmpty(sPath))
                sPath = HttpContext.Server.MapPath("~/App_Data/JainDB/Chain");
            int iCount = 0;
            if (Directory.Exists(sPath))
                iCount = Directory.GetFiles(sPath).Count(); //count Blockchain Files

            var cCache = new Cache();
            cCache.Add("totalDeviceCount", iCount, null, DateTime.Now.AddSeconds(90), Cache.NoSlidingExpiration, System.Web.Caching.CacheItemPriority.High, null);

            return iCount;
        }

        [AllowAnonymous]
        [HttpGet]
        public ActionResult About()
        {
            ViewBag.Message = "JainDB running on Device Commander";
            return View();
        }

        //[AllowAnonymous]
        [System.Web.Mvc.Authorize]
        [HttpGet]
        public ActionResult Inv(string id, string name = "", int index = -1)
        {
            string spath = HttpContext.Server.MapPath("~/App_Data/JainDB");
            jaindb.jDB.FilePath = spath;

            if (!string.IsNullOrEmpty(name))
                id = jDB.LookupID("name", name);

            if (string.IsNullOrEmpty(id))
                return Redirect("../DevCdr/Dashboard");

            var oInv = jDB.GetFull(id, index);

            if (oInv != new JObject())
            {
                ViewBag.Id = id;

                try
                {
                    string sInstance = oInv["DevCDRInstance"].ToString();
                    try
                    {
                        TimeSpan tDiff = DateTime.Now.ToUniversalTime() - (DateTime)oInv["_date"];
                        if (tDiff.TotalDays >= 2)
                            ViewBag.LastInv = ((int)tDiff.TotalDays).ToString() + " days"; 
                        else
                        {
                            if((tDiff.TotalHours >= 1))
                                ViewBag.LastInv = ((int)tDiff.TotalHours).ToString() + " hours";
                            else
                                ViewBag.LastInv = ((int)tDiff.TotalMinutes).ToString() + " minutes";
                        }
                    }
                    catch
                    {
                        ViewBag.LastInv = oInv["_date"];
                    }

                    ViewBag.Id = id;
                    ViewBag.Index = oInv["_index"];
                    ViewBag.OS = oInv["OS"]["Caption"];
                    ViewBag.Name = oInv["Computer"]["#Name"];
                    ViewBag.Title = oInv["Computer"]["#Name"];
                    ViewBag.UserName = oInv["Computer"]["#UserName"] ?? "";
                    ViewBag.Vendor = oInv["Computer"]["Manufacturer"] ?? oInv["BIOS"]["Manufacturer"];
                    ViewBag.Serial = oInv["Computer"]["#SerialNumber"] ?? oInv["BIOS"]["#SerialNumber"] ?? "unknown";
                    ViewBag.Version = oInv["OS"]["Version"];
                    ViewBag.InstDate = oInv["OS"]["#InstallDate"];
                    ViewBag.LastBoot = oInv["OS"]["@LastBootUpTime"];
                    ViewBag.Model = oInv["Computer"]["Model"] ?? "unknown";
                    ViewBag.Language = oInv["OS"]["OSLanguage"].ToString();
                    ViewBag.Arch = oInv["OS"]["OSArchitecture"];
                    ViewBag.CPU = oInv["Processor"]["Name"] ?? oInv["Processor"][0]["Name"];
                    ViewBag.Memory = Convert.ToInt32((long)oInv["Computer"]["TotalPhysicalMemory"] / 1000 / 1000 / 1000);
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
                    int chassis = int.Parse(oInv["Computer"]["ChassisTypes"][0].ToString() ?? "2");

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

                                    foreach(var oItem in oRes.ToList())
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
                        foreach(var xSW in oInv["Software"])
                        {
                            if(aIndApps.Contains(xSW["DisplayName"].ToString().TrimEnd()))
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
                    ViewBag.IndSW = lIndSW.Distinct().OrderBy(t=>t);
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

        [System.Web.Mvc.Authorize]
        [HttpGet]
        public ActionResult Diff(string id, int l =-1, int r = -1)
        {
            string spath = HttpContext.Server.MapPath("~/App_Data/JainDB");
            jaindb.jDB.FilePath = spath;

            if (!string.IsNullOrEmpty(id))
            {

                var oL = jDB.GetFull(id, l);
                if (r == -1)
                {
                    try
                    {
                        r = (int)oL["_index"] - 1;
                    }
                    catch { }
                }
                var oR = jDB.GetFull(id, r);

                //remove all @ attributes
                foreach (var oKey in oL.Descendants().Where(t => t.Type == JTokenType.Property && ((JProperty)t).Name.StartsWith("@")).ToList())
                {
                    try
                    {
                        oKey.Remove();
                    }
                    catch (Exception ex)
                    {
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
                    }
                }

                ViewBag.jsonR = oR.ToString(Formatting.None);
                ViewBag.jsonL = oL.ToString(Formatting.None);
            }
            return View("Diff");
        }

        [System.Web.Mvc.Authorize]
        [HttpGet]
        public ActionResult InvJson(string id, int l = -1)
        {
            string spath = HttpContext.Server.MapPath("~/App_Data/JainDB");
            jaindb.jDB.FilePath = spath;

            if (!string.IsNullOrEmpty(id))
            {

                var oL = jDB.GetFull(id, l);

                ViewBag.jsonL = oL.ToString(Formatting.None);
            }
            return View("InvJson");
        }
    }


    public class BasicAuthenticationAttribute : ActionFilterAttribute
    {
        public string BasicRealm { get; set; }
        protected string Username { get; set; }
        protected string Password { get; set; }

        public BasicAuthenticationAttribute(string username, string password)
        {
            this.Username = username;
            this.Password = password;
        }

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            var req = filterContext.HttpContext.Request;
            var auth = req.Headers["Authorization"];
            if (!String.IsNullOrEmpty(auth))
            {
                var cred = System.Text.ASCIIEncoding.ASCII.GetString(Convert.FromBase64String(auth.Substring(6))).Split(':');
                var user = new { Name = cred[0], Pass = cred[1] };
                if (user.Name == Username && user.Pass == Password) return;
            }
            filterContext.HttpContext.Response.AddHeader("WWW-Authenticate", String.Format("Basic realm=\"{0}\"", BasicRealm ?? "devcdr"));
            /// thanks to eismanpat for this line: http://www.ryadel.com/en/http-basic-authentication-asp-net-mvc-using-custom-actionfilter/#comment-2507605761
            filterContext.Result = new HttpUnauthorizedResult();
        }
    }
}