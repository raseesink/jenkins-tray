using Foundation;
using JenkinsTray.Core;
using Security;

namespace JenkinsTray.MacOS;

public sealed class MacSecretStore : ISecretStore
{
    private static SecRecord Query(string reference) => new(SecKind.GenericPassword) { Service = "io.jenkinstray.desktop", Account = reference };
    private static void Check(SecStatusCode status)
    {
        if (status != SecStatusCode.Success) throw new InvalidOperationException("Keychain refused the operation (" + status + ").");
    }
    public Task<string?> ReadAsync(string reference, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var query = Query(reference);
        using var record = SecKeyChain.QueryAsRecord(query, out var status);
        if (status == SecStatusCode.ItemNotFound) return null;
        Check(status);
        using var decoded = record?.ValueData?.ToString(NSStringEncoding.UTF8);
        return decoded?.ToString();
    }, cancellationToken);

    public Task WriteAsync(string reference, string secret, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var query = Query(reference);
        using var data = NSData.FromString(secret, NSStringEncoding.UTF8);
        using var value = new SecRecord(SecKind.GenericPassword) { ValueData = data };
        var status = SecKeyChain.Update(query, value);
        if (status == SecStatusCode.ItemNotFound)
        {
            query.ValueData = data;
            status = SecKeyChain.Add(query);
        }
        Check(status);
    }, cancellationToken);

    public Task RemoveAsync(string reference, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var query = Query(reference);
        var status = SecKeyChain.Remove(query);
        if (status != SecStatusCode.ItemNotFound) Check(status);
    }, cancellationToken);
}
