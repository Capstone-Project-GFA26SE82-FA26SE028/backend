using Identity.Common.DTOs;
using Identity.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Identity.Tests;

public sealed partial class UserManagementTests
{
    [Fact]
    public async Task CustomerRegistersWithoutStaffPrivilegesAndCanSignIn()
    {
        var auth = await PrepareAuth();
        if (!await db.Roles.AnyAsync(role => role.RoleId == "customer"))
        {
            db.Roles.Add(new Role
            {
                RoleId = "customer",
                RoleName = "Customer"
            });
            await db.SaveChangesAsync();
        }

        var body = new RegisterCustomerDTO
        {
            Email = "online@example.test",
            Password = "CustomerPassword123!",
            FullName = "Online customer",
            PhoneNumber = "0901234567"
        };
        Assert.Equal(201, (await auth.Register(body, default)).StatusCode);
        var user = await db.Users.SingleAsync(value => value.Email == body.Email);
        Assert.Equal("customer", user.RoleId);
        Assert.True(user.IsActive);
        Assert.NotEqual(body.Password, user.PasswordHash);
        Assert.Equal(409, (await auth.Register(body, default)).StatusCode);
        Assert.Equal(200, (await auth.Login(new AuthDTO.LoginDTO
        {
            Email = body.Email,
            Password = body.Password
        })).StatusCode);
    }
}
