using MediaBrowser.Model.Plugins;

namespace Jellygate.JellyfinBridge.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public string GatewayBaseUrl { get; set; } = string.Empty;

    public string GatewayHandoffPath { get; set; } = "/auth/handoff";

    public string SharedSecret { get; set; } = string.Empty;

    public int TokenLifetimeSeconds { get; set; } = 60;

    public string DefaultReturnPath { get; set; } = "/";

    public bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(GatewayBaseUrl) && !string.IsNullOrWhiteSpace(SharedSecret);
    }
}
