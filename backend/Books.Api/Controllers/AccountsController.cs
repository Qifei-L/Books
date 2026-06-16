using Books.Infrastructure.Data;
using Books.Application.DTOs;
using Books.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Books.Api.Controllers;

[ApiController]
public class AccountsController(AppDbContext db) : ControllerBase
{
    [HttpGet("api/v1/ledgers/{ledgerId:int}/accounts")]
    public async Task<ActionResult<List<Account>>> GetByLedger(int ledgerId)
    {
        return await db.Accounts
            .Where(x => x.LedgerId == ledgerId)
            .OrderBy(x => x.Code)
            .ToListAsync();
    }

    [HttpGet("api/v1/accounts/{id:int}")]
    public async Task<ActionResult<Account>> Get(int id)
    {
        var account = await db.Accounts.FindAsync(id);
        return account is null ? NotFound() : account;
    }

    [HttpPost("api/v1/ledgers/{ledgerId:int}/accounts")]
    public async Task<ActionResult<Account>> Create(int ledgerId, CreateAccountDto dto)
    {
        var ledgerExists = await db.Ledgers.AnyAsync(x => x.Id == ledgerId);
        if (!ledgerExists)
        {
            return NotFound(new { error = "Ledger not found." });
        }

        var account = new Account
        {
            LedgerId = ledgerId,
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
}
