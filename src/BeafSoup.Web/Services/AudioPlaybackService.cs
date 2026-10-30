using Microsoft.JSInterop;

namespace BeafSoup.Web.Services;

public sealed class AudioPlaybackService(HttpClient httpClient, IJSRuntime jsRuntime)
{
    public async Task<bool> PlayAsync(string url)
    {
        try
        {
            var absoluteUrl = new Uri(httpClient.BaseAddress!, url).ToString();
            await jsRuntime.InvokeVoidAsync("beafAudio.play", absoluteUrl);
            return true;
        }
        catch (JSException)
        {
            return false;
        }
    }

    /// <summary>Plays a clip and waits for the outcome: "ended", "error" or "stopped".</summary>
    public async Task<string> PlayToEndAsync(string url)
    {
        var absoluteUrl = new Uri(httpClient.BaseAddress!, url).ToString();
        return await jsRuntime.InvokeAsync<string>("beafAudio.playToEnd", absoluteUrl);
    }

    public async Task StopAsync()
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("beafAudio.stop");
        }
        catch (JSException)
        {
            // Nothing to stop.
        }
    }
}
