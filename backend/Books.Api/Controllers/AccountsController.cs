using Books.Application.DTOs;
using Books.Domain.Entities;
using Books.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Books.Api.Controllers;

[ApiController]
public class AccountsController(AppDbContext db) : ControllerBase
{
    [HttpGet("api/v1/entities/{entityId:int}/accounts")]
    public async Task<ActionResult<List<Account>>> GetByEntity(int entityId)
    {
        var entityExists = await db.Entities.AnyAsync(x => x.Id == entityId);
        if (!entityExists)
        {
            return NotFound(new { error = "Entity not found." });
        }

        return await QueryAccountsByEntity(entityId).ToListAsync();
    }

    [HttpGet("api/v1/ledgers/{ledgerId:int}/accounts")]
    public async Task<ActionResult<List<Account>>> GetByLedger(int ledgerId)
    {
        var entityId = await GetLedgerEntityIdAsync(ledgerId);
        if (!entityId.HasValue)
        {
            return NotFound(new { error = "Ledger not found." });
        }

        return await QueryAccountsByEntity(entityId.Value).ToListAsync();
    }

    [HttpGet("api/v1/accounts/{id:int}")]
    public async Task<ActionResult<Account>> Get(int id)
    {
        var account = await db.Accounts.FindAsync(id);
        return account is null ? NotFound() : account;
    }

    [HttpPost("api/v1/entities/{entityId:int}/accounts")]
    public async Task<ActionResult<Account>> CreateForEntity(int entityId, CreateAccountDto dto)
    {
        var entityExists = await db.Entities.AnyAsync(x => x.Id == entityId);
        if (!entityExists)
        {
            return NotFound(new { error = "Entity not found." });
        }

        return await CreateAccountAsync(entityId, dto);
    }

    [HttpPost("api/v1/ledgers/{ledgerId:int}/accounts")]
    public async Task<ActionResult<Account>> CreateForLedger(int ledgerId, CreateAccountDto dto)
    {
        var entityId = await GetLedgerEntityIdAsync(ledgerId);
        if (!entityId.HasValue)
        {
            return NotFound(new { error = "Ledger not found." });
        }

        return await CreateAccountAsync(entityId.Value, dto);
    }

    [HttpPut("api/v1/accounts/{id:int}")]
    public async Task<IActionResult> Update(int id, CreateAccountDto dto)
    {
        var account = await db.Accounts.FindAsync(id);
        if (account is null)
        {
            return NotFound();
        }

        if (!account.IsSystemReserved)
        {
            account.Code = dto.Code.Trim();
            account.Type = dto.Type;
        }

        account.Name = dto.Name.Trim();
        account.Description = dto.Description?.Trim() ?? string.Empty;
        account.IsActive = dto.IsActive;
        account.AllowManualJournal = dto.AllowManualJournal;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("api/v1/accounts/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var account = await db.Accounts.FindAsync(id);
        if (account is null)
        {
            return NotFound();
        }

        if (account.IsSystemReserved)
        {
            return BadRequest(new { error = "System reserved accounts cannot be deleted." });
        }

        account.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private IQueryable<Account> QueryAccountsByEntity(int entityId)
    {
        return db.Accounts
            .Where(x => x.EntityId == entityId)
            .OrderBy(x => x.Code);
    }

    private async Task<ActionResult<Account>> CreateAccountAsync(int entityId, CreateAccountDto dto)
    {
        var account = new Account
        {
            EntityId = entityId,
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            Type = dto.Type,
            Description = dto.Description?.Trim() ?? string.Empty,
            IsActive = dto.IsActive,
            IsSystemReserved = false,
            AllowManualJournal = dto.AllowManualJournal
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = account.Id }, account);
    }

    private async Task<int?> GetLedgerEntityIdAsync(int ledgerId)
    {
        return await db.Ledgers
            .Where(x => x.Id == ledgerId)
            .Select(x => (int?)x.EntityId)
            .FirstOrDefaultAsync();
    }
}
