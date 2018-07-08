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

namespace DevCDRServer.Controllers
{
    [AllowAnonymous]
    [System.Web.Mvc.Authorize]
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

            return jDB.UploadFull(sParams, param);
        }

        [AllowAnonymous]
        [HttpGet]
        public ActionResult About()
        {
            ViewBag.Message = "JainDB running on Device Commander";
            return View();
        }
    }
}