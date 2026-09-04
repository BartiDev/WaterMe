using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using water_me.Models;
using water_me.Services;

namespace water_me.Pages.Plants;

public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IWateringScheduleService _scheduleService;
    private readonly IPlantService _plantService;

    public EditModel(ApplicationDbContext db, UserManager<IdentityUser> userManager, IWateringScheduleService scheduleService, IPlantService plantService)
    {
        _db = db;
        _userManager = userManager;
        _scheduleService = scheduleService;
        _plantService = plantService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public int PlantId { get; private set; }

    public class InputModel
    {
        [Required, StringLength(200)]
        public string SpeciesName { get; set; } = "";

        [StringLength(200)]
        public string? Nickname { get; set; }

        [Required, Range(1, 365)]
        public int WateringFrequencyDays { get; set; }

        [Required, StringLength(200)]
        public string WateringAmount { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var userId = _userManager.GetUserId(User) ?? throw new InvalidOperationException("Authenticated user has no ID claim.");
        var plant = await _db.Plants.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (plant == null) return NotFound();

        PlantId = id;
        Input = new InputModel
        {
            SpeciesName = plant.SpeciesName,
            Nickname = plant.Nickname,
            WateringFrequencyDays = plant.WateringFrequencyDays,
            WateringAmount = plant.WateringAmount
        };
        return Page();
    }

    public async Task<IActionResult> OnPostSuggestAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.SpeciesName))
            return new JsonResult(new { success = false });

        var result = await _scheduleService.GetScheduleAsync(Input.SpeciesName);
        return new JsonResult(new { success = result.Success, frequencyDays = result.FrequencyDays, amount = result.Amount });
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            PlantId = id;
            return Page();
        }

        var userId = _userManager.GetUserId(User) ?? throw new InvalidOperationException("Authenticated user has no ID claim.");
        var plant = await _db.Plants.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (plant == null) return NotFound();

        plant.SpeciesName = Input.SpeciesName;
        plant.Nickname = Input.Nickname;
        plant.WateringFrequencyDays = Input.WateringFrequencyDays;
        plant.WateringAmount = Input.WateringAmount;
        await _db.SaveChangesAsync();

        return RedirectToPage("/Plants/Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var userId = _userManager.GetUserId(User) ?? throw new InvalidOperationException("Authenticated user has no ID claim.");
        var deleted = await _plantService.DeleteAsync(id, userId);
        if (!deleted) return NotFound();
        return RedirectToPage("/Plants/Index");
    }
}
