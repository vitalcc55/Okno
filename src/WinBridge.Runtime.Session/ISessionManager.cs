using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Session;

public interface ISessionManager
{
    SessionSnapshot GetSnapshot();

    AttachedWindow? GetAttachedWindow();

    SessionMutation Attach(WindowDescriptor window, string matchStrategy);
}
