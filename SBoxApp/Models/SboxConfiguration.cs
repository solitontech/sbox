using System.ComponentModel.DataAnnotations;

namespace SBoxApp.Models;

/// <summary>
/// Configuration edited through the Settings page and used by the SBOX runtime.
/// </summary>
public class SboxConfiguration
{
    [Required]
    [EmailAddress]
    [Display(Name = "Player Email")]
    public string PlayerEmail { get; set; } = string.Empty;

    [Required]
    [Display(Name = "SPL API Key")]
    public string ApiKey { get; set; } = string.Empty;

    [Display(Name = "Team Id (optional)")]
    public string TeamId { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Bot Gateway Address")]
    public string BotHost { get; set; } = "127.0.0.1";

    [Range(1, 65535)]
    [Display(Name = "Bot Gateway Port")]
    public int BotPort { get; set; } = 2025;

    [Required]
    [Display(Name = "Server Address")]
    public string ServerHost { get; set; } = "127.0.0.1";

    [Range(1, 65535)]
    [Display(Name = "Server Port")]
    public int ServerPort { get; set; } = 50505;

    public SboxConfiguration Clone() => new()
    {
        PlayerEmail = PlayerEmail,
        ApiKey = ApiKey,
        TeamId = TeamId,
        BotHost = BotHost,
        BotPort = BotPort,
        ServerHost = ServerHost,
        ServerPort = ServerPort
    };
}
