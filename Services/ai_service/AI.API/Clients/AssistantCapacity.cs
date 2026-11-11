using AI.Common.Settings;
using Microsoft.Extensions.Options;

namespace AI.API.Clients;

public sealed class AssistantCapacity(IOptions<AISettings> settings) : IDisposable
{
    private readonly SemaphoreSlim _slots = new(settings.Value.MaximumConcurrentInferences, settings.Value.MaximumConcurrentInferences);

    public bool TryEnter(CancellationToken ct)
    {
        return _slots.Wait(0, ct);
    }

    public void Exit()
    {
        _slots.Release();
    }

    public void Dispose()
    {
        _slots.Dispose();
    }
}
