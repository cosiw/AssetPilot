namespace AssetPilot.Infrastructure.Accounts;

public interface IWindowsCredentialManager
{
    WindowsCredential? Read(string targetName);
    void Write(WindowsCredential credential);
    IReadOnlyList<WindowsCredential> Enumerate(string targetFilter);
    void Delete(string targetName);
}
