using Microsoft.Extensions.Configuration;
using Restaurant.Common.Settings;
using Xunit;

namespace Restaurant.Tests;

public sealed class ReservationDockerConfigurationTests
{
    [Fact]
    public void DockerConfiguresOnlyReservationStaffRoles()
    {
        var settings = LoadDockerSettings();

        Assert.Equal(new[] { "waiter", "cashier" }, settings.StaffRoleIds);
    }

    [Fact]
    public void DockerConfiguresReservationWindowsAndExpiration()
    {
        var settings = LoadDockerSettings();

        Assert.Equal(120, settings.DefaultDurationMinutes);
        Assert.Empty(settings.DurationMinutesByRestaurant);
        Assert.Equal(30, settings.MinimumAdvanceMinutes);
        Assert.Equal(30, settings.MaximumAdvanceDays);
        Assert.Equal(30, settings.HoldBeforeMinutes);
        Assert.Equal(30, settings.HoldAfterMinutes);
        Assert.True(settings.ExpirationEnabled);
        Assert.Equal(30, settings.ExpirationIntervalSeconds);
        Assert.Equal(100, settings.ExpirationBatchSize);
    }

    private static ReservationSettings LoadDockerSettings()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Backend.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        // The Docker build excludes appsettings.json; do not inherit local configuration in this test.
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(directory.FullName,
                "Services/restaurant_service/Restaurant.API/appsettings.Docker.json"), optional: false)
            .Build();
        return configuration.GetSection("Reservations").Get<ReservationSettings>() ?? new ReservationSettings();
    }
}
