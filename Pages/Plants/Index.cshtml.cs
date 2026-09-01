using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using water_me.Models;

namespace water_me.Pages.Plants;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;

    public IndexModel(ApplicationDbContext db, UserManager<IdentityUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public IList<Plant> Plants { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var userId = _userManager.GetUserId(User)!;
        Plants = await _db.Plants
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync();
    }

    public string GetStatus(Plant p)
    {
        if (p.LastWateredAt == null)
            return "Water today";

        var daysUntilNext = p.WateringFrequencyDays - (int)(DateTime.UtcNow - p.LastWateredAt.Value).TotalDays;
        return daysUntilNext > 0
            ? $"Next watering in {daysUntilNext} day(s)"
            : $"Overdue by {-daysUntilNext} day(s)";
    }

    public async Task<IActionResult> OnPostWaterAsync(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var plant = await _db.Plants.FindAsync(id);
        if (plant == null || plant.UserId != userId)
            return Forbid();

        plant.PreviousLastWateredAt = plant.LastWateredAt;
        plant.LastWateredAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new JsonResult(new { status = GetStatus(plant) });
    }

    public async Task<IActionResult> OnPostUnwaterAsync(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var plant = await _db.Plants.FindAsync(id);
        if (plant == null || plant.UserId != userId)
            return Forbid();

        plant.LastWateredAt = plant.PreviousLastWateredAt;
        plant.PreviousLastWateredAt = null;
        await _db.SaveChangesAsync();

        return new JsonResult(new { status = GetStatus(plant) });
    }
}
