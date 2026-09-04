namespace water_me.Services;

public interface IPlantService
{
    Task<bool> DeleteAsync(int id, string userId);
}
