using DevCDRServer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace DevCDR
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IWebHostEnvironment env)
        {
            //var builder = new ConfigurationBuilder()
            //    .SetBasePath(env.ContentRootPath)
            //    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            //    .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
            //    .AddEnvironmentVariables();
            //Configuration = builder.Build();
            Configuration = configuration;
            Env = env;
        }

        public IConfiguration Configuration { get; }
        public IWebHostEnvironment Env { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddAuthentication(sharedOptions =>
            {
                sharedOptions.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                sharedOptions.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddAzureAd(options => Configuration.Bind("AzureAd", options))
            .AddCookie();

            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => false;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Azure:SignalR:ConnectionString")))
                services.AddSignalR();
            else
            {
                services.AddSignalR()
                  .AddAzureSignalR();
            }

            services.AddMemoryCache();
            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
            });

            if (Env.IsDevelopment())
            {
                //Disable AUthentication in Develpment mode
                services.AddSingleton<IAuthorizationHandler, AllowAnonymous>();
            }

            services.AddMvc();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                //app.UseBrowserLink();
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/DevCDR/Error");
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseCors();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseCookiePolicy();

            app.UseEndpoints(endpoints =>
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Azure:SignalR:ConnectionString")))
                {
                    endpoints.MapHub<Default>("/chat");
                }
                else
                {
                    app.UseAzureSignalR(routes =>
                    {
                        routes.MapHub<Default>("/chat");
                    });
                }
                endpoints.MapControllerRoute("default", "{controller=DevCDR}/{action=Dashboard}/{id?}");
            });

            Console.WriteLine("loading JainDB plugins:");
            jaindb.jDB.loadPlugins();
            Console.WriteLine("");
        }

        /// <summary>
        /// This authorisation handler will bypass all requirements
        /// </summary>
        public class AllowAnonymous : IAuthorizationHandler
        {
            public Task HandleAsync(AuthorizationHandlerContext context)
            {
                foreach (IAuthorizationRequirement requirement in context.PendingRequirements.ToList())
                    context.Succeed(requirement); //Simply pass all requirements

                return Task.CompletedTask;
            }
        }
    }
}
