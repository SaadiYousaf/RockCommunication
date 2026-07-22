using CRM.Application.Common.Assignment;
using CRM.Application.Common.Commission;
using CRM.Application.Common.Integrations;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Metrics;
using CRM.Application.Common.Notifications;
using CRM.Application.Sales.Commands;
using CRM.Application.Users.Commands;
using CRM.Domain.Common;
using CRM.Infrastructure.Assignment;
using CRM.Infrastructure.BackgroundJobs;
using CRM.Infrastructure.Commission;
using CRM.Infrastructure.Identity;
using CRM.Infrastructure.Integrations.Carrier;
using CRM.Infrastructure.Integrations.Dialer;
using CRM.Infrastructure.Integrations.Email;
using CRM.Infrastructure.Integrations.Funding;
using CRM.Infrastructure.Integrations.Jornaya;
using CRM.Infrastructure.Integrations.Sms;
using CRM.Infrastructure.Metrics;
using CRM.Infrastructure.Notifications;
using CRM.Infrastructure.Persistence;
using CRM.Infrastructure.Persistence.Interceptors;
using CRM.Infrastructure.Sales;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Hangfire;
using Hangfire.MemoryStorage;
using Hangfire.PostgreSql;

namespace CRM.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        Guard.AgainstNull(services);
        Guard.AgainstNull(config);

        services.AddScoped<AuditInterceptor>();
        services.AddScoped<TenantInterceptor>();

        services.AddDbContext<AppDbContext>((sp, opts) =>
        {
            var conn = config.GetConnectionString("Default") ?? "Data Source=crm.db";
            var provider = config.GetValue("Database:Provider", "Sqlite");
            if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
                opts.UseSqlServer(conn);
            // MySQL provider intentionally omitted; add Pomelo.EntityFrameworkCore.MySql + UseMySql later if needed.
            else
                opts.UseSqlite(conn);

            opts.AddInterceptors(
                sp.GetRequiredService<AuditInterceptor>(),
                sp.GetRequiredService<TenantInterceptor>());
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddIdentity<ApplicationUser, ApplicationRole>(opts =>
        {
            opts.Password.RequireDigit = true;
            opts.Password.RequireLowercase = true;
            opts.Password.RequireUppercase = true;
            opts.Password.RequireNonAlphanumeric = true;
            opts.Password.RequiredLength = 8;
            opts.User.RequireUniqueEmail = true;
            opts.SignIn.RequireConfirmedAccount = false;

            // Lockout protects against online password-guessing. 5 attempts then a 15-min
            // window matches OWASP guidance and what most enterprise CRMs ship with.
            opts.Lockout.AllowedForNewUsers = true;
            opts.Lockout.MaxFailedAccessAttempts = 5;
            opts.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        // Password-reset / email-confirmation tokens expire in 30 minutes (the reset email
        // states 30 min; the framework default is 24 h, leaving a stolen link valid far
        // longer than advertised).
        services.Configure<Microsoft.AspNetCore.Identity.DataProtectionTokenProviderOptions>(
            o => o.TokenLifespan = TimeSpan.FromMinutes(30));

        var jwtSection = config.GetSection("Jwt");
        services.Configure<JwtOptions>(jwtSection);
        var jwt = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

        // HMAC-SHA256 needs at least 256 bits of secret material. A missing or short
        // secret in production silently degrades signature strength and lets an attacker
        // forge tokens in seconds. Fail fast outside the local dev box.
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var isDev = string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
        if (!isDev)
        {
            if (string.IsNullOrWhiteSpace(jwt.Secret))
                throw new InvalidOperationException(
                    "Jwt:Secret is not configured. Set a long random value (>= 32 chars) " +
                    "via configuration or the JWT__SECRET environment variable before starting the API.");
            if (Encoding.UTF8.GetByteCount(jwt.Secret) < 32)
                throw new InvalidOperationException(
                    "Jwt:Secret is too short. Use at least 32 bytes (256 bits) of random material.");
            // Reject the known repo-committed placeholder: a long-but-public value passes the
            // length check yet lets anyone with repo access forge tokens.
            if (jwt.Secret.Contains("REPLACE-WITH", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Jwt:Secret is the committed placeholder. Supply a real random secret via JWT__SECRET.");
        }

        // PII-at-rest encryption key (SSN / driver's licence). Prefer a dedicated
        // Encryption:Key; otherwise derive deterministically from the JWT secret so there is
        // no extra secret to manage. A stable source keeps previously-encrypted values readable.
        var encKeySource = config["Encryption:Key"];
        if (string.IsNullOrEmpty(encKeySource)) encKeySource = jwt.Secret;
        if (!string.IsNullOrEmpty(encKeySource))
            Security.PiiProtector.Configure(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(encKeySource + "|pii-v1")));

        services.AddAuthentication(opts =>
        {
            opts.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            opts.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            opts.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        })
            .AddJwtBearer(opts =>
            {
                opts.MapInboundClaims = false;
                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "unique_name",
                    RoleClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                        string.IsNullOrEmpty(jwt.Secret) ? new string('X', 64) : jwt.Secret))
                };

                // SignalR over WebSocket can't send the Authorization header, so the client
                // puts the token in `?access_token=...`. Read it for hub requests.
                opts.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var accessToken = ctx.Request.Query["access_token"].ToString();
                        var path = ctx.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        {
                            ctx.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<ITwoFactorService, TwoFactorService>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<AuthEmailSender>();
        services.AddSingleton<IChatAttachmentStorage, Services.LocalChatAttachmentStorage>();
        services.AddSingleton<IFileStorage, Services.LocalFileStorage>();

        // Second-factor methods
        services.AddScoped<ISecondFactorMethod, TotpSecondFactor>();
        services.AddScoped<ISecondFactorMethod, EmailOtpSecondFactor>();
        services.AddScoped<SecondFactorRegistry>();

        // Integration options
        services.Configure<Integrations.IntegrationOptions>(config.GetSection("Integrations"));
        var integration = config.GetSection("Integrations").Get<Integrations.IntegrationOptions>() ?? new();

        // Integrations — chosen by config; "Http" = real client, default = stub
        if (integration.Jornaya.Provider.Equals("Http", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<IJornayaProvider, Integrations.Jornaya.HttpJornayaProvider>().AddStandardResilienceHandler();
        else
            services.AddScoped<IJornayaProvider, StubJornayaProvider>();

        // Single active dialer, chosen by Integrations:Dialer:Provider (Vici | Zoom | RingCentral).
        // Each falls back to the logging stub when its BaseUrl / credentials are absent.
        var dialerProvider = integration.Dialer.Provider ?? "Vici";
        if (dialerProvider.Equals("Zoom", StringComparison.OrdinalIgnoreCase) ||
            dialerProvider.Equals("ZoomPhone", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<IDialerProvider, Integrations.Dialer.HttpZoomPhoneDialerProvider>().AddStandardResilienceHandler();
        else if (dialerProvider.Equals("RingCentral", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<IDialerProvider, Integrations.Dialer.HttpRingCentralDialerProvider>().AddStandardResilienceHandler();
        else if (dialerProvider.Equals("Http", StringComparison.OrdinalIgnoreCase) ||
            dialerProvider.Equals("Vici", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(integration.Dialer.BaseUrl))
            services.AddHttpClient<IDialerProvider, Integrations.Dialer.HttpViciDialerProvider>().AddStandardResilienceHandler();
        else
            services.AddScoped<IDialerProvider, ViciDialerProvider>();

        if (integration.Sms.Provider.Equals("Twilio", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<ISmsProvider, Integrations.Sms.HttpTwilioSmsProvider>().AddStandardResilienceHandler();
        else if (integration.Sms.Provider.Equals("Http", StringComparison.OrdinalIgnoreCase) ||
                 (integration.Sms.Provider.Equals("GHL", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(integration.Sms.BaseUrl)))
            services.AddHttpClient<ISmsProvider, Integrations.Sms.HttpGhlSmsProvider>().AddStandardResilienceHandler();
        else
            services.AddScoped<ISmsProvider, StubSmsProvider>();

        // Use real SMTP whenever a host is configured — Provider="Stub" only wins
        // when SmtpHost is empty (dev default). The provider itself surfaces dev links to logs
        // when credentials are missing.
        if (!string.IsNullOrEmpty(integration.Email.SmtpHost) &&
            !integration.Email.Provider.Equals("Stub", StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IEmailProvider, Integrations.Email.SmtpEmailProvider>();
        else
            services.AddScoped<IEmailProvider, StubEmailProvider>();

        services.AddScoped<IFundingProvider, StubFundingProvider>();

        if (integration.Carriers.Endpoints.Count > 0)
        {
            services.AddHttpClient<Integrations.Carrier.HttpCarrierAetna>().AddStandardResilienceHandler();
            services.AddHttpClient<Integrations.Carrier.HttpCarrierUhc>().AddStandardResilienceHandler();
            services.AddScoped<ICarrierProvider>(sp => sp.GetRequiredService<Integrations.Carrier.HttpCarrierAetna>());
            services.AddScoped<ICarrierProvider>(sp => sp.GetRequiredService<Integrations.Carrier.HttpCarrierUhc>());
        }
        else
        {
            services.AddScoped<ICarrierProvider, StubCarrierAetna>();
            services.AddScoped<ICarrierProvider, StubCarrierUnitedHealth>();
        }
        services.AddScoped<ICarrierRegistry, CarrierRegistry>();

        // Notification channels
        services.AddScoped<INotificationChannel, InAppNotificationChannel>();
        services.AddScoped<INotificationChannel, EmailNotificationChannel>();
        services.AddScoped<INotificationChannel, SmsNotificationChannel>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        // Lead assignment strategies
        services.AddScoped<IAssignmentStrategy, RoundRobinStrategy>();
        services.AddScoped<IAssignmentStrategy, LeastBusyStrategy>();
        services.AddScoped<IAssignmentStrategy, ManualStrategy>();
        services.AddScoped<IAssignmentStrategy, SkillBasedStrategy>();
        services.AddScoped<IAssignmentStrategyRegistry, AssignmentStrategyRegistry>();
        services.AddScoped<IAssignmentService, AssignmentService>();

        // Commission rules
        services.AddScoped<IAgencyCommissionConfigProvider, AgencyCommissionConfigProvider>();
        services.AddScoped<ICommissionRule, CloserFlatRateRule>();
        services.AddScoped<ICommissionRule, JrCloserSplitRule>();
        services.AddScoped<ICommissionRule, ValidatorBonusRule>();
        services.AddScoped<ICommissionRule, HighPremiumKickerRule>();
        services.AddScoped<ICommissionEngine, CommissionEngine>();

        // Sales support
        services.AddScoped<IInternalSaleChecker, InternalSaleChecker>();

        // Dashboard metrics
        services.AddScoped<IMetric, TotalLeadsMetric>();
        services.AddScoped<IMetric, FrontedLeadsMetric>();
        services.AddScoped<IMetric, ClosedSalesMetric>();
        services.AddScoped<IMetric, FundedSalesMetric>();
        services.AddScoped<IMetric, TotalPremiumMetric>();
        services.AddScoped<IMetric, ConversionRateMetric>();
        services.AddScoped<IMetric, AverageHandleTimeMetric>();
        services.AddScoped<IMetric, AnswerRateMetric>();
        services.AddScoped<IMetric, AbandonRateMetric>();
        services.AddScoped<IMetric, ServiceLevelMetric>();
        services.AddScoped<IMetric, OccupancyMetric>();
        services.AddScoped<IMetric, CostPerLeadMetric>();
        services.AddScoped<IDashboardService, DashboardService>();

        // Compliance
        services.AddScoped<Application.Common.Compliance.IPhoneNormalizer, Compliance.PhoneNormalizer>();
        services.AddScoped<Application.Common.Compliance.IDncChecker, Compliance.DncChecker>();
        services.AddScoped<Application.Common.Compliance.ITcpaWindowChecker, Compliance.TcpaWindowChecker>();
        services.AddScoped<Application.Common.Compliance.IComplianceGuard, Compliance.ComplianceGuard>();

        // Permissions
        services.AddScoped<Application.Common.Authorization.IPermissionService, Identity.PermissionService>();

        // Modules / Role management (RBAC)
        services.AddScoped<Application.Common.Authorization.IModuleAccessService, Identity.ModuleAccessService>();
        services.AddScoped<Application.Common.Authorization.IRoleManagementService, Identity.RoleManagementService>();

        // Lead scoring
        services.AddScoped<Application.Common.Scoring.IScoringRule, Scoring.JornayaVerifiedRule>();
        services.AddScoped<Application.Common.Scoring.IScoringRule, Scoring.HasEmailRule>();
        services.AddScoped<Application.Common.Scoring.IScoringRule, Scoring.ConsentCapturedRule>();
        services.AddScoped<Application.Common.Scoring.IScoringRule, Scoring.HighValueStateRule>();
        services.AddScoped<Application.Common.Scoring.IScoringRule, Scoring.DncDeductionRule>();
        services.AddScoped<Application.Common.Scoring.ILeadScorer, Scoring.LeadScorer>();

        // Workflow engine
        services.AddScoped<Application.Common.Workflow.IWorkflowAction, Workflow.AssignAgentAction>();
        services.AddScoped<Application.Common.Workflow.IWorkflowAction, Workflow.MoveStageAction>();
        services.AddScoped<Application.Common.Workflow.IWorkflowAction, Workflow.SendSmsWorkflowAction>();
        services.AddScoped<Application.Common.Workflow.IWorkflowAction, Workflow.SendEmailWorkflowAction>();
        services.AddScoped<Application.Common.Workflow.IWorkflowAction, Workflow.CreateCallbackAction>();
        services.AddScoped<Application.Common.Workflow.IWorkflowAction, Workflow.NotifyUserAction>();
        services.AddHttpClient<Workflow.WebhookWorkflowAction>().AddStandardResilienceHandler();
        services.AddScoped<Application.Common.Workflow.IWorkflowAction>(sp => sp.GetRequiredService<Workflow.WebhookWorkflowAction>());
        services.AddScoped<Application.Common.Workflow.IWorkflowActionRegistry, Workflow.WorkflowActionRegistry>();
        services.AddScoped<Application.Common.Workflow.IWorkflowEngine, Workflow.WorkflowEngine>();

        // Team-wise commission rule
        services.AddScoped<Application.Common.Commission.ICommissionRule, Commission.TeamLeadOverrideRule>();

        // Lyons banking validator (bank-account verification → sale banking code)
        services.Configure<Integrations.Lyons.LyonsOptions>(config.GetSection("Integrations:Lyons"));
        var lyons = config.GetSection("Integrations:Lyons").Get<Integrations.Lyons.LyonsOptions>() ?? new();
        if (lyons.Provider.Equals("Http", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(lyons.BaseUrl))
            services.AddHttpClient<Application.Common.Integrations.ILyonsBankingValidator, Integrations.Lyons.HttpLyonsBankingValidator>().AddStandardResilienceHandler();
        else
            services.AddScoped<Application.Common.Integrations.ILyonsBankingValidator, Integrations.Lyons.StubLyonsBankingValidator>();

        // BLA / Trello
        services.Configure<Integrations.Bla.BlaOptions>(config.GetSection("Integrations:Bla"));
        var bla = config.GetSection("Integrations:Bla").Get<Integrations.Bla.BlaOptions>() ?? new();
        if (bla.Provider.Equals("Http", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(bla.BaseUrl))
            services.AddHttpClient<Application.Common.Integrations.IBlaProvider, Integrations.Bla.HttpBlaProvider>().AddStandardResilienceHandler();
        else
            services.AddScoped<Application.Common.Integrations.IBlaProvider, Integrations.Bla.StubBlaProvider>();

        services.Configure<Integrations.Trello.TrelloOptions>(config.GetSection("Integrations:Trello"));
        var trello = config.GetSection("Integrations:Trello").Get<Integrations.Trello.TrelloOptions>() ?? new();
        if (trello.Provider.Equals("Http", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(trello.Token))
            services.AddHttpClient<Application.Common.Integrations.ITrelloProvider, Integrations.Trello.HttpTrelloProvider>().AddStandardResilienceHandler();
        else
            services.AddScoped<Application.Common.Integrations.ITrelloProvider, Integrations.Trello.StubTrelloProvider>();

        // AI services. Default = heuristic stub. With Ai:Provider=OpenAI and Ai:ApiKey set,
        // calls go to OpenAI's chat/completions (or any OpenAI-compatible endpoint).
        services.Configure<Ai.AiOptions>(config.GetSection("Ai"));
        var aiOpts = config.GetSection("Ai").Get<Ai.AiOptions>() ?? new();
        if ((aiOpts.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) ||
             aiOpts.Provider.Equals("Http", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrEmpty(aiOpts.ApiKey))
        {
            services.AddHttpClient<Application.Common.Ai.IAiCompletionProvider, Ai.HttpOpenAiCompletionProvider>()
                .AddStandardResilienceHandler();
        }
        else
        {
            services.AddScoped<Application.Common.Ai.IAiCompletionProvider, Ai.StubAiCompletionProvider>();
        }
        services.AddScoped<Application.Common.Ai.ICallSummarizer, Ai.CallSummarizer>();
        services.AddScoped<Application.Common.Ai.ILeadAiScorer, Ai.LeadAiScorer>();
        services.AddScoped<Application.Common.Ai.IRecommendationService, Ai.RecommendationService>();

        // ── Background jobs ────────────────────────────────────────────────
        // Provider selection is config-driven so dev can stay on the in-process
        // scheduler while prod can flip to durable storage without code changes.
        //
        //   BackgroundJobs:Provider = "InProcess"   (dev default — single-instance only)
        //                           = "Hangfire"    (durable via Hangfire)
        //   BackgroundJobs:Storage  = "Memory"      (dev — lost on restart)
        //                           = "SqlServer"   (prod, Microsoft SQL)
        //                           = "Postgres"    (prod, PostgreSQL)
        //   BackgroundJobs:ConnectionString = full connection string for the chosen storage
        //                                     (defaults to ConnectionStrings:Default)
        // ─────────────────────────────────────────────────────────────────────
        var useHangfire = config.GetValue("BackgroundJobs:Provider", "InProcess").Equals("Hangfire", StringComparison.OrdinalIgnoreCase);
        if (useHangfire)
        {
            var hangfireStorage = config.GetValue("BackgroundJobs:Storage", "Memory");
            var hangfireConn = config.GetValue<string?>("BackgroundJobs:ConnectionString", null)
                               ?? config.GetConnectionString("Default")
                               ?? "";

            services.AddHangfire(c =>
            {
                c.SetDataCompatibilityLevel(Hangfire.CompatibilityLevel.Version_180)
                 .UseSimpleAssemblyNameTypeSerializer()
                 .UseRecommendedSerializerSettings();

                // Storage selection — fall back to MemoryStorage when not configured for prod.
                if (string.Equals(hangfireStorage, "SqlServer", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(hangfireConn))
                {
                    c.UseSqlServerStorage(hangfireConn, new Hangfire.SqlServer.SqlServerStorageOptions
                    {
                        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                        QueuePollInterval = TimeSpan.Zero,
                        UseRecommendedIsolationLevel = true,
                        DisableGlobalLocks = true,
                    });
                }
                else if (string.Equals(hangfireStorage, "Postgres", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(hangfireConn))
                {
                    c.UsePostgreSqlStorage(opts => opts.UseNpgsqlConnection(hangfireConn));
                }
                else
                {
                    c.UseMemoryStorage();
                }
            });

            // Tunable worker count — defaults to processor count, capped at 20.
            services.AddHangfireServer(opts =>
            {
                opts.WorkerCount = config.GetValue("BackgroundJobs:Workers", Math.Min(Environment.ProcessorCount * 2, 20));
                opts.Queues = new[] { "default", "emails", "webhooks" };
            });
            services.AddScoped<Workflow.IBackgroundJobScheduler, BackgroundJobs.HangfireJobScheduler>();
        }
        else
        {
            services.AddSingleton<Workflow.IBackgroundJobScheduler, BackgroundJobs.InProcessJobScheduler>();
            services.AddHostedService<CallbackReminderService>();
        }
        services.AddScoped<BackgroundJobs.WorkflowJobRunner>();
        services.AddScoped<BackgroundJobs.CallbackReminderJob>();
        services.AddScoped<BackgroundJobs.CadenceRunnerJob>();

        return services;
    }
}
