// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.IO;

namespace MAAT.App.Services;

/// <summary>
/// Liste des projets .maat récents (écran d'accueil), persistée dans les préférences.
/// Seuls les chemins sont conservés (aucune donnée d'audit) ; les fichiers disparus
/// sont ignorés à l'affichage.
/// </summary>
public static class RecentProjects
{
    private const int MaxEntries = 6;

    /// <summary>Place <paramref name="path"/> en tête de liste (sans doublon).</summary>
    public static void Add(string path)
    {
        path = PathDisplay.Strip(path);
        var s = UserSettings.Load();
        s.RecentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        s.RecentProjects.Insert(0, path);
        if (s.RecentProjects.Count > MaxEntries)
        {
            s.RecentProjects.RemoveRange(MaxEntries, s.RecentProjects.Count - MaxEntries);
        }
        s.Save();
    }

    /// <summary>Vide la liste (confidentialité : aucun chemin conservé).</summary>
    public static void Clear()
    {
        var s = UserSettings.Load();
        s.RecentProjects.Clear();
        s.Save();
    }

    /// <summary>Projets récents dont le fichier existe encore, plus récent en tête.</summary>
    public static IReadOnlyList<string> Existing()
    {
        try
        {
            return UserSettings.Load().RecentProjects.Where(File.Exists).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
