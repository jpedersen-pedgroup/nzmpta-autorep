using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Autorep.Web.Services;

/// <summary>
/// The link in a new account's welcome email, which sets its first password. A password-reset
/// link lasts an hour, as the reset emails say, but a welcome email can sit unread for days, so
/// its link comes from a token provider of its own and lasts a week. /Account/ResetPassword takes
/// either kind: it swaps a good invitation for a reset token, so setting the first password is an
/// ordinary reset. Both kinds carry the security stamp, so both stop working once it is set.
/// </summary>
public static class Invitation
{
    public const string TokenProvider = "Invitation";
    public const string Purpose = "SetFirstPassword";
    public static readonly TimeSpan Lifespan = TimeSpan.FromDays(7);

    /// <summary>How long a password-reset link (Identity's default provider) lasts.</summary>
    public static readonly TimeSpan ResetLinkLifespan = TimeSpan.FromHours(1);
}

/// <summary>Its own options type, so the reset links' one-hour lifespan doesn't reach it, and its
/// own <see cref="DataProtectionTokenProviderOptions.Name"/>, so one kind of token can never pass
/// for the other.</summary>
public class InvitationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public InvitationTokenProviderOptions()
    {
        Name = Invitation.TokenProvider;
        TokenLifespan = Invitation.Lifespan;
    }
}

public class InvitationTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<InvitationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class;
