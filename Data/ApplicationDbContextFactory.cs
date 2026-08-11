using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace water_me;

// Used by EF Core CLI tools (dotnet ef migrations add) at design time only.
// Not used at runtime — the app's DI registration takes over there.
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Data Source=waterme.db")
            .Options;
        return new ApplicationDbContext(options);
    }
}
