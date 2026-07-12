using System.Runtime.InteropServices;
using System.Text;

namespace LinkRoom.Core;

public static class NotificationService
{
    public static void Show(string title, string message)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var ps = $"""
                    [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
                    $template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
                    $text = $template.GetElementsByTagName('text')
                    $text[0].AppendChild($template.CreateTextNode('{Escape(title)}')) | Out-Null
                    $text[1].AppendChild($template.CreateTextNode('{Escape(message)}')) | Out-Null
                    $toast = [Windows.UI.Notifications.ToastNotification]::new($template)
                    [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('LinkRoom').Show($toast)
                    """;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{ps.Replace("\"", "\\\"")}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
            }
        }
        catch { /* fallback silent */ }
    }

    static string Escape(string s) => s.Replace("'", "''");
}
