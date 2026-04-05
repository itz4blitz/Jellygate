using Jellygate.JellyfinBridge.Configuration;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
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
    private readonly ILogger<JellygateBridgeController> _logger;
    private readonly IUserManager _userManager;

    public JellygateBridgeController(IUserManager userManager, ILogger<JellygateBridgeController> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    [Authorize]
    [HttpGet("start")]
    public ActionResult Start([FromQuery] string? returnTo = null)
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

        var handoffUri = BuildGatewayUri(config, token);
        _logger.LogInformation("Issuing Jellygate handoff for user {Username}.", user.Username);
        return Redirect(handoffUri);
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
}
