using System.Net;
using System.Net.Http.Json;
using ApiGateway.DTOs.Restaurant;
using Backend.Contracts.Identity;
using Grpc.Net.Client;
using Identity.Domain.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.DAL.Models;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class ReservationSqlServerTests
{
    [SqlServerFact]
    public async Task PaymentRequestRejectsWrongSessionWrongRestaurantAndUnassignedRoles()
    {
        var assigned = await Location();
        var other = await Location(assignStaff: false);
        var firstSession = await SeedOpenPaymentSession(assigned);
        var otherSession = await SeedOpenPaymentSession(other);
        using var firstGuest = await JoinPaymentSession(firstSession);
        using var otherGuest = await JoinPaymentSession(otherSession);
        using var noGuest = _gateway.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var firstPath = PaymentRequestPath(firstSession);
        var otherPath = PaymentRequestPath(otherSession);
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await firstGuest.PostAsync(firstPath, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostPaymentRequest(noGuest, firstPath, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostPaymentRequest(otherGuest, firstPath, id)).StatusCode);

        var first = await Read<PaymentRequestResult>(await PostPaymentRequest(firstGuest, firstPath, id));
        Assert.Equal(assigned.RestaurantId, first.RestaurantId);
        Assert.Equivalent(first, await Read<PaymentRequestResult>(await PostPaymentRequest(firstGuest, firstPath, id)), strict: true);
        Assert.Equal(HttpStatusCode.Conflict, (await PostPaymentRequest(firstGuest, firstPath, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostPaymentRequest(otherGuest, otherPath, id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherGuest.GetAsync(otherPath + $"/{id}")).StatusCode);

        var otherId = Guid.NewGuid();
        await Read<PaymentRequestResult>(await PostPaymentRequest(otherGuest, otherPath, otherId));
        var queue = await Read<PaymentRequestPage>(await Send(HttpMethod.Get,
            $"/api/restaurants/{assigned.RestaurantId}/payment-requests", "waiter"));
        Assert.Equal(1, queue.TotalCount);
        var queued = Assert.Single(queue.Items);
        Assert.Equal(first.ServiceRequestId, queued.ServiceRequestId);
        var table = Assert.Single(queued.Tables);
        Assert.Equal(assigned.TableId, table.TableId);
        Assert.Equal("T1", table.TableCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get,
            $"/api/restaurants/{other.RestaurantId}/payment-requests", "waiter")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, otherPath + $"/{otherId}/acknowledge", "waiter")).StatusCode);
        var ack = firstPath + $"/{id}/acknowledge";
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Post, ack, null)).StatusCode);
        foreach (var role in new[] { "admin", "cashier", "kitchen" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, ack, role)).StatusCode);
        }

        var accepted = await Read<PaymentRequestResult>(await Send(HttpMethod.Post, ack, "waiter"));
        Assert.Equal(_users["waiter"], accepted.HandledByRef);
        Assert.Equal("Acknowledged", accepted.Status);
        Assert.NotNull(accepted.AcknowledgedAt);
        Assert.Equivalent(accepted, await Read<PaymentRequestResult>(await Send(HttpMethod.Post, ack, "waiter")), strict: true);
        var guest = await Read<PaymentRequestResult>(await firstGuest.GetAsync(firstPath + $"/{id}"));
        Assert.Equal(accepted.AcknowledgedAt, guest.AcknowledgedAt);
        Assert.Null(guest.HandledByRef);

        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var session = await db.TableSessions.SingleAsync(value => value.TableSessionId == firstSession);
        Assert.Equal("Open", session.Status);
        Assert.Null(session.ClosedAt);
        var assignment = await db.RestaurantStaffs.SingleAsync(value =>
            value.RestaurantId == assigned.RestaurantId && value.UserIdRef == _users["waiter"]);
        assignment.IsActive = false;
        var closed = await db.TableSessions.SingleAsync(value => value.TableSessionId == otherSession);
        closed.Status = "Closed";
        closed.ClosedAt = _clock.Now.UtcDateTime;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, ack, "waiter")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Send(HttpMethod.Post, otherPath + $"/{otherId}/acknowledge", "waiter")).StatusCode);
    }

    [SqlServerFact]
    public async Task ConcurrentPaymentRequestsSerializeOnTheTableSession()
    {
        var session = await SeedOpenPaymentSession(await Location());
        using var firstGuest = await JoinPaymentSession(session);
        using var secondGuest = await JoinPaymentSession(session);
        var path = PaymentRequestPath(session);

        var responses = await ContendForPaymentSession(session,
            () => PostPaymentRequest(firstGuest, path, Guid.NewGuid()),
            () => PostPaymentRequest(secondGuest, path, Guid.NewGuid()));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);

        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var stored = await db.ServiceRequests.SingleAsync(value => value.TableSessionId == session);
        Assert.Equal("Payment", stored.Type);
        Assert.Equal("Requested", stored.Status);
    }

    [SqlServerFact]
    public async Task ConcurrentPaymentRequestsForDifferentRestaurantsBothSucceed()
    {
        var first = await SeedOpenPaymentSession(await Location());
        var second = await SeedOpenPaymentSession(await Location(assignStaff: false));
        using var firstGuest = await JoinPaymentSession(first);
        using var secondGuest = await JoinPaymentSession(second);

        var responses = await Task.WhenAll(
            PostPaymentRequest(firstGuest, PaymentRequestPath(first), Guid.NewGuid()),
            PostPaymentRequest(secondGuest, PaymentRequestPath(second), Guid.NewGuid()));

        var firstResult = await Read<PaymentRequestResult>(responses[0]);
        var secondResult = await Read<PaymentRequestResult>(responses[1]);
        Assert.NotEqual(firstResult.RestaurantId, secondResult.RestaurantId);
        Assert.Equal(first, firstResult.TableSessionId);
        Assert.Equal(second, secondResult.TableSessionId);
    }


    [SqlServerFact]
    public async Task PaymentRequestAcknowledgementsForDifferentRestaurantsBothSucceed()
    {
        var firstRestaurant = await Location();
        var secondRestaurant = await Location(assignStaff: false);
        await AddSecondPaymentWaiter(secondRestaurant.RestaurantId);
        var firstSession = await SeedOpenPaymentSession(firstRestaurant);
        var secondSession = await SeedOpenPaymentSession(secondRestaurant);
        using var firstGuest = await JoinPaymentSession(firstSession);
        using var secondGuest = await JoinPaymentSession(secondSession);
        var firstPath = PaymentRequestPath(firstSession);
        var secondPath = PaymentRequestPath(secondSession);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        await Read<PaymentRequestResult>(await PostPaymentRequest(firstGuest, firstPath, firstId));
        await Read<PaymentRequestResult>(await PostPaymentRequest(secondGuest, secondPath, secondId));

        var responses = await Task.WhenAll(
            Send(HttpMethod.Post, firstPath + $"/{firstId}/acknowledge", "waiter"),
            Send(HttpMethod.Post, secondPath + $"/{secondId}/acknowledge", "secondWaiter"));

        var first = await Read<PaymentRequestResult>(responses[0]);
        var second = await Read<PaymentRequestResult>(responses[1]);
        Assert.Equal("Acknowledged", first.Status);
        Assert.Equal("Acknowledged", second.Status);
        Assert.Equal(_users["waiter"], first.HandledByRef);
        Assert.Equal(_users["secondWaiter"], second.HandledByRef);
    }

    [SqlServerFact]
    public async Task PaymentRequestAcknowledgementsByDifferentWaitersHaveOneWinner()
    {
        var location = await Location();
        await AddSecondPaymentWaiter(location.RestaurantId);
        var session = await SeedOpenPaymentSession(location);
        using var guest = await JoinPaymentSession(session);
        var path = PaymentRequestPath(session);
        var id = Guid.NewGuid();
        await Read<PaymentRequestResult>(await PostPaymentRequest(guest, path, id));

        var acknowledgementPath = path + $"/{id}/acknowledge";
        var roles = new[] { "waiter", "secondWaiter" };
        var responses = await ContendForPaymentSession(session,
            () => Send(HttpMethod.Post, acknowledgementPath, roles[0]),
            () => Send(HttpMethod.Post, acknowledgementPath, roles[1]));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var winner = Array.FindIndex(responses, response => response.StatusCode == HttpStatusCode.OK);
        var result = await Read<PaymentRequestResult>(responses[winner]);
        Assert.Equal("Acknowledged", result.Status);
        Assert.Equal(_users[roles[winner]], result.HandledByRef);

        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var stored = await db.ServiceRequests.SingleAsync(value => value.ServiceRequestId == id);
        Assert.Equal(result.HandledByRef, stored.HandledByRef);
        Assert.Equal(result.AcknowledgedAt!.Value.UtcDateTime, stored.AcknowledgedAt);
    }

    [SqlServerFact]
    public async Task PaymentRequestCurrentIsVisibleToGuestsFromTheSameSession()
    {
        var location = await Location();
        var session = await SeedOpenPaymentSession(location);
        var otherSession = await SeedOpenPaymentSession(await Location(assignStaff: false));
        using var firstGuest = await JoinPaymentSession(session);
        using var secondGuest = await JoinPaymentSession(session);
        using var otherGuest = await JoinPaymentSession(otherSession);
        var path = PaymentRequestPath(session);
        var currentPath = path + "/current";
        Assert.Equal(HttpStatusCode.NotFound, (await secondGuest.GetAsync(currentPath)).StatusCode);

        var id = Guid.NewGuid();
        await Read<PaymentRequestResult>(await PostPaymentRequest(firstGuest, path, id));
        Assert.Equal(HttpStatusCode.Conflict, (await PostPaymentRequest(secondGuest, path, Guid.NewGuid())).StatusCode);
        var current = await Read<PaymentRequestResult>(await secondGuest.GetAsync(currentPath));
        Assert.Equal(id, current.ServiceRequestId);
        Assert.Equal("Requested", current.Status);

        await Read<PaymentRequestResult>(await Send(HttpMethod.Post, path + $"/{id}/acknowledge", "waiter"));
        current = await Read<PaymentRequestResult>(await secondGuest.GetAsync(currentPath));
        Assert.Equal("Acknowledged", current.Status);
        Assert.Null(current.HandledByRef);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherGuest.GetAsync(currentPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherGuest.GetAsync(PaymentRequestPath(otherSession) + "/current")).StatusCode);
    }

    private async Task AddSecondPaymentWaiter(Guid restaurantId)
    {
        const string email = "waiter2@sql.example.test";
        var userId = Guid.NewGuid();
        using (var scope = _identity.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(new User
            {
                UserId = userId,
                RestaurantId = restaurantId,
                RoleId = "waiter",
                UserName = "waiter2",
                Email = email,
                FullName = "Second SQL waiter",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(_password),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var channel = GrpcChannel.ForAddress("http://identity.test", new GrpcChannelOptions
        {
            HttpHandler = _identity.Server.CreateHandler()
        });
        var auth = new IdentityGrpc.IdentityGrpcClient(channel);
        var reply = await auth.LoginAsync(new LoginRequest
        {
            Email = email,
            Password = _password
        });
        Assert.True(reply.IsSuccess);
        _users["secondWaiter"] = userId;
        _tokens["secondWaiter"] = reply.Result.AccessToken;
        await Grant(restaurantId, "secondWaiter");
    }

    private async Task<HttpResponseMessage[]> ContendForPaymentSession(Guid session,
        Func<Task<HttpResponseMessage>> firstRequest, Func<Task<HttpResponseMessage>> secondRequest)
    {
        await using var blocker = new SqlConnection(_restaurantConnection);
        await blocker.OpenAsync();
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
        await using (var command = new SqlCommand(
            "SELECT TableSessionId FROM dbo.TableSession WITH (UPDLOCK, HOLDLOCK) WHERE TableSessionId = @id",
            blocker, transaction))
        {
            command.Parameters.AddWithValue("@id", session);
            await command.ExecuteScalarAsync();
        }

        var first = firstRequest();
        var second = secondRequest();
        var bothBlocked = await WaitForBlocked(blocker.ServerProcessId, 2);
        await transaction.CommitAsync();
        var responses = await Task.WhenAll(first, second);
        Assert.True(bothBlocked, "Both requests must reach the SQL Server session lock before it is released.");
        return responses;
    }

    private async Task<Guid> SeedOpenPaymentSession(LocationIds location)
    {
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var sessionId = Guid.NewGuid();
        db.SessionTables.Add(new SessionTable
        {
            SessionTableId = Guid.NewGuid(),
            TableId = location.TableId,
            AssignedAt = _clock.Now.UtcDateTime,
            TableSession = new TableSession
            {
                TableSessionId = sessionId,
                PartySize = 2,
                Status = "Open",
                OpenedAt = _clock.Now.UtcDateTime
            }
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    private async Task<HttpClient> JoinPaymentSession(Guid sessionId)
    {
        var client = _gateway.CreateClient();
        await Read<GuestAccessDto>(await client.PostAsJsonAsync(
            $"/api/table-sessions/{sessionId}/guest-access/join", new { phoneOrCode = "123" }));
        return client;
    }

    private static string PaymentRequestPath(Guid sessionId)
    {
        return $"/api/table-sessions/{sessionId}/payment-requests";
    }

    private static async Task<HttpResponseMessage> PostPaymentRequest(HttpClient client, string path, Guid id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Idempotency-Key", id.ToString());
        return await client.SendAsync(request);
    }
}
