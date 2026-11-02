using System.Net;
using System.Text;
using System.Text.Json;
using ApiGateway.DTOs.Identity;
using ApiGateway.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Restaurant.Tests
{
    public sealed class VietnameseValidationTests
    {
        [Theory]
        [InlineData("/validation-test/role", "{}", "Trường")]
        [InlineData("/validation-test/id", "{\"id\":\"sai mã\"}", "Giá trị không hợp lệ")]
        [InlineData("/validation-test/id", "{", "Giá trị không hợp lệ")]
        [InlineData("/validation-test/id", "", "Giá trị không hợp lệ")]
        public async Task InvalidRequestsKeepProblemShapeAndReturnVietnamese(string route, string json, string expected)
        {
            await using var app = await CreateApp();
            using var client = app.GetTestClient();
            using var response = await client.PostAsync(route, new StringContent(json, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Dữ liệu yêu cầu không hợp lệ.", document.RootElement.GetProperty("title").GetString());
            Assert.Equal(400, document.RootElement.GetProperty("status").GetInt32());
            var errors = document.RootElement.GetProperty("errors").EnumerateObject()
                .SelectMany(property => property.Value.EnumerateArray()).Select(value => value.GetString()).ToArray();
            Assert.NotEmpty(errors);
            Assert.All(errors, message => Assert.StartsWith(expected, message));
        }

        [Fact]
        public async Task AiValidationRetainsFrameworkMessages()
        {
            await using var app = await CreateApp();
            using var client = app.GetTestClient();
            using var response = await client.PostAsync("/ai-validation-test", new StringContent("{", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("One or more validation errors occurred.", document.RootElement.GetProperty("title").GetString());
        }

        private static async Task<WebApplication> CreateApp()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddControllers().AddApplicationPart(typeof(ValidationProbeController).Assembly)
                .AddVietnameseValidation();
            var app = builder.Build();
            app.MapControllers();
            await app.StartAsync();
            return app;
        }
    }

    [ApiController]
    [Route("validation-test")]
    public sealed class ValidationProbeController : ControllerBase
    {
        [HttpPost("role")]
        public IActionResult Role(CreateRoleDTO body)
        {
            return Ok();
        }

        [HttpPost("id")]
        public IActionResult Id(ValidationProbeDTO body)
        {
            return Ok();
        }
    }

    public sealed class ValidationProbeDTO
    {
        public Guid Id { get; set; }
    }
}

namespace ApiGateway.Controllers.AI
{
    [ApiController]
    [Route("ai-validation-test")]
    public sealed class ValidationExclusionProbeController : ControllerBase
    {
        [HttpPost]
        public IActionResult Post(Restaurant.Tests.ValidationProbeDTO body)
        {
            return Ok();
        }
    }
}
