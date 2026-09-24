// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Globalization;
using System.IO;
using MAAT.App.Localization;

namespace MAAT.App.ViewModels;

/// <summary>Entrée « projet récent » de l'écran d'accueil.</summary>
public sealed class RecentProjectViewModel
{
    public RecentProjectViewModel(string path, Action<string> open)
    {
        FullPath = path;
        Name = Path.GetFileNameWithoutExtension(path);
        Folder = Path.GetDirectoryName(path) ?? string.Empty;
        try
        {
            var culture = CultureInfo.GetCultureInfo(LocalizationManager.Instance.ActiveCode == "en" ? "en-GB" : "fr-FR");
            DateText = File.GetLastWriteTime(path).ToString("d MMM yyyy", culture);
        }
        catch
        {
            DateText = string.Empty;
        }
        OpenCommand = new RelayCommand(() => open(path));
    }

    public string FullPath { get; }
    public string Name { get; }
    public string Folder { get; }
    public string DateText { get; }
    public RelayCommand OpenCommand { get; }
}
