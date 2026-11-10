using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace AssetPilot.Infrastructure.Accounts;

public sealed class WindowsCredentialManager : IWindowsCredentialManager
{
    private const uint GenericCredentialType = 1;
    private const uint LocalMachinePersistence = 2;
    private const int NotFoundError = 1168;

    public WindowsCredential? Read(string targetName)
    {
        if (!CredRead(targetName, GenericCredentialType, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFoundError)
            {
                return null;
            }

            throw new Win32Exception(error);
        }

        try
        {
            return ToManagedCredential(credentialPointer);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public void Write(WindowsCredential credential)
    {
        var targetNamePointer = IntPtr.Zero;
        var userNamePointer = IntPtr.Zero;
        var credentialBlobPointer = IntPtr.Zero;

        try
        {
            targetNamePointer = Marshal.StringToCoTaskMemUni(credential.TargetName);
            userNamePointer = Marshal.StringToCoTaskMemUni(credential.UserName);

            if (credential.CredentialBlob.Length > 0)
            {
                credentialBlobPointer = Marshal.AllocCoTaskMem(credential.CredentialBlob.Length);
                Marshal.Copy(credential.CredentialBlob, 0, credentialBlobPointer, credential.CredentialBlob.Length);
            }

            var nativeCredential = new NativeCredential
            {
                Type = GenericCredentialType,
                TargetName = targetNamePointer,
                CredentialBlobSize = (uint)credential.CredentialBlob.Length,
                CredentialBlob = credentialBlobPointer,
                Persist = LocalMachinePersistence,
                UserName = userNamePointer
            };

            if (!CredWrite(ref nativeCredential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(targetNamePointer);
            Marshal.FreeCoTaskMem(userNamePointer);
            Marshal.FreeCoTaskMem(credentialBlobPointer);
        }
    }

    public IReadOnlyList<WindowsCredential> Enumerate(string targetFilter)
    {
        if (!CredEnumerate(targetFilter, 0, out var count, out var credentialPointers))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFoundError)
            {
                return [];
            }

            throw new Win32Exception(error);
        }

        try
        {
            var credentials = new List<WindowsCredential>((int)count);
            for (var index = 0; index < count; index++)
            {
                var credentialPointer = Marshal.ReadIntPtr(credentialPointers, index * IntPtr.Size);
                credentials.Add(ToManagedCredential(credentialPointer));
            }

            return credentials;
        }
        finally
        {
            CredFree(credentialPointers);
        }
    }

    public void Delete(string targetName)
    {
        if (!CredDelete(targetName, GenericCredentialType, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != NotFoundError)
            {
                throw new Win32Exception(error);
            }
        }
    }

    private static WindowsCredential ToManagedCredential(IntPtr credentialPointer)
    {
        var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
        var credentialBlob = new byte[credential.CredentialBlobSize];
        if (credentialBlob.Length > 0)
        {
            Marshal.Copy(credential.CredentialBlob, credentialBlob, 0, credentialBlob.Length);
        }

        return new WindowsCredential(
            Marshal.PtrToStringUni(credential.TargetName) ?? string.Empty,
            Marshal.PtrToStringUni(credential.UserName) ?? string.Empty,
            credentialBlob);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string targetName, uint type, uint flags, out IntPtr credentialPointer);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredEnumerate(string targetFilter, uint flags, out uint count, out IntPtr credentialPointers);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string targetName, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}
