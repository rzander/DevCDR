using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace DevCDRServer
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            //routes.MapMvcAttributeRoutes();

            //routes.MapRoute(
            //    name: "JaindB",
            //    url: "jaindb/{action}/{param}",
            //    defaults: new { controller = "JainDB", action = "About", param = UrlParameter.Optional }
            //);

            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "DevCDR", action = "Dashboard", id = UrlParameter.Optional }
            );


        }
    }
}
