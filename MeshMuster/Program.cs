using MeshMuster.Config;
using MeshMuster.Data;
using MeshMuster.Pages.Api;
using MeshMuster.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

var validator = new ValidateAppOptions();
builder.Services.AddSingleton<IValidateOptions<AppOptions>>(validator);
builder.Services.AddOptions<AppOptions>()
    .Configure<IConfiguration>(AppOptionsLoader.Populate)
    .ValidateOnStart();

var appOpts = AppOptionsLoader.Load(builder.Configuration);
var eager = validator.Validate(Options.DefaultName, appOpts);
if (eager.Failed)
    throw new OptionsValidationException(Options.DefaultName, typeof(AppOptions), eager.Failures);

Directory.CreateDirectory(appOpts.StatePath);
Directory.CreateDirectory(
    Path.GetDirectoryName(Path.GetFullPath(appOpts.DatabasePath)) ?? appOpts.StatePath
);

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    foreach (var proxy in appOpts.TrustedProxies) o.KnownProxies.Add(proxy);
    foreach (var network in appOpts.TrustedNetworks) o.KnownIPNetworks.Add(network);
});

builder.Services.AddSingleton(TimeProvider.System);

// Validation already refused a bad SECRET_KEY; never null here.
builder.Services.AddSingleton(new SecretProtector(appOpts.SecretKeyBytes()!));
builder.Services.AddDbContext<AppDbContext>(o => o
    .UseSqlite(SqliteConfiguration.ConnectionString(appOpts))
    .ReplaceService<IModelCacheKeyFactory, SecretModelCacheKeyFactory>());

builder.Services.AddHealthChecks();
builder.Services.AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN");
builder.Services.AddRazorPages()
    .AddMvcOptions(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);


var app = builder.Build();
app.UseForwardedHeaders();
app.UseMeshMusterSecurityHeaders();
app.UseStaticFiles();
app.UseRouting();
app.UseAntiforgery();
app.MapRazorPages();
app.MapHealthEndpoints();

app.Run(appOpts.GetListenUrl());

public partial class Program;
