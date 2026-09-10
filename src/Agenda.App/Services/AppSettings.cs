using System.Text.Json;
namespace Agenda.App.Services;

public sealed class AppSettings
{
    public bool ShowWeekNumbers { get; set; } = true;
    public bool UseIsoWeeks { get; set; }
    public bool InterceptClock { get; set; }
    public string ClientId { get; set; } = "";
    public string SourceMode { get; set; } = "windows";
    public HashSet<string> HiddenCalendarIds { get; set; } = [];
    public static AppSettings Load(string directory)
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(directory, "settings.json"))) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}
