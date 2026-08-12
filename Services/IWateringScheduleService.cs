namespace water_me.Services;

public record WateringScheduleResult(bool Success, int FrequencyDays, string Amount);

public interface IWateringScheduleService
{
    Task<WateringScheduleResult> GetScheduleAsync(string speciesName, CancellationToken ct = default);
}
