namespace MolasLubes.Infrastructure.Security;

public class AccountLockedException : Exception
{
    public DateTime LockedUntil { get; }

    public AccountLockedException(DateTime lockedUntil)
        : base($"Account locked until {lockedUntil:O}")
    {
        LockedUntil = lockedUntil;
    }
}
