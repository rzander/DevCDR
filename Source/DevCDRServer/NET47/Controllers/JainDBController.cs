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