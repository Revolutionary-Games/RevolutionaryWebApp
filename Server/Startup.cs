namespace ThriveDevCenter.Server
{
    using System.Linq;
    using Hubs;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.AspNetCore.ResponseCompression;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.AspNetCore.Authentication;
    using Models;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.HttpsPolicy;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.ResponseCompression;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using System.Linq;

    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            // TODO: message pack protocol
            services.AddSignalR();
            services.AddControllersWithViews();
            services.AddRazorPages();

            services.AddResponseCompression(opts =>
            {
                opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                    new[] { "application/octet-stream" });
            });

            services.AddDbContext<ApplicationDbContext>(opts =>
                opts.UseNpgsql(Configuration.GetConnectionString("WebApiConnection")));

            services.AddIdentity<User, IdentityRole<long>>(opts => { opts.SignIn.RequireConfirmedAccount = false; })
                .AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultUI().AddDefaultTokenProviders();

            // services.AddDefaultIdentity<User>(opts =>
            // {
            //     opts.SignIn.RequireConfirmedAccount = false;
            // }).AddEntityFrameworkStores<ApplicationDbContext>();

            // Maybe default token providers is not actually good as it shouldn't allow sso users to
            // change their passwords...

            // Can't use inbuilt AddApiAuthorization
            services.AddIdentityServer().AddAspNetIdentity<User>()
                .AddOperationalStore<ApplicationDbContext>()
                // protected...
                // .ConfigureReplacedServices()
                .AddIdentityResources()
                .AddApiResources()
                .AddClients()
                .AddSigningCredentials();

            services.AddControllers(options =>
                options.Filters.Add(new HttpResponseExceptionFilter()));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseResponseCompression();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseWebAssemblyDebugging();
            }
            else
            {
                app.UseExceptionHandler("/Error");

                // app.UseHsts();
            }

            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapControllers();
                endpoints.MapHub<NotificationsHub>("/notifications");
                endpoints.MapFallbackToPage("/_Host");
            });
        }
    }
}
