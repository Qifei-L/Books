using Books.Application.DTOs;
using Books.Domain.Entities;
using Books.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Books.Api.Controllers;

[ApiController]
[Route("api/v1/entities")]
public class EntitiesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Entity>>> GetAll()
    {
        return await db.Entities
            .OrderBy(x => x.Code)
            .ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Entity>> Get(int id)
    {
        var entity = await db.Entities.FindAsync(id);
        return entity is null ? NotFound() : entity;
    }

    [HttpPost]
    public async Task<ActionResult<Entity>> Create(CreateEntityDto dto)
    {
        var entity = new Entity
        {
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            IsActive = dto.IsActive
        };

        db.Entities.Add(entity);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, entity);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, CreateEntityDto dto)
    {
        var entity = await db.Entities.FindAsync(id);
        if (entity is null)
        {
            return NotFound();
        }

        entity.Code = dto.Code.Trim();
        entity.Name = dto.Name.Trim();
        entity.IsActive = dto.IsActive;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await db.Entities.FindAsync(id);
        if (entity is null)
        {
            return NotFound();
        }

        entity.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
