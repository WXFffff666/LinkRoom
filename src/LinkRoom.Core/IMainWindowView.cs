namespace LinkRoom.Core;

public interface IMainWindowView
{
    void AppendLog(string line);
    void ShowCreatedRoom(string roomId, string? linkCode = null, string? qrPayload = null);
    string GetCreatePassword();
    void SetPasswordText(string pw);
    void SetUpdateLabel(string text);
}
