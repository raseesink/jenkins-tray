using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using JenkinsTray.Core;

namespace JenkinsTray.Windows;

public sealed class WindowsSecretStore : ISecretStore
{
    private const int NotFound = 1168;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Write(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Read(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Delete(string target, uint type, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredFree")] private static extern void Free(IntPtr credential);

    public Task<string?> ReadAsync(string reference, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Read(reference, 1, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound) return null;
            throw new Win32Exception(error, "Credential Manager refused to read the credential.");
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return Marshal.PtrToStringUni(credential.Blob, checked((int)credential.BlobSize / 2));
        }
        finally { Free(pointer); }
    }, cancellationToken);

    public Task WriteAsync(string reference, string secret, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential { TargetName = reference, Type = 1, Blob = blob, BlobSize = (uint)bytes.Length, Persist = 2, UserName = "JenkinsTray" };
            if (!Write(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Credential Manager refused to save the credential.");
        }
        finally
        {
            Array.Clear(bytes);
            for (var i = 0; i < secret.Length * 2; i++) Marshal.WriteByte(blob, i, 0);
            Marshal.FreeHGlobal(blob);
        }
    }, cancellationToken);

    public Task RemoveAsync(string reference, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Delete(reference, 1, 0) && Marshal.GetLastWin32Error() != NotFound)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Credential Manager refused to remove the credential.");
    }, cancellationToken);
}
