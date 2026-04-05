using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Jellygate.JellyfinBridge.Configuration;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Common;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellygate.JellyfinBridge.Api;

[ApiController]
[Route("JellygateBridge")]
public class JellygateBridgeController : ControllerBase
{
    private readonly IApplicationHost _applicationHost;
    private readonly ILogger<JellygateBridgeController> _logger;
    private readonly IUserManager _userManager;

    public JellygateBridgeController(IApplicationHost applicationHost, IUserManager userManager, ILogger<JellygateBridgeController> logger)
    {
        _applicationHost = applicationHost;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet("start")]
    public ActionResult Start([FromQuery] string? returnTo = null)
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            return Redirect(BuildLaunchUri(returnTo));
        }

        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return Problem("Jellygate Bridge is not loaded.", statusCode: StatusCodes.Status500InternalServerError);
        }

        var config = plugin.Configuration;
        if (!config.IsConfigured())
        {
            return Problem("Jellygate Bridge is not configured yet.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var username = User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Unauthorized();
        }

        var user = _userManager.GetUserByName(username);
        if (user is null)
        {
            return Unauthorized();
        }

        var safeReturnTo = NormalizeReturnTo(returnTo) ?? NormalizeReturnTo(config.DefaultReturnPath) ?? "/";
        var role = user.HasPermission(PermissionKind.IsAdministrator) ? "admin" : "user";
        var token = HandoffTokenIssuer.Issue(
            config.SharedSecret,
            user.Id.ToString(),
            user.Username,
            role,
            Math.Max(config.TokenLifetimeSeconds, 15),
            safeReturnTo);

        var handoffUri = BuildGatewayUri(config, token);
        _logger.LogInformation("Issuing Jellygate handoff for user {Username}.", user.Username);
        return Redirect(handoffUri);
    }

    [HttpGet("launch")]
    public ContentResult Launch([FromQuery] string? returnTo = null, [FromQuery] string? manual = null)
    {
        var safeReturnTo = NormalizeReturnTo(returnTo) ?? NormalizeReturnTo(Plugin.Instance?.Configuration.DefaultReturnPath) ?? "/";
        var manualMode = string.Equals(manual, "1", StringComparison.Ordinal);

        var sessionEndpoint = $"{Request.PathBase}/JellygateBridge/session?returnTo={Uri.EscapeDataString(safeReturnTo)}";
        var loginPath = BuildJellyfinLoginUri();
        var encodedServerId = JavaScriptStringEncode(_applicationHost.SystemId);
        var encodedSessionEndpoint = JavaScriptStringEncode(sessionEndpoint);
        var encodedLoginPath = JavaScriptStringEncode(loginPath);
        var encodedManualMode = manualMode ? "true" : "false";

        var html = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Jellygate Bridge</title>
    <style>
        :root {
            color-scheme: dark;
            font-family: Inter, system-ui, sans-serif;
            background: #0f172a;
            color: #e2e8f0;
        }

        body {
            margin: 0;
            min-height: 100vh;
            display: grid;
            place-items: center;
            background:
                radial-gradient(circle at top, rgba(56, 189, 248, 0.18), transparent 35%),
                radial-gradient(circle at bottom right, rgba(99, 102, 241, 0.18), transparent 32%),
                #0f172a;
        }

        main {
            width: min(100%, 32rem);
            padding: 2rem;
            border-radius: 1.25rem;
            background: rgba(15, 23, 42, 0.92);
            border: 1px solid rgba(148, 163, 184, 0.2);
            box-shadow: 0 24px 80px rgba(15, 23, 42, 0.45);
        }

        h1 {
            margin: 0 0 0.75rem;
            font-size: 1.7rem;
        }

        p {
            margin: 0;
            color: #94a3b8;
            line-height: 1.55;
        }

        button {
            margin-top: 1.25rem;
            border: 0;
            border-radius: 0.85rem;
            padding: 0.85rem 1rem;
            font: inherit;
            font-weight: 700;
            cursor: pointer;
            background: linear-gradient(135deg, #38bdf8, #6366f1);
            color: #0f172a;
        }

        button[hidden] {
            display: none;
        }

        .muted {
            margin-top: 1rem;
            font-size: 0.95rem;
        }
    </style>
</head>
<body>
    <main>
        <h1>Jellygate Bridge</h1>
        <p id="message">Checking your Jellyfin session and preparing your Aurral access.</p>
        <button id="loginButton" hidden type="button">Continue to Jellyfin</button>
        <p class="muted" id="detail"></p>
    </main>

    <script>
        const serverId = "{{encodedServerId}}";
        const sessionEndpoint = "{{encodedSessionEndpoint}}";
        const loginPath = "{{encodedLoginPath}}";
        const manualMode = {{encodedManualMode}};
        const message = document.getElementById('message');
        const detail = document.getElementById('detail');
        const loginButton = document.getElementById('loginButton');

        let pollHandle = null;

        function parseCredentials() {
            try {
                const raw = localStorage.getItem('jellyfin_credentials');
                if (!raw) {
                    return null;
                }

                const data = JSON.parse(raw);
                const servers = Array.isArray(data.Servers) ? data.Servers : [];
                const match = servers.find((server) => server.Id === serverId && server.AccessToken);
                return match || servers.find((server) => server.AccessToken) || null;
            } catch {
                return null;
            }
        }

        async function continueToGateway() {
            const credentials = parseCredentials();
            if (!credentials?.AccessToken) {
                return false;
            }

            const response = await fetch(sessionEndpoint, {
                headers: {
                    'Accept': 'application/json',
                    'X-Emby-Token': credentials.AccessToken
                }
            });

            if (!response.ok) {
                return false;
            }

            const data = await response.json();
            if (!data?.redirectUrl) {
                return false;
            }

            window.location.replace(data.redirectUrl);
            return true;
        }

        function showLoginPrompt() {
            message.textContent = 'Finish signing in to Jellyfin to continue into Aurral.';
            detail.textContent = 'If you are already signed in in another tab, this page will continue automatically.';
            loginButton.hidden = false;
        }

        async function startFlow() {
            if (manualMode) {
                message.textContent = 'You signed out of Aurral. Continue when you want to start a new Jellygate session.';
                detail.textContent = 'If you are still signed in to Jellyfin, the button will continue immediately. Otherwise it will take you to Jellyfin login first.';
                loginButton.textContent = 'Continue to Aurral';
                loginButton.hidden = false;
                return;
            }

            if (await continueToGateway()) {
                return;
            }

            showLoginPrompt();
            pollHandle = window.setInterval(async () => {
                if (await continueToGateway()) {
                    if (pollHandle) {
                        window.clearInterval(pollHandle);
                    }
                }
            }, 1500);
        }

        loginButton.addEventListener('click', async () => {
            if (await continueToGateway()) {
                return;
            }

            window.location.assign(loginPath);
        });

        startFlow().catch(() => {
            message.textContent = 'Unable to verify your Jellyfin session automatically.';
            detail.textContent = 'Use the button below to sign in to Jellyfin, then this page will continue.';
            loginButton.hidden = false;
        });
    </script>
</body>
</html>
""";

        return Content(html, "text/html", Encoding.UTF8);
    }

    [Authorize]
    [HttpGet("session")]
    public ActionResult GetSession([FromQuery] string? returnTo = null)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return Problem("Jellygate Bridge is not loaded.", statusCode: StatusCodes.Status500InternalServerError);
        }

        var config = plugin.Configuration;
        if (!config.IsConfigured())
        {
            return Problem("Jellygate Bridge is not configured yet.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var username = User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Unauthorized();
        }

        var user = _userManager.GetUserByName(username);
        if (user is null)
        {
            return Unauthorized();
        }

        var safeReturnTo = NormalizeReturnTo(returnTo) ?? NormalizeReturnTo(config.DefaultReturnPath) ?? "/";
        var role = user.HasPermission(PermissionKind.IsAdministrator) ? "admin" : "user";
        var token = HandoffTokenIssuer.Issue(
            config.SharedSecret,
            user.Id.ToString(),
            user.Username,
            role,
            Math.Max(config.TokenLifetimeSeconds, 15),
            safeReturnTo);

        _logger.LogInformation("Issuing Jellygate session redirect for user {Username}.", user.Username);
        return Ok(new
        {
            redirectUrl = BuildGatewayUri(config, token)
        });
    }

    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpGet("config")]
    public ActionResult<PluginConfiguration> GetConfiguration()
    {
        return Ok(Plugin.Instance?.Configuration ?? new PluginConfiguration());
    }

    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpPost("config")]
    public ActionResult<PluginConfiguration> SaveConfiguration([FromBody] PluginConfiguration configuration)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return Problem("Jellygate Bridge is not loaded.", statusCode: StatusCodes.Status500InternalServerError);
        }

        configuration.GatewayBaseUrl = configuration.GatewayBaseUrl.Trim();
        configuration.GatewayHandoffPath = NormalizeGatewayHandoffPath(configuration.GatewayHandoffPath);
        configuration.DefaultReturnPath = NormalizeReturnTo(configuration.DefaultReturnPath) ?? "/";
        configuration.TokenLifetimeSeconds = Math.Max(configuration.TokenLifetimeSeconds, 15);

        plugin.UpdateConfiguration(configuration);
        _logger.LogInformation("Updated Jellygate Bridge configuration.");
        return Ok(configuration);
    }

    [HttpGet("health")]
    public ActionResult GetHealth()
    {
        return Ok(new
        {
            configured = Plugin.Instance?.Configuration.IsConfigured() ?? false
        });
    }

    private static string BuildGatewayUri(PluginConfiguration configuration, string token)
    {
        var builder = new UriBuilder(configuration.GatewayBaseUrl.TrimEnd('/'));
        builder.Path = CombinePaths(builder.Path, configuration.GatewayHandoffPath);
        builder.Query = $"token={Uri.EscapeDataString(token)}";
        return builder.Uri.ToString();
    }

    private string BuildJellyfinLoginUri()
    {
        var currentTarget = BuildLaunchUri(Request.Query["returnTo"]);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Request.PathBase}/web/#/login?serverid={Uri.EscapeDataString(_applicationHost.SystemId)}&url={Uri.EscapeDataString(currentTarget)}");
    }

    private string BuildLaunchUri(string? returnTo)
    {
        var safeReturnTo = NormalizeReturnTo(returnTo) ?? NormalizeReturnTo(Plugin.Instance?.Configuration.DefaultReturnPath) ?? "/";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Request.PathBase}/JellygateBridge/launch?returnTo={Uri.EscapeDataString(safeReturnTo)}");
    }

    private static string CombinePaths(string left, string right)
    {
        var normalizedLeft = string.IsNullOrWhiteSpace(left) ? string.Empty : left.TrimEnd('/');
        var normalizedRight = NormalizeGatewayHandoffPath(right);
        return $"{normalizedLeft}{normalizedRight}";
    }

    private static string NormalizeGatewayHandoffPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/auth/handoff";
        }

        return path.StartsWith('/') ? path : $"/{path}";
    }

    private static string? NormalizeReturnTo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal) ? value : null;
    }

    private static string JavaScriptStringEncode(string value)
        => JsonSerializer.Serialize(value).Trim('"');
}
