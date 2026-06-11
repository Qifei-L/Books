using Books.Infrastructure.Data;
using Books.Application.DTOs;
using Books.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Books.Api.Controllers;

[ApiController]
[Route("api/v1/ledgers")]
public class LedgersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Ledger>>> GetAll()
    {
        return await db.Ledgers.OrderBy(x => x.Name).ToListAsync();
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
        var ledger = new Ledger { Name = dto.Name.Trim(), IsActive = dto.IsActive };
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

        ledger.Name = dto.Name.Trim();
        ledger.IsActive = dto.IsActive;
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
