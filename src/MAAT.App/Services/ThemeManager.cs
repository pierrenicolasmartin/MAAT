// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Windows;

namespace MAAT.App.Services;

public enum AppTheme
{
    Dark,
    Light,
}

/// <summary>
/// Bascule de thème à chaud : remplace le dictionnaire de palette fusionné
/// (index 0) par la palette claire ou sombre. Les styles de contrôles utilisent
/// <c>DynamicResource</c>, donc l'interface se met à jour instantanément.
/// </summary>
public static class ThemeManager
{
    private const string DarkUri = "pack://application:,,,/MAAT.App;component/Themes/Dark.xaml";
    private const string LightUri = "pack://application:,,,/MAAT.App;component/Themes/Light.xaml";

    public static AppTheme Current { get; private set; } = AppTheme.Light;

    public static void Apply(AppTheme theme)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary
        {
            Source = new Uri(theme == AppTheme.Light ? LightUri : DarkUri, UriKind.Absolute),
        };

        // La palette est toujours en première position (les styles la référencent en DynamicResource).
        if (dicts.Count > 0)
        {
            dicts[0] = palette;
        }
        else
        {
            dicts.Add(palette);
        }
        Current = theme;
    }

    public static AppTheme Toggle()
    {
        Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
        return Current;
    }

    /// <summary>
    /// Applique une préférence : « Light », « Dark », ou autre (« Auto ») =
    /// thème du système Windows.
    /// </summary>
    public static void ApplyPreference(string pref) => Apply(pref switch
    {
        "Light" => AppTheme.Light,
        "Dark" => AppTheme.Dark,
        _ => DetectSystem(),
    });

    /// <summary>
    /// Thème clair/sombre du système Windows (registre AppsUseLightTheme).
    /// Repli sur clair si indéterminable.
    /// </summary>
    public static AppTheme DetectSystem()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v)
            {
                return v == 0 ? AppTheme.Dark : AppTheme.Light;
            }
        }
        catch
        {
            // Registre inaccessible : repli sur clair.
        }
        return AppTheme.Light;
    }
}