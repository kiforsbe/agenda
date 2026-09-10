using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Agenda.App.Services;

public static class RegionalSettings
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetUserDefaultLocaleName(StringBuilder localeName, int count);

    public static CultureInfo Read()
    {
        CultureInfo.CurrentCulture.ClearCachedData();
        TimeZoneInfo.ClearCachedData();
        var name = new StringBuilder(85);
        var culture = GetUserDefaultLocaleName(name, name.Capacity) > 0
            ? new CultureInfo(name.ToString(), useUserOverride: true)
            : new CultureInfo(CultureInfo.CurrentCulture.Name, useUserOverride: true);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        return culture;
    }
}
