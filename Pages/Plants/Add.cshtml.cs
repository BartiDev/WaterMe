using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using water_me.Models;
using water_me.Services;

namespace water_me.Pages.Plants;

public class AddModel : PageModel
{
    private readonly IWateringScheduleService _scheduleService;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;

    public AddModel(IWateringScheduleService scheduleService, ApplicationDbContext db, UserManager<IdentityUser> userManager)
    {
        _scheduleService = scheduleService;
        _db = db;
        _userManager = userManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

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

    public bool ShowScheduleSection =>
        Input.WateringFrequencyDays != 0 ||
        !string.IsNullOrEmpty(Input.WateringAmount) ||
        ModelState.ContainsKey("Input.WateringFrequencyDays") ||
        ModelState.ContainsKey("Input.WateringAmount");

    public Task<IActionResult> OnGetAsync() => Task.FromResult<IActionResult>(Page());

    public async Task<IActionResult> OnPostSuggestAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.SpeciesName))
            return new JsonResult(new { success = false });

        var result = await _scheduleService.GetScheduleAsync(Input.SpeciesName);
        return new JsonResult(new { success = result.Success, frequencyDays = result.FrequencyDays, amount = result.Amount });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var plant = new Plant
        {
            UserId = _userManager.GetUserId(User) ?? throw new InvalidOperationException("Authenticated user has no ID claim."),
            SpeciesName = Input.SpeciesName,
            Nickname = Input.Nickname,
            WateringFrequencyDays = Input.WateringFrequencyDays,
            WateringAmount = Input.WateringAmount,
            CreatedAt = DateTime.UtcNow
        };

        _db.Plants.Add(plant);
        await _db.SaveChangesAsync();

        return RedirectToPage("/Plants/Index");
    }
}
