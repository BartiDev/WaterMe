using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using water_me.Models;
using water_me.Services;

namespace water_me.Pages.Plants;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IPlantService _plantService;

    public IndexModel(ApplicationDbContext db, UserManager<IdentityUser> userManager, IPlantService plantService)
    {
        _db = db;
        _userManager = userManager;
        _plantService = plantService;
    }

    public IList<Plant> Plants { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var userId = _userManager.GetUserId(User) ?? throw new InvalidOperationException("Authenticated user has no ID claim.");
        Plants = await _db.Plants
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.CreatedAt)
            .AsNoTracking()
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
        var userId = _userManager.GetUserId(User) ?? throw new InvalidOperationException("Authenticated user has no ID claim.");
        var plant = await _db.Plants
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (plant == null) return NotFound();

        plant.PreviousLastWateredAt = plant.LastWateredAt;
        plant.LastWateredAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new JsonResult(new { status = GetStatus(plant) });
    }

    public async Task<IActionResult> OnPostUnwaterAsync(int id)
    {
        var userId = _userManager.GetUserId(User) ?? throw new InvalidOperationException("Authenticated user has no ID claim.");
        var plant = await _db.Plants
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (plant == null) return NotFound();

        plant.LastWateredAt = plant.PreviousLastWateredAt;
        plant.PreviousLastWateredAt = null;
        await _db.SaveChangesAsync();

        return new JsonResult(new { status = GetStatus(plant) });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var userId = _userManager.GetUserId(User) ?? throw new InvalidOperationException("Authenticated user has no ID claim.");
        var deleted = await _plantService.DeleteAsync(id, userId);
        if (!deleted) return NotFound();
        return RedirectToPage("/Plants/Index");
    }
}
