using Agenda.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Agenda.App.Services;

// Windows owns presentation, sound, Do Not Disturb, and Notification Center.
public sealed class MeetingNotificationService : IDisposable
{
    private readonly Action<string> _open;
    private bool _registered;
    public string? Error { get; private set; }

    public MeetingNotificationService(Action<string> open)
    {
        _open=open;
        var stage="support check";
        try {
            if(!AppNotificationManager.IsSupported()) {
                Error="Windows app notifications are unavailable. Install the Windows App Runtime and run Agenda without administrator privileges.";
                return;
            }
            stage="activation handler";
            AppNotificationManager.Default.NotificationInvoked+=Invoked;
            stage="registration";
            AppNotificationManager.Default.Register();
            _registered=true;
            Diagnostic($"Registered; Windows setting: {AppNotificationManager.Default.Setting}");
        }
        catch(Exception error) {
            try { AppNotificationManager.Default.NotificationInvoked-=Invoked; } catch { }
            try {
                File.AppendAllText(Path.Combine(App.DataDirectory,"errors.log"),
                    $"{DateTimeOffset.UtcNow:u} Notification {stage}: {error.GetType().Name} HRESULT 0x{error.HResult:X8}\n{error.StackTrace}\n");
            } catch(IOException) { } catch(UnauthorizedAccessException) { }
            Error=error.HResult==unchecked((int)0x8007007E)
                ? "A required notification component is missing (0x8007007E). Rebuild or reinstall Agenda."
                : $"Windows notification registration failed (0x{error.HResult:X8}). Restart Agenda without administrator privileges and check the Windows App Runtime installation.";
        }
    }

    public bool Available {
        get {
            if(!_registered)return false;
            try { return AppNotificationManager.Default.Setting==AppNotificationSetting.Enabled; }
            catch { return false; }
        }
    }

    private void Invoked(AppNotificationManager sender,AppNotificationActivatedEventArgs args)
    {
        if(args.Arguments.TryGetValue("event",out var key) && key.Length==64 && key.All(Uri.IsHexDigit)) {
            Diagnostic("Notification activated");
            _open(key);
        }
    }

    public bool Show(MeetingReminder reminder,string time,string calendar,string sound)
    {
        if(!Available)return false;
        var builder=new AppNotificationBuilder()
            .AddArgument("event",reminder.EventKey)
            .AddText(reminder.Event.Subject)
            .AddText($"{time} · {calendar}")
            .AddText(reminder.MinutesBefore==0?"Starting now":$"Starts in {reminder.MinutesBefore} minutes");
        sound=ReminderSounds.Resolve(sound,null);
        if(sound=="Silent")builder.MuteAudio();
        else builder.SetAudioEvent(Enum.Parse<AppNotificationSoundEvent>(sound));
        var notification=builder.BuildNotification();
        notification.Group=reminder.EventKey[..16];
        notification.Tag=reminder.MinutesBefore.ToString(System.Globalization.CultureInfo.InvariantCulture);
        notification.Expiration=reminder.Event.End;
        AppNotificationManager.Default.Show(notification);
        Diagnostic($"Windows accepted notification ID {notification.Id}");
        return notification.Id!=0;
    }

    private static void Diagnostic(string message)
    {
        if(!Environment.GetCommandLineArgs().Contains("--notification-diagnostics"))return;
        try { File.AppendAllText(Path.Combine(App.DataDirectory,"notifications.log"),$"{DateTimeOffset.UtcNow:u} {message}\n"); }
        catch(IOException) { } catch(UnauthorizedAccessException) { }
    }

    public async Task ClearAsync(string? eventKey=null)
    {
        if(!_registered)return;
        try {
            if(eventKey is null)await AppNotificationManager.Default.RemoveAllAsync();
            else await AppNotificationManager.Default.RemoveByGroupAsync(eventKey[..16]);
        }
        catch { /* Windows may be shutting down; reminders are never scheduled ahead. */ }
    }

    public void Dispose()
    {
        if(!_registered)return;
        AppNotificationManager.Default.NotificationInvoked-=Invoked;
        try { AppNotificationManager.Default.Unregister(); } catch { }
        _registered=false;
    }
}
