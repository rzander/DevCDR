using Owin;

namespace DevCDRServer
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureAuth(app);
            app.MapSignalR("/Chat", new Microsoft.AspNet.SignalR.HubConfiguration());
        }
    }
}