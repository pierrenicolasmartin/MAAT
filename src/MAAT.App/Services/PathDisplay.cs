// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System;

namespace MAAT.App.Services;

/// <summary>
/// Mise en forme de chemins pour l'<b>affichage</b> uniquement. Les boîtes de dialogue
/// d'enregistrement renvoient parfois la forme « extended-length » (<c>\\?\…</c>),
/// nécessaire à l'écriture des chemins longs mais inélégante à l'écran : on la retire
/// pour l'affichage (et l'ouverture shell), sans toucher au chemin utilisé pour les
/// opérations fichier.
/// </summary>
public static class PathDisplay
{
    /// <summary>Retire le préfixe <c>\\?\</c> (et <c>\\?\UNC\</c> → <c>\\</c>) d'un chemin.</summary>
    public static string Strip(string? path)
    {
        if (string.IsNullOrEmpty(path)) { return path ?? string.Empty; }
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) { return @"\\" + path[8..]; }
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) { return path[4..]; }
        return path;
    }

    /// <summary>
    /// Chemin présenté relativement à la racine auditée, préfixé du nom de la racine
    /// (« Partages\Finance\Paie ») : lisible même quand la racine est longue. Un chemin
    /// hors racine est renvoyé tel quel.
    /// </summary>
    public static string Relative(string path, string root)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) { return path ?? string.Empty; }
        string r = root.TrimEnd('\\');
        if (string.Equals(path.TrimEnd('\\'), r, StringComparison.OrdinalIgnoreCase))
        {
            return RootName(r); // la racine elle-même
        }
        if (path.Length > r.Length && path[r.Length] == '\\'
            && path.StartsWith(r, StringComparison.OrdinalIgnoreCase))
        {
            return RootName(r) + path[r.Length..];
        }
        return path;
    }

    /// <summary>
    /// Nom affichable d'une racine : dernier segment (« Partages »), lecteur (« C: »)
    /// ou partage UNC entier (« \\srv\partage »).
    /// </summary>
    public static string RootName(string root)
    {
        string r = Strip(root).TrimEnd('\\');
        if (r.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // \\srv\partage[\sous-dossier…] : sous la racine du partage, dernier segment.
            int shareEnd = r.IndexOf('\\', r.IndexOf('\\', 2) + 1);
            if (shareEnd < 0) { return r; }
        }
        int i = r.LastIndexOf('\\');
        return i >= 0 && i < r.Length - 1 ? r[(i + 1)..] : r;
    }
}
