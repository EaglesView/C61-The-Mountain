using System;
using System.Threading.Tasks;
using Core.Profile.Domain;

namespace Core.Profile.Application;

public sealed class PlayerProfileUseCase
{
    private readonly IPlayerProfileRepository _repository;

    public PlayerProfileUseCase(IPlayerProfileRepository repository)
    {
        _repository = repository;
    }

    public async Task<PlayerProfile> GetOrCreateProfileAsync(
        string userId, string defaultUsername)
    {
        var profile = await _repository.GetByUserIdAsync(userId);
        if (profile is not null) return profile;

        profile = new PlayerProfile(userId, defaultUsername);
        await _repository.SaveAsync(profile);
        return profile;
    }

    public Task<PlayerProfile?> GetProfileAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Task.FromResult<PlayerProfile?>(null);
        return _repository.GetByUserIdAsync(userId);
    }

    public async Task<PlayerProfile> UpdateUsernameAsync(string userId, string newUsername)
    {
        if (string.IsNullOrWhiteSpace(newUsername) || newUsername.Trim().Length < 2)
            throw new ArgumentException("Username must be at least 2 characters.");

        var profile = await _repository.GetByUserIdAsync(userId)
            ?? throw new InvalidOperationException("Profile not found.");

        profile.UpdateUsername(newUsername.Trim());
        await _repository.SaveAsync(profile);
        return profile;
    }

    public async Task<PlayerProfile> UpdateHatIdAsync(string userId, string hatId)
    {
        var profile = await _repository.GetByUserIdAsync(userId)
            ?? throw new InvalidOperationException("Profile not found.");

        profile.UpdateHatId(hatId);
        await _repository.SaveAsync(profile);
        return profile;
    }
}
