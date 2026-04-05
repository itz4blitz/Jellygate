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
        <title>Music Requests</title>
        <style>
            :root {
                color-scheme: dark;
                font-family: "Noto Sans", system-ui, sans-serif;
                background: #10151f;
                color: #f3f6fb;
            }

            body {
                margin: 0;
                min-height: 100vh;
                display: grid;
                place-items: center;
                background:
                    radial-gradient(circle at top, rgba(0, 124, 166, 0.18), transparent 32%),
                    radial-gradient(circle at bottom right, rgba(170, 92, 195, 0.18), transparent 28%),
                    linear-gradient(180deg, #10151f 0%, #0b0f17 100%);
            }

            main {
                width: min(100%, 34rem);
                padding: 2.25rem;
                border-radius: 1.5rem;
                background: rgba(20, 26, 40, 0.94);
                border: 1px solid rgba(0, 124, 166, 0.28);
                box-shadow: 0 24px 80px rgba(0, 0, 0, 0.45);
                backdrop-filter: blur(16px);
            }

            .brand {
                display: flex;
                gap: 1rem;
                align-items: center;
                margin-bottom: 1.25rem;
            }

            .logo {
                width: 3.5rem;
                height: 3.5rem;
                flex: 0 0 auto;
            }

            .eyebrow {
                margin: 0 0 0.35rem;
                color: #79d3ee;
                font-size: 0.76rem;
                font-weight: 700;
                letter-spacing: 0.14em;
                text-transform: uppercase;
            }

            h1 {
                margin: 0;
                font-size: 1.85rem;
                line-height: 1.15;
            }

            p {
                margin: 0;
                color: #b7c2d6;
                line-height: 1.55;
            }

            button {
                margin-top: 1.25rem;
                border: 0;
                border-radius: 999px;
                padding: 0.85rem 1rem;
                font: inherit;
                font-weight: 700;
                cursor: pointer;
                background: linear-gradient(135deg, #00a4dc, #aa5cc3);
                color: #ffffff;
                box-shadow: 0 10px 24px rgba(0, 164, 220, 0.22);
            }

            button[hidden] {
                display: none;
            }

            .muted {
                margin-top: 1rem;
                font-size: 0.95rem;
            }

            .status {
                min-height: 3rem;
            }

            .pulse {
                width: 0.8rem;
                height: 0.8rem;
                border-radius: 999px;
                background: linear-gradient(135deg, #00a4dc, #aa5cc3);
                box-shadow: 0 0 0 0 rgba(0, 164, 220, 0.4);
                animation: pulse 1.8s infinite;
            }

            .progress {
                display: flex;
                align-items: center;
                gap: 0.65rem;
                margin-top: 1.15rem;
                color: #dce8f4;
                font-size: 0.95rem;
            }

            @keyframes pulse {
                0% { box-shadow: 0 0 0 0 rgba(0, 164, 220, 0.4); }
                70% { box-shadow: 0 0 0 12px rgba(0, 164, 220, 0); }
                100% { box-shadow: 0 0 0 0 rgba(0, 164, 220, 0); }
            }
        </style>
</head>
<body>
    <main>
        <div class="brand">
            <svg class="logo" viewBox="0 0 64 64" fill="none" aria-hidden="true">
                <defs>
                    <linearGradient id="logoFill" x1="12" y1="10" x2="52" y2="54" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="#00A4DC" />
                        <stop offset="1" stop-color="#AA5CC3" />
                    </linearGradient>
                </defs>
                <circle cx="32" cy="32" r="30" fill="#172138" stroke="rgba(121,211,238,0.35)" stroke-width="1.5" />
                <path d="M32 11L48 42.5H16L32 11Z" fill="url(#logoFill)" />
                <circle cx="32" cy="42.5" r="8.75" fill="#10151F" stroke="#79D3EE" stroke-width="1.5" />
            </svg>
            <div>
                <p class="eyebrow">Music Requests</p>
                <h1 id="heading">Opening your music request session</h1>
            </div>
        </div>

        <div class="status">
            <p id="message">Checking your saved sign-in and preparing access.</p>
            <p class="muted" id="detail"></p>
        </div>

        <div class="progress" id="progressRow">
            <div class="pulse" aria-hidden="true"></div>
            <span id="progressText">Checking session...</span>
        </div>

        <button id="loginButton" hidden type="button">Continue</button>
    </main>

    <script>
        const serverId = "{{encodedServerId}}";
        const sessionEndpoint = "{{encodedSessionEndpoint}}";
        const loginPath = "{{encodedLoginPath}}";
        const manualMode = {{encodedManualMode}};
        const heading = document.getElementById('heading');
        const message = document.getElementById('message');
        const detail = document.getElementById('detail');
        const progressRow = document.getElementById('progressRow');
        const progressText = document.getElementById('progressText');
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

        function startPolling() {
            if (pollHandle) {
                return;
            }

            pollHandle = window.setInterval(async () => {
                if (await continueToGateway()) {
                    if (pollHandle) {
                        window.clearInterval(pollHandle);
                        pollHandle = null;
                    }
                }
            }, 1500);
        }

        function showManualPrompt() {
            heading.textContent = 'Continue to music requests';
            message.textContent = 'We could not restore your session automatically yet.';
            detail.textContent = 'Continue to sign in. If you already completed sign-in in another tab, this page will continue automatically.';
            progressRow.hidden = true;
            loginButton.hidden = false;
        }

        function openLogin() {
            heading.textContent = 'Continue to music requests';
            message.textContent = 'Finish signing in and this page will continue automatically.';
            detail.textContent = 'A sign-in page will open in another tab so this page can keep watching for your session.';
            progressRow.hidden = false;
            progressText.textContent = 'Waiting for sign-in...';

            const popup = window.open(loginPath, '_blank');
            if (!popup) {
                detail.textContent = 'Your browser blocked the sign-in tab. Allow pop-ups for this site, then press Continue again.';
            }

            startPolling();
        }

        async function startFlow() {
            if (manualMode) {
                heading.textContent = 'Signed out';
                message.textContent = 'Continue when you want to open music requests again.';
                detail.textContent = 'If your sign-in is still active, this page will continue immediately. Otherwise a sign-in page will open.';
                progressRow.hidden = true;
                loginButton.textContent = 'Continue';
                loginButton.hidden = false;
                return;
            }

            if (await continueToGateway()) {
                return;
            }

            showManualPrompt();
            startPolling();
        }

        loginButton.addEventListener('click', async () => {
            progressRow.hidden = false;
            progressText.textContent = 'Checking session...';

            if (await continueToGateway()) {
                return;
            }

            openLogin();
        });

        startFlow().catch(() => {
            heading.textContent = 'Continue to music requests';
            message.textContent = 'We could not restore your session automatically.';
            detail.textContent = 'Continue to sign in and this page will finish the handoff when your session appears.';
            progressRow.hidden = true;
            loginButton.textContent = 'Continue';
            loginButton.hidden = false;
            startPolling();
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
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Request.PathBase}/web/#/login?serverid={Uri.EscapeDataString(_applicationHost.SystemId)}");
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
