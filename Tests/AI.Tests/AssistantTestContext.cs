using AI.BLL.Implements;
using AI.BLL.Interfaces;
using AI.Common.DTOs;
using AI.Common.Exceptions;
using AI.Common.Settings;
using Microsoft.Extensions.Options;

namespace AI.Tests;

public sealed class AssistantTestContext : IGuestAccessClient, ICatalogMenuClient, ICartClient, IAssistantModelClient
{
    public AISettings Settings { get; } = new()
    {
        Channel = "DineIn",
        Currency = "VND",
        DuplicatePolicy = "ItemId",
        RequestTimeoutSeconds = 90,
        DependencyTimeoutSeconds = 10,
        MaximumConcurrentInferences = 1,
        MaximumSuggestions = 3,
        MaximumMenuItems = 200,
        MenuPageSize = 50,
        ModelCandidateCount = 8,
        MaximumMessageLength = 500,
        MaximumHistoryTurns = 4
    };

    public GuestContext Guest { get; } = new(Guid.NewGuid(), new string('a', 64));
    public List<MenuItemDTO> Menu { get; } =
    [
        Dish("Beef rice", "Main dishes", 45000),
        Dish("Lemon tea", "Drinks", 15000),
        Dish("French fries", "Side dishes", 20000)
    ];
    public CartContextDTO Cart { get; set; }
    public ChatPlanDTO Plan { get; set; } = new()
    {
        Language = "en",
        Topic = "overview",
        Intent = "menu",
        InformationKeys = ["m0"]
    };
    public bool DenyGuest { get; set; }
    public int GuestReads { get; private set; }
    public int CartReads { get; private set; }
    public int ModelCalls { get; private set; }
    public int AnswerCalls { get; private set; }
    public Action? AfterModel { get; set; }
    public Action? AfterAnswer { get; set; }
    public ChatAnswerRequestDTO? AnswerRequest { get; private set; }
    public ChatAnswerDTO? ForcedAnswer { get; set; }
    public Func<ModelItemDTO, bool> Select { get; set; } = _ => true;
    public IReadOnlyList<ModelSuggestionDTO>? ForcedSuggestions { get; set; }
    public RecommendationModelRequestDTO? ModelRequest { get; private set; }
    public ChatModelRequestDTO? ChatModelRequest { get; private set; }
    public Func<Guid, MenuItemDTO?>? Refresh { get; set; }

    public AssistantTestContext()
    {
        Cart = new CartContextDTO(Guid.NewGuid(), Guest.TableSessionId, DateTimeOffset.UtcNow.AddHours(1), "v1", [new CartLineDTO(Menu[0].ItemId, 1)]);
    }

    public ChatService Chat(IAssistantModelClient? model = null)
    {
        var options = Options.Create(Settings);
        return new ChatService(this, new MenuContextReader(this, options), model ?? this, options, TimeProvider.System);
    }

    public RecommendationService Recommendations(IAssistantModelClient? model = null)
    {
        var options = Options.Create(Settings);
        return new RecommendationService(this, this, new MenuContextReader(this, options), model ?? this, options, TimeProvider.System);
    }

    public ChatRequestDTO Question(string message = "Is beef rice available?")
    {
        return new ChatRequestDTO(Guest, message, []);
    }

    public RecommendationRequestDTO Request(int limit = 3)
    {
        return new RecommendationRequestDTO(Guest, Cart.CartId, limit);
    }

    public Task<VerifiedGuest> VerifyAsync(GuestContext guest, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        GuestReads++;
        Assert.Equal(Guest, guest);
        if (DenyGuest)
        {
            throw new AIException(401, "QR guest access is invalid or expired.");
        }

        return Task.FromResult(new VerifiedGuest(guest.TableSessionId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)));
    }

    public Task<MenuPageDTO> GetAllAsync(GuestContext guest, string channel, int page, int pageSize, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Assert.Equal(Guest, guest);
        Assert.Equal("DineIn", channel);
        return Task.FromResult(new MenuPageDTO(Menu.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), Menu.Count));
    }

    public Task<MenuItemDTO?> GetByIdAsync(GuestContext guest, string channel, Guid itemId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Assert.Equal(Guest, guest);
        return Task.FromResult(Refresh is null ? Menu.Find(item => item.ItemId == itemId) : Refresh(itemId));
    }

    public Task<CartContextDTO> GetByIdAsync(GuestContext guest, Guid cartId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        CartReads++;
        Assert.Equal(Guest, guest);
        return Task.FromResult(Cart);
    }

    public Task<ChatPlanDTO> ChatAsync(ChatModelRequestDTO request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ModelCalls++;
        ChatModelRequest = request;
        AfterModel?.Invoke();
        var keys = Plan.Intent == "recommendation" ? Plan.RecommendationKeys : Plan.InformationKeys;
        var selected = request.Menu.Where(item => keys.Contains(item.Key)).ToArray();
        var notice = request.Notices?.GetValueOrDefault(Plan.Intent);
        var fallback = selected.Length == 0
            ? request.Notices?.GetValueOrDefault("no_match") ?? "Please ask staff."
            : selected[0].CanOrder
                ? $"You can try {selected[0].Name} with your meal."
                : $"Not currently available: {selected[0].Name}. Please ask a staff member.";
        var reply = ForcedAnswer ?? new ChatAnswerDTO
        {
            Answer = (notice ?? fallback) + (request.SearchLimited ? " This is a limited selection." : ""),
            UsedKeys = notice is not null ? [] : keys.ToList()
        };
        Plan.Answer = reply.Answer;
        Plan.UsedKeys = reply.UsedKeys;
        AfterAnswer?.Invoke();
        return Task.FromResult(Plan);
    }

    public Task<IReadOnlyList<ModelSuggestionDTO>> RecommendAsync(RecommendationModelRequestDTO request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ModelCalls++;
        ModelRequest = request;
        AfterModel?.Invoke();
        return Task.FromResult(ForcedSuggestions ?? request.Candidates.Where(Select).Take(request.Limit).Select(item => new ModelSuggestionDTO
        {
            Key = item.Key,
            Reason = RecommendationReasons.Allowed[0]
        }).ToArray());
    }

    public Task<ChatAnswerDTO> AnswerAsync(ChatAnswerRequestDTO request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        AnswerCalls++;
        AnswerRequest = request;
        AfterAnswer?.Invoke();
        if (request.VerifiedNotice is not null)
        {
            return Task.FromResult(ForcedAnswer ?? new ChatAnswerDTO
            {
                Answer = request.VerifiedNotice,
                UsedKeys = []
            });
        }

        return Task.FromResult(ForcedAnswer ?? new ChatAnswerDTO
        {
            Answer = request.VerifiedMenu[0].CanOrder
                ? $"You can try {request.VerifiedMenu[0].Name} with your meal." + (request.SearchLimited ? " This is a limited selection." : "")
                : $"Not currently available: {request.VerifiedMenu[0].Name}. Please ask a staff member.",
            UsedKeys = request.VerifiedMenu.Select(item => item.Key).ToList()
        });
    }

    public Task<bool> IsReadyAsync(CancellationToken ct)
    {
        return Task.FromResult(true);
    }

    private static MenuItemDTO Dish(string name, string category, decimal price)
    {
        return new MenuItemDTO
        {
            ItemId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            ItemName = name,
            CategoryName = category,
            Price = price,
            PriceId = Guid.NewGuid(),
            Availability = "Available",
            CanOrder = true,
            Channel = "DineIn",
            EvaluatedAt = DateTimeOffset.UtcNow
        };
    }
}
