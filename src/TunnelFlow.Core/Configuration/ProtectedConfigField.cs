using System.Security.Cryptography;
using System.Text;

namespace TunnelFlow.Core.Configuration;

public static class ProtectedConfigField
{
    public const string MachineDpapiV1Prefix = "tf-dpapi:v1:local-machine:";

    public static string ProtectForSharedConfig(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.LocalMachine);
        return MachineDpapiV1Prefix + Convert.ToBase64String(protectedBytes);
    }

    public static ProtectedConfigFieldReadResult UnprotectFromSharedConfig(string protectedValue)
    {
        if (protectedValue.StartsWith(MachineDpapiV1Prefix, StringComparison.Ordinal))
        {
            var payload = protectedValue[MachineDpapiV1Prefix.Length..];
            return new ProtectedConfigFieldReadResult(
                UnprotectDpapi(payload, DataProtectionScope.LocalMachine, "versioned machine-scoped protected config field"),
                ProtectedConfigFieldScheme.VersionedMachineDpapi);
        }

        return UnprotectLegacy(protectedValue);
    }

    private static ProtectedConfigFieldReadResult UnprotectLegacy(string payload)
    {
        Exception? machineScopeFailure = null;
        try
        {
            return new ProtectedConfigFieldReadResult(
                UnprotectDpapi(payload, DataProtectionScope.LocalMachine, "legacy machine-scoped protected config field"),
                ProtectedConfigFieldScheme.LegacyMachineDpapi);
        }
        catch (ProtectedConfigFieldException ex)
        {
            machineScopeFailure = ex;
        }

        try
        {
            return new ProtectedConfigFieldReadResult(
                UnprotectDpapi(payload, DataProtectionScope.CurrentUser, "legacy user-scoped protected config field"),
                ProtectedConfigFieldScheme.LegacyCurrentUserDpapi);
        }
        catch (ProtectedConfigFieldException ex)
        {
            throw new ProtectedConfigFieldException(
                "Protected config field is in the legacy unversioned format, but it could not be decrypted with machine-scoped or current-user DPAPI.",
                ex,
                machineScopeFailure);
        }
    }

    private static string UnprotectDpapi(string base64Payload, DataProtectionScope scope, string description)
    {
        try
        {
            var protectedBytes = Convert.FromBase64String(base64Payload);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            throw new ProtectedConfigFieldException($"Failed to decrypt {description}.", ex);
        }
    }
}

public sealed record ProtectedConfigFieldReadResult(
    string Plaintext,
    ProtectedConfigFieldScheme Scheme)
{
    public bool RequiresMigration => Scheme != ProtectedConfigFieldScheme.VersionedMachineDpapi;
}

public enum ProtectedConfigFieldScheme
{
    VersionedMachineDpapi,
    LegacyMachineDpapi,
    LegacyCurrentUserDpapi
}

public sealed class ProtectedConfigFieldException : Exception
{
    public ProtectedConfigFieldException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ProtectedConfigFieldException(string message, Exception innerException, Exception? machineScopeFailure)
        : base(message, innerException)
    {
        MachineScopeFailure = machineScopeFailure;
    }

    public Exception? MachineScopeFailure { get; }
}
