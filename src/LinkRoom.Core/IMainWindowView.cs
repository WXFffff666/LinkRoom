namespace LinkRoom.Core;

public interface IMainWindowView
{
    void AppendLog(string line);
    void ShowCreatedRoom(string roomId, string? linkCode = null);
    string GetCreatePassword();
    void SetPasswordText(string pw);
}
