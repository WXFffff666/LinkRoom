namespace LinkRoom.Core;

/// <summary>
/// Interface for the main window's ViewModel-facing API.
/// Allows the Gui library to interact with the WPF window without referencing WPF types.
/// </summary>
public interface IMainWindowView
{
    void AppendLog(string line);
    void ShowCreatedRoom(string roomId);
    string GetCreatePassword();
}