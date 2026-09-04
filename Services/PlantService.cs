using Microsoft.EntityFrameworkCore;
using water_me.Models;

namespace water_me.Services;

public class PlantService : IPlantService
{
    private readonly ApplicationDbContext _db;

    public PlantService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<bool> DeleteAsync(int id, string userId)
    {
        var plant = await _db.Plants.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (plant == null) return false;

        _db.Plants.Remove(plant);
        await _db.SaveChangesAsync();
        return true;
    }
}
