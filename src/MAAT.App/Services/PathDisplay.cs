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
internal static class PathDisplay
{
    /// <summary>Retire le préfixe <c>\\?\</c> (et <c>\\?\UNC\</c> → <c>\\</c>) d'un chemin.</summary>
    public static string Strip(string? path)
    {
        if (string.IsNullOrEmpty(path)) { return path ?? string.Empty; }
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) { return @"\\" + path[8..]; }
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) { return path[4..]; }
        return path;
    }
}
