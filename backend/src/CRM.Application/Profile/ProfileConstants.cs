namespace CRM.Application.Profile;

/// <summary>
/// Single source of truth for the self-service profile feature's magic values — the avatar
/// storage container, the upload size cap, and the persisted field-length limits — so the
/// controller, the FluentValidation rules, and the EF column mappings can never drift apart.
/// </summary>
public static class ProfileConstants
{
    /// <summary><see cref="Common.Interfaces.IFileStorage"/> container (logical bucket) for avatar images.</summary>
    public const string AvatarContainer = "avatars";

    /// <summary>Hard cap on an uploaded avatar (5 MB). Also fed to <c>[RequestSizeLimit]</c>.</summary>
    public const long MaxAvatarBytes = 5 * 1024 * 1024;

    /// <summary>Max length of the persisted avatar storage key (the opaque "avatars/{guid}.ext").</summary>
    public const int MaxAvatarKeyLength = 300;

    /// <summary>Max length of the free-text bio / about-me.</summary>
    public const int MaxBioLength = 1000;

    /// <summary>Max length of the location / city text.</summary>
    public const int MaxLocationLength = 120;

    /// <summary>Max length of the phone number (mirrors the Employee phone cap).</summary>
    public const int MaxPhoneLength = 40;
}
