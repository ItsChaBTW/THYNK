using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using THYNK.Data;
using Microsoft.AspNetCore.Identity.UI.Services;
using THYNK.Services;
using THYNK.Models;
using Microsoft.AspNetCore.Authentication.Google;
using WkHtmlToPdfDotNet;
using WkHtmlToPdfDotNet.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using THYNK.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Configure email settings
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddTransient<IEmailSender, EmailSender>();

// Configure cookie policy
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => true;
    options.MinimumSameSitePolicy = SameSiteMode.None;
});

// Add Google Authentication
builder.Services.AddAuthentication()
    .AddGoogle(options =>
    {
        options.ClientId = "11636115526-jphpfc89sg5tka1gkv86a3166n9j1qd3.apps.googleusercontent.com";
        options.ClientSecret = "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";
        options.CorrelationCookie.SameSite = SameSiteMode.None;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
    });

// Add PDF services
builder.Services.AddSingleton(typeof(IConverter), new SynchronizedConverter(new PdfTools()));
builder.Services.AddScoped<PdfService>();

builder.Services.AddScoped<IPSGCService, PSGCService>();
builder.Services.AddHttpClient<IPSGCService, PSGCService>();

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseCookiePolicy();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Add Database Diagnostic Middleware
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/LGU/CreateAlert") && 
        context.Request.Method == "POST")
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("CreateAlert POST request detected");
        
        try
        {
            // Check database connection
            using var scope = context.RequestServices.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            
            bool canConnect = dbContext.Database.CanConnect();
            logger.LogInformation($"Database connection test from middleware: {(canConnect ? "SUCCESS" : "FAILED")}");
            
            // Check if Alerts table exists and is accessible
            int alertCount = await dbContext.Alerts.CountAsync();
            logger.LogInformation($"Current alert count in database: {alertCount}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database diagnostic middleware error");
        }
    }
    
    await next();
});

// Map SignalR hubs
app.MapHub<AdminHub>("/adminHub");
app.MapHub<AlertHub>("/alertHub");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

// Seed database on startup
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        await DbInitializer.Initialize(context, userManager, roleManager);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();
