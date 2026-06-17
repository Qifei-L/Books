using Books.Application.Services;
using Books.Domain.Entities;

namespace Books.Blazor.Services;

public class AccountingContextService(GeneralLedgerAppService gl)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;

    public event Func<Task>? Changed;

    public List<Entity> Entities { get; private set; } = [];
    public List<Ledger> Ledgers { get; private set; } = [];
    public Entity? CurrentEntity { get; private set; }
    public Ledger? CurrentLedger { get; private set; }
    public int CurrentEntityId => CurrentEntity?.Id ?? 0;
    public int CurrentLedgerId => CurrentLedger?.Id ?? 0;

    public async Task InitializeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (initialized)
            {
                return;
            }

            await ReloadEntitiesCoreAsync();
            initialized = true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReloadEntitiesAsync(bool notify = true)
    {
        await gate.WaitAsync();
        try
        {
            await ReloadEntitiesCoreAsync();
        }
        finally
        {
            gate.Release();
        }

        if (notify)
        {
            await NotifyAsync();
        }
    }

    public async Task SetEntityAsync(int entityId, bool notify = true)
    {
        await gate.WaitAsync();
        try
        {
            await SetEntityCoreAsync(entityId);
        }
        finally
        {
            gate.Release();
        }

        if (notify)
        {
            await NotifyAsync();
        }
    }

    public async Task SetLedgerAsync(int ledgerId)
    {
        await gate.WaitAsync();
        try
        {
            CurrentLedger = Ledgers.FirstOrDefault(x => x.Id == ledgerId)
                ?? await gl.GetLedgerAsync(ledgerId);

            if (CurrentLedger is not null && CurrentEntity?.Id != CurrentLedger.EntityId)
            {
                CurrentEntity = Entities.FirstOrDefault(x => x.Id == CurrentLedger.EntityId)
                    ?? await gl.GetEntityAsync(CurrentLedger.EntityId);
                Ledgers = CurrentEntity is null ? [] : await gl.GetLedgersAsync(CurrentEntity.Id);
            }
        }
        finally
        {
            gate.Release();
        }

        await NotifyAsync();
    }

    private async Task ReloadEntitiesCoreAsync()
    {
        Entities = (await gl.GetEntitiesAsync())
            .Where(x => x.IsActive)
            .OrderBy(x => x.Code)
            .ToList();

        if (Entities.Count == 0)
        {
            Entities.Add(await gl.GetDefaultEntityAsync());
        }

        var entityId = CurrentEntityId != 0 && Entities.Any(x => x.Id == CurrentEntityId)
            ? CurrentEntityId
            : Entities[0].Id;
        await SetEntityCoreAsync(entityId);
    }

    private async Task SetEntityCoreAsync(int entityId)
    {
        CurrentEntity = Entities.FirstOrDefault(x => x.Id == entityId)
            ?? await gl.GetEntityAsync(entityId);

        if (CurrentEntity is null)
        {
            Ledgers = [];
            CurrentLedger = null;
            return;
        }

        Ledgers = await gl.GetLedgersAsync(CurrentEntity.Id);
        CurrentLedger = Ledgers.FirstOrDefault(x => string.Equals(x.Code, "MAIN", StringComparison.OrdinalIgnoreCase))
            ?? Ledgers.FirstOrDefault();
    }

    private async Task NotifyAsync()
    {
        if (Changed is not null)
        {
            foreach (var handler in Changed.GetInvocationList().Cast<Func<Task>>())
            {
                await handler();
            }
        }
    }
}
