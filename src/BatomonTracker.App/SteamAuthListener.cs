using System.Net;

namespace BatomonTracker.App;

public sealed class SteamAuthListener : IDisposable
{
    private const string Prefix = "http://127.0.0.1:48721/auth/callback/";
    private HttpListener? listener;
    private string? state;

    public event EventHandler<SteamAuthenticatedEventArgs>? Authenticated;

    public string CurrentState => state ?? throw new InvalidOperationException("Steam auth listener is not started.");

    public string Start()
    {
        Stop();
        state = Guid.NewGuid().ToString("N");
        listener = new HttpListener();
        listener.Prefixes.Add(Prefix);
        listener.Start();
        _ = ListenAsync(listener, state);
        return $"{Prefix}?state={Uri.EscapeDataString(state)}";
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task ListenAsync(HttpListener activeListener, string expectedState)
    {
        try
        {
            HttpListenerContext context = await activeListener.GetContextAsync();
            string receivedState = context.Request.QueryString["state"] ?? string.Empty;
            string steamId = context.Request.QueryString["steamid"] ?? string.Empty;
            string accessToken = context.Request.QueryString["token"] ?? string.Empty;
            bool valid = string.Equals(receivedState, expectedState, StringComparison.Ordinal) &&
                         !string.IsNullOrWhiteSpace(steamId) &&
                         !string.IsNullOrWhiteSpace(accessToken);
            string body = valid
                ? "<html><body><h1>Accesso completato</h1><p>Puoi tornare a Batomon Tracker.</p></body></html>"
                : "<html><body><h1>Accesso non valido</h1></body></html>";
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(body);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();

            if (valid)
            {
                Authenticated?.Invoke(this, new SteamAuthenticatedEventArgs(steamId, accessToken));
            }
        }
        catch (HttpListenerException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Stop()
    {
        if (listener is null)
        {
            return;
        }

        listener.Stop();
        listener.Close();
        listener = null;
        state = null;
    }
}

public sealed class SteamAuthenticatedEventArgs : EventArgs
{
    public SteamAuthenticatedEventArgs(string steamId, string accessToken)
    {
        SteamId = steamId;
        AccessToken = accessToken;
    }

    public string SteamId { get; }

    public string AccessToken { get; }
}
