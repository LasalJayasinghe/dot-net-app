using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

public class ProfileRepository
{
    private readonly AppDbContext _db;
    public ProfileRepository(AppDbContext db) => _db = db;

    public async Task<Profile?> GetProfileByUserIdAsync(string userId, CancellationToken cancellationToken)
    {
        return await _db.Profiles
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
    }

    public Task UpdateProfileAsync(Profile profile)
    {
        _db.Profiles.Update(profile);
        return Task.CompletedTask;
    }

    public async Task AddProfileAsync(Profile profile)
    {
        _db.Profiles.Add(profile);
        await _db.SaveChangesAsync();
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }
}