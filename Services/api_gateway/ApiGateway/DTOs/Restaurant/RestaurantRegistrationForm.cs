namespace ApiGateway.DTOs.Restaurant;

public sealed class RestaurantRegistrationForm : RestaurantWriteDto
{
    public Guid RegistrationId { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public IFormFile? BusinessCertificateImage { get; set; }
    public IFormFile? FoodSafetyCertificateImage { get; set; }
}
