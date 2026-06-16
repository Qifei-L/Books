using Books.Application.DTOs;
using Books.Domain.Entities;
using Books.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Books.Api.Controllers;

[ApiController]
[Route("api/v1/ledgers")]
public class LedgersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Ledger>>> GetAll([FromQuery] int? entityId = null)
    {
        var query = db.Ledgers.AsQueryable();
        if (entityId.HasValue)
        {
            query = query.Where(x => x.EntityId == entityId.Value);
        }

        return await query
            .OrderBy(x => x.EntityId)
            .ThenBy(x => x.Code)
            .ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Ledger>> Get(int id)
    {
        var ledger = await db.Ledgers.FindAsync(id);
        return ledger is null ? NotFound() : ledger;
    }

    [HttpPost]
    public async Task<ActionResult<Ledger>> Create(CreateLedgerDto dto)
    {
        var entityExists = await db.Entities.AnyAsync(x => x.Id == dto.EntityId);
        if (!entityExists)
        {
            return NotFound(new { error = "Entity not found." });
        }

        var ledger = new Ledger
        {
            EntityId = dto.EntityId,
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            LedgerType = dto.LedgerType,
            IsActive = dto.IsActive,
            AllowDeletePostedJournal = dto.AllowDeletePostedJournal
        };
        db.Ledgers.Add(ledger);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = ledger.Id }, ledger);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, CreateLedgerDto dto)
    {
        var ledger = await db.Ledgers.FindAsync(id);
        if (ledger is null)
        {
            return NotFound();
        }

        var entityExists = await db.Entities.AnyAsync(x => x.Id == dto.EntityId);
        if (!entityExists)
        {
            return NotFound(new { error = "Entity not found." });
        }

        ledger.EntityId = dto.EntityId;
        ledger.Code = dto.Code.Trim();
        ledger.Name = dto.Name.Trim();
        ledger.LedgerType = dto.LedgerType;
        ledger.IsActive = dto.IsActive;
        ledger.AllowDeletePostedJournal = dto.AllowDeletePostedJournal;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var ledger = await db.Ledgers.FindAsync(id);
        if (ledger is null)
        {
            return NotFound();
        }

        ledger.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
