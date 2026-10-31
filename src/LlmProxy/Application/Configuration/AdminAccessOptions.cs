namespace LlmProxy.Application.Configuration;

/// <summary>
/// Settings for who may reach the operator surface, bound from the "Admin" configuration section.
/// </summary>
public class AdminAccessOptions
{
    /// <summary>
    /// When true, the default, the operator surface answers loopback clients only. The operator pages expose the
    /// full text of every recorded conversation and the provider API reports provider configuration, so the
    /// surface is not something to hand to a network that does not run the proxy itself. Set to false only
    /// when the surface is fronted by something that authenticates its users.
    /// </summary>
    public bool RequireLocalhost { get; set; } = true;
}
