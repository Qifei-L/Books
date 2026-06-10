using Books.Api.Data;
using Books.Api.DTOs;
using Books.Api.Models;
using Books.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Books.Api.Controllers;

[ApiController]
public class JournalEntriesController(AppDbContext db, JournalService journalService) : ControllerBase
{
    [HttpGet("api/v1/ledgers/{ledgerId:int}/journal-entries")]
    public async Task<ActionResult<List<object>>> GetByLedger(int ledgerId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var query = db.JournalEntries
            .Include(x => x.Lines)
            .Where(x => x.LedgerId == ledgerId);

        if (from.HasValue)
        {
            query = query.Where(x => x.EntryDate >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(x => x.EntryDate <= to.Value);
        }

        return await query
            .OrderByDescending(x => x.EntryDate)
            .ThenBy(x => x.JournalNo)
            .Select(x => new
            {
                x.Id,
                x.LedgerId,
                x.JournalNo,
                x.EntryDate,
                x.Description,
                x.Status,
                TotalDebit = x.Lines.Sum(line => line.Debit),
                TotalCredit = x.Lines.Sum(line => line.Credit)
            })
            .ToListAsync<object>();
    }

    [HttpGet("api/v1/journal-entries/{id:int}")]
    public async Task<ActionResult<JournalEntry>> Get(int id)
    {
        var entry = await journalService.GetAsync(id);
        return entry is null ? NotFound() : entry;
    }

    [HttpPost("api/v1/ledgers/{ledgerId:int}/journal-entries")]
    public async Task<ActionResult<JournalEntry>> Create(int ledgerId, CreateJournalEntryDto dto)
    {
        try
        {
            var entry = await journalService.CreateAsync(ledgerId, dto);
            return CreatedAtAction(nameof(Get), new { id = entry.Id }, entry);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("api/v1/journal-entries/{id:int}")]
    public async Task<IActionResult> Update(int id, CreateJournalEntryDto dto)
    {
        try
        {
            var result = await journalService.UpdateAsync(id, dto);
            if (!result.Success)
            {
                return result.Entry is null ? NotFound(new { error = result.Error }) : BadRequest(new { error = result.Error });
            }

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("api/v1/journal-entries/{id:int}/post")]
    public async Task<ActionResult<JournalEntry>> Post(int id)
    {
        try
        {
            var result = await journalService.PostAsync(id);
            if (!result.Success)
            {
                return result.Entry is null ? NotFound(new { error = result.Error }) : BadRequest(new { error = result.Error });
            }

            return result.Entry!;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("api/v1/journal-entries/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await journalService.DeleteAsync(id);
        if (!result.Success)
        {
            return result.Error == "Journal entry not found." ? NotFound(new { error = result.Error }) : BadRequest(new { error = result.Error });
        }

        return NoContent();
    }
}
