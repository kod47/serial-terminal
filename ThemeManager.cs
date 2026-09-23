using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SerialTerminal;

public static class ThemeManager
{
    public static bool SystemIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(bool dark)
    {
        Application.Current.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;

        Set("TermBackground", dark ? "#1E1E1E" : "#FFFFFF");
        Set("TermRx",         dark ? "#E4E4E4" : "#1E1E1E");
        Set("TermTx",         dark ? "#4FC1FF" : "#0451A5");
        Set("TermInfo",       dark ? "#6CC070" : "#107C10");
        Set("TermError",      dark ? "#F48771" : "#C42B1C");
        Set("TermTimestamp",  dark ? "#7F7F7F" : "#8A8A8A");
        Set("TermSelection",  dark ? "#264F78" : "#ADD6FF");
    }

    private static void Set(string key, string color) =>
        Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
}
