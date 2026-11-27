namespace Restaurant.DAL.Models;

public sealed class RestaurantRegistration
{
    public Guid RegistrationId { get; set; }
    public Guid ApplicantUserRef { get; set; }
    public bool NewAccount { get; set; }
    public string Email { get; set; } = string.Empty;
    public string RestaurantDetails { get; set; } = string.Empty;
    public string BusinessCertificateAssetId { get; set; } = string.Empty;
    public string FoodSafetyCertificateAssetId { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public Guid? ReviewedByRef { get; set; }
    public Guid? RestaurantId { get; set; }
}
