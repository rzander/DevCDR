using System.Web;
using System.Web.Optimization;

namespace DevCDRServer
{
    public class BundleConfig
    {
        // For more information on bundling, visit https://go.microsoft.com/fwlink/?LinkId=301862
        public static void RegisterBundles(BundleCollection bundles)
        {
            BundleTable.EnableOptimizations = false;

            bundles.Add(new ScriptBundle("~/bundles/jquery").Include(
                        "~/Scripts/jquery-{version}.js"));

            bundles.Add(new ScriptBundle("~/bundles/signalr").Include(
                        "~/Scripts/jquery.signalR-{version}.js"));

            bundles.Add(new ScriptBundle("~/bundles/jqueryval").Include(
                        "~/Scripts/jquery.validate*"));

            // Use the development version of Modernizr to develop with and learn from. Then, when you're
            // ready for production, use the build tool at https://modernizr.com to pick only the tests you need.
            bundles.Add(new ScriptBundle("~/bundles/modernizr").Include(
                        "~/Scripts/modernizr-*"));

            bundles.Add(new ScriptBundle("~/bundles/bootstrap").Include(
                      "~/Scripts/bootstrap.js",
                      "~/Scripts/respond.js"));

            bundles.Add(new StyleBundle("~/Content/css").Include(
                      "~/Content/bootstrap.css",
                      "~/Content/site.css"));

            bundles.Add(new StyleBundle("~/Content/datatables").Include(
                       "~/Content/DataTables/css/jquery.dataTables.min.css",
                       "~/Content/DataTables/css/select.dataTables.min.css",
                       "~/Content/DataTables/css/buttons.dataTables.min.css"));

            bundles.Add(new ScriptBundle("~/bundles/datatables").Include(
           "~/Scripts/DataTables/jquery.dataTables.min.js",
           "~/Scripts/DataTables/dataTables.select.min.js",
           "~/Scripts/DataTables/dataTables.buttons.min.js",
           "~/Scripts/DataTables/buttons.html5.min.js"));

            bundles.Add(new StyleBundle("~/Content/contextmenu").Include(
                        "~/Content/ContextMenu/jquery.contextMenu.css"));

            bundles.Add(new ScriptBundle("~/bundles/contextmenu").Include(
           "~/Scripts/ContextMenu/jquery.contextMenu.js",
           "~/Scripts/ContextMenu/jquery.ui.position.min.js"));

            bundles.Add(new ScriptBundle("~/bundles/jquery-ui").Include(
            "~/Scripts/jquery-ui-{version}.js"));

            bundles.Add(new StyleBundle("~/Content/jquery-ui").Include(
           "~/Content/themes/base/jquery-ui.css"));
        }
    }
}
