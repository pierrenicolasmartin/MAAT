// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MAAT.App.Services;

/// <summary>
/// Accorde la barre de titre native (DWM) au thème de l'application : mode sombre
/// immersif, et sous Windows 11 couleurs de légende / bordure / texte identiques à la
/// barre d'application (la barre de titre et l'en-tête ne forment qu'un seul bandeau).
/// Sans effet (et sans erreur) sur les versions de Windows qui ignorent ces attributs.
/// </summary>
public static class TitleBarTheme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Applique le thème à chaque fenêtre dès son affichage.</summary>
    public static void Register()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => Apply((Window)s)));
    }

    /// <summary>Réapplique le thème à toutes les fenêtres ouvertes (bascule à chaud).</summary>
    public static void ApplyAll()
    {
        if (Application.Current is null) { return; }
        foreach (Window w in Application.Current.Windows)
        {
            Apply(w);
        }
    }

    public static void Apply(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) { return; }
        try
        {
            int dark = ThemeManager.Current == AppTheme.Dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            SetColor(hwnd, DWMWA_CAPTION_COLOR, "SurfaceColor");
            SetColor(hwnd, DWMWA_BORDER_COLOR, "BorderStrongColor");
            SetColor(hwnd, DWMWA_TEXT_COLOR, "TextColor");
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private static void SetColor(IntPtr hwnd, int attribute, string resourceKey)
    {
        if (Application.Current?.TryFindResource(resourceKey) is Color c)
        {
            int colorRef = c.R | (c.G << 8) | (c.B << 16); // COLORREF = 0x00BBGGRR
            DwmSetWindowAttribute(hwnd, attribute, ref colorRef, sizeof(int));
        }
    }
}
