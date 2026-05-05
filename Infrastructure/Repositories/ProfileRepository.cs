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

    public async Task<Profile?> UpdateProfileAsync(string userId, ProfileDto profileDto, CancellationToken cancellationToken)
    {
        var profile = await _db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (profile == null)
            throw new Exception("Profile not found");

        if (profileDto == null)
            throw new ArgumentNullException(nameof(profileDto));

        profile.FirstName = profileDto.FirstName ?? string.Empty;
        profile.LastName = profileDto.LastName ?? string.Empty;
        profile.Bio = profileDto.Bio ?? string.Empty;

        if (user != null)
        {
            user.Email = profileDto.Email ?? user.Email;
            user.UserName = profileDto.Username ?? user.UserName;
            _db.Users.Update(user);
        }

        _db.Profiles.Update(profile);

        await _db.SaveChangesAsync(cancellationToken);
        return profile;
    }

    public async Task<Profile?> UpdateTelegramIdAsync(string userId, UpdateTelegramIdDto updateTelegramIdDto, CancellationToken cancellationToken)
    {
        var profile = await _db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (profile == null)
            throw new Exception("Profile not found");

        if (updateTelegramIdDto == null)
            throw new ArgumentNullException(nameof(updateTelegramIdDto));

        profile.TelegramId = updateTelegramIdDto.TelegramId;
        _db.Profiles.Update(profile);

        await _db.SaveChangesAsync(cancellationToken);
        return profile;
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