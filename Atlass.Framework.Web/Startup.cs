using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Atlass.Framework.Cache;
using Atlass.Framework.Common.NLog;
using Atlass.Framework.Core;
using Atlass.Framework.Core.DI;
using Atlass.Framework.Core.Extensions;
using Atlass.Framework.Core.HostService;
using Atlass.Framework.Core.Middleware;
using Atlass.Framework.ViewModels;
using Autofac;
using Hangfire;
using Hangfire.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Senparc.CO2NET;
using Senparc.CO2NET.RegisterServices;
using Senparc.Weixin;
using Senparc.Weixin.Entities;
using Senparc.Weixin.MP;
using Senparc.Weixin.RegisterServices;
using Senparc.Weixin.TenPay;
using StackExchange.Redis;

namespace Atlass.Framework.Web
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        public Startup(IConfiguration configuration)
        {
            var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("configs/appsettings.json", optional: true, reloadOnChange: true);
            Configuration = builder.Build();
        }

       

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {

            // services.AddMvc().AddRazorRuntimeCompilation();
            services.AddScoped<IActionResultExecutor<HtmlResult>, HtmlResultExecutor<HtmlResult>>();
            services.AddControllersWithViews(option => {
                option.Filters.Add<GlobalExceptionFilter>();
            }).AddRazorRuntimeCompilation().AddNewtonsoftJson();

            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => false;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });
            //services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            //���ʹ��IIS�����ڳ���ģ��
            services.Configure<IISServerOptions>(option =>
            {
                option.AutomaticAuthentication = false;
            });
            #region ������ע��
            services.AddMemoryCache();//ʹ�ñ��ػ����������
            services.AddSession();//ʹ��Session
            services.AddOptions();
            
            //���ݵ�ַ
            services.AddGlobalVariable(Configuration);
            //services.AddFreeSql();
            #endregion


            #region hangfire����
            services.AddAtlassHangfire(Configuration);
            #endregion
            //΢��
            services.AddSenparcGlobalServices(Configuration)//Senparc.CO2NET ȫ��ע��
              .AddSenparcWeixinServices(Configuration);//Senparc.Weixin ע��
            services.Configure<SenparcWeixinSetting>(Configuration.GetSection("SenparcWeixinSetting"));


            //��־����
            services.AddHostedService<EasyLogHostedService>();

        }
        public void ConfigureContainer(ContainerBuilder builder)
        {
            try
            {
                builder.RegisterModule(new AtlassAutofacDI());
            }
            catch (Exception e)
            {
                LogNHelper.Exception(e);
            }

        }
        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, 
            IHostApplicationLifetime appLifetime, IOptions<Dictionary<string, string>> options,
            IOptions<SenparcSetting> senparcSetting, IOptions<SenparcWeixinSetting> senparcWeixinSetting)
        {
            //if (env.IsDevelopment())
            //{
            //    app.UseDeveloperExceptionPage();
            // //  app.AddRazorRuntimeCompilation();
            //}
            //else
            //{
            //    app.UseExceptionHandler("/Home/Error");
            //    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            //    app.UseHsts();
            //}
            app.UseCookiePolicy();
            app.UseHttpsRedirection();
            
            //�����µ�ý���ļ����ͣ���̬�ļ�·��
            app.UseAtlassDefaultFiles(options);

            app.UseRouting();
            #region �Զ����м��
            app.UseMiddleware<AtlassHttpRequestMiddleware>();
            //app.UseMiddleware(typeof(AtlassExceptionMiddlerware));
            #endregion
            app.UseAuthorization();
            app.UseSession();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
                endpoints.MapControllerRoute(name: "areaRoute",
                  pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
            });

            //����hangfire��������
            app.UseHangfireServer(new BackgroundJobServerOptions
            {
                Queues = new[] { "default"}
            });
            app.UseHangfireDashboard();
            //app.UseHangfireDashboard("/hangfire", new DashboardOptions()
            //{
            //    Authorization = new[] { new HangfireAuthorizeFilter() }
            //});


            //���� UseSenparcGlobal() �ĸ����÷��� CO2NET Demo��
            //https://github.com/Senparc/Senparc.CO2NET/blob/master/Sample/Senparc.CO2NET.Sample.netcore/Startup.cs
            IRegisterService register = RegisterService.Start(senparcSetting.Value)
                .UseSenparcGlobal();

            register.ChangeDefaultCacheNamespace("DefaultCO2NETCache");
            register.UseSenparcWeixin(senparcWeixinSetting.Value, senparcSetting.Value)

            #region ע�ṫ�ںţ����裩
                //ע�ṫ�ںţ���ע������                                                -- DPBMARK MP
                .RegisterMpAccount(senparcWeixinSetting.Value, "doctor_platform_mp") // DPBMARK_END //ע������΢��֧���汾��V3������ע������
                .RegisterTenpayV3(senparcWeixinSetting.Value, "doctor_platform_tenpay"); //��¼��ͬһ�� SenparcWeixinSettingItem ������
            #endregion

            //Ӧ�ó���������
            appLifetime.ApplicationStarted.Register(() => {
                try
                {

                    GlobalParamsDto.WebRoot = env.WebRootPath;
                    //SugarDbConn.DbConnectStr = this.Configuration.GetSection("DbConn:mysqlConn").Value;   //Ϊ���ݿ������ַ�����ֵ
                    GlobalParamsDto.Host = this.Configuration.GetSection("WebHost:Host").Value;

                    //��ʼ����Ŀ��صĻ���
                    CmsCacheInit.Init();
                }
                catch (Exception e)
                {
                    LogNHelper.Exception(e);
                }
            });


        }
    }
}