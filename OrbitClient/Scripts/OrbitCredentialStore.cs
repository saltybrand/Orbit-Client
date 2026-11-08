using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace OrbitClient.Scripts;

public interface IOrbitCredentialStore
{
    string? Read(string project);
    void Write(string project, string refreshToken);
    void Delete(string project);
}

public sealed class OrbitCredentialStore : IOrbitCredentialStore
{
    private static string Target(string project) => "OrbitClient:Supabase:" +
      Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(project)));

    public string? Read(string project)
    {
        if (!CredRead(Target(project), 1, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == 1168) return null;
            throw new IOException("Windows could not read your saved sign-in.");
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return Marshal.PtrToStringUni(credential.Blob, (int)credential.BlobSize / 2);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Write(string project, string refreshToken)
    {
        var bytes = Encoding.Unicode.GetBytes(refreshToken);
        if (bytes.Length > 2560) throw new IOException("The session is too large to save securely.");
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            var credential = new Credential
            {
                Type = 1,
                TargetName = Target(project),
                UserName = "Orbit session",
                Blob = pointer,
                BlobSize = (uint)bytes.Length,
                Persist = 2
            };
            if (!CredWrite(ref credential, 0))
                throw new IOException("Windows could not save your sign-in securely. Try without Remember me.");
        }
        finally
        {
            Array.Clear(bytes);
            for (var i = 0; i < bytes.Length; i++) Marshal.WriteByte(pointer, i, 0);
            Marshal.FreeHGlobal(pointer);
        }
    }

    public void Delete(string project)
    {
        if (!CredDelete(Target(project), 1, 0) && Marshal.GetLastWin32Error() != 1168)
            throw new IOException("Windows could not remove your saved sign-in.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [
      return: MarshalAs(UnmanagedType.Bool)
    ]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [
      return: MarshalAs(UnmanagedType.Bool)
    ]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [
      return: MarshalAs(UnmanagedType.Bool)
    ]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}