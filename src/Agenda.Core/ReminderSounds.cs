namespace Agenda.Core;

public static class ReminderSounds
{
    public static readonly IReadOnlyList<string> Choices = Array.AsReadOnly(new[] {
        "Default", "Reminder", "Mail", "IM", "SMS", "Silent"
    });

    public static string Resolve(string? defaultSound, string? eventSound) =>
        Choices.Contains(eventSound!) ? eventSound! :
        Choices.Contains(defaultSound!) ? defaultSound! : "Default";
}
