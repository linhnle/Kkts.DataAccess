using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Kkts.DataAccess.DataProtection;

public sealed class DataProtectionContinuationTokenProtector : IContinuationTokenProtector
{
    readonly IDataProtector protector;

    public DataProtectionContinuationTokenProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        protector = provider.CreateProtector("Kkts.DataAccess.ForwardCursor.v1");
    }

    public byte[] Protect(byte[] payload) => protector.Protect(payload);

    public byte[] Unprotect(byte[] protectedPayload)
    {
        try
        {
            return protector.Unprotect(protectedPayload);
        }
        catch (CryptographicException)
        {
            throw new InvalidContinuationTokenException();
        }
    }
}
