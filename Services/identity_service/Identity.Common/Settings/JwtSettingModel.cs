namespace Identity.Common.Settings;

public sealed class JwtSettingModel
{
    public string SecretKey { get; set; } = "";
    public string Issuer { get; set; } = "https://localhost:7155";
    public string Audience { get; set; } = "https://localhost:3000";
    public int AccessTokenMinutes { get; set; } = 15;
    public int SessionHours { get; set; } = 12;
}
