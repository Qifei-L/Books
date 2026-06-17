using Books.Application.Services;
using Books.Domain.Entities;

namespace Books.Blazor.Services;

public class AccountingContextService(GeneralLedgerAppService gl)
{
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
        if (initialized)
        {
            return;
        }

        initialized = true;
        await ReloadEntitiesAsync();
    }

    public async Task ReloadEntitiesAsync()
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
        await SetEntityAsync(entityId);
    }

    public async Task SetEntityAsync(int entityId)
    {
        CurrentEntity = Entities.FirstOrDefault(x => x.Id == entityId)
            ?? await gl.GetEntityAsync(entityId);

        if (CurrentEntity is null)
        {
            Ledgers = [];
            CurrentLedger = null;
            await NotifyAsync();
            return;
        }

        Ledgers = await gl.GetLedgersAsync(CurrentEntity.Id);
        CurrentLedger = Ledgers.FirstOrDefault(x => string.Equals(x.Code, "MAIN", StringComparison.OrdinalIgnoreCase))
            ?? Ledgers.FirstOrDefault();

        await NotifyAsync();
    }

    public async Task SetLedgerAsync(int ledgerId)
    {
        CurrentLedger = Ledgers.FirstOrDefault(x => x.Id == ledgerId)
            ?? await gl.GetLedgerAsync(ledgerId);

        if (CurrentLedger is not null && CurrentEntity?.Id != CurrentLedger.EntityId)
        {
            CurrentEntity = Entities.FirstOrDefault(x => x.Id == CurrentLedger.EntityId)
                ?? await gl.GetEntityAsync(CurrentLedger.EntityId);
            Ledgers = CurrentEntity is null ? [] : await gl.GetLedgersAsync(CurrentEntity.Id);
        }

        await NotifyAsync();
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
