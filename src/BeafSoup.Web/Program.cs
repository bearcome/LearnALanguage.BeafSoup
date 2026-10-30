using BeafSoup.Web;
using BeafSoup.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"]
        ?? throw new InvalidOperationException("未配置 ApiBaseUrl。"))
});
builder.Services.AddScoped<LearningApiClient>();
builder.Services.AddScoped<GuestProgressStore>();
builder.Services.AddScoped<AudioPlaybackService>();

await builder.Build().RunAsync();
