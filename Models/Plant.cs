using System.ComponentModel.DataAnnotations;

namespace water_me.Models;

public class Plant
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = null!;

    [Required, StringLength(200)]
    public string SpeciesName { get; set; } = null!;

    [StringLength(200)]
    public string? Nickname { get; set; }

    [Range(1, 365)]
    public int WateringFrequencyDays { get; set; }

    [Required, StringLength(200)]
    public string WateringAmount { get; set; } = null!;

    public DateTime? LastWateredAt { get; set; }

    public DateTime? PreviousLastWateredAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
