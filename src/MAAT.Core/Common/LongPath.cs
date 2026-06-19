// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

namespace MAAT.Core.Common;

/// <summary>
/// Préfixage « chemin long » (<c>\\?\</c>) pour les API Win32 limitées à MAX_PATH
/// (260). Indispensable et cohérent entre l'<b>énumération</b> et la <b>lecture
/// ACL</b> : sans cela, des fichiers profondément imbriqués (sessions, dépôts Git,
/// node_modules…) seraient énumérés mais leur ACL illisible — donc l'élément
/// silencieusement omis (régression de fiabilité).
/// </summary>
internal static class LongPath
{
    /// <summary>
    /// Forme « extended-length » d'un chemin local de lecteur (<c>X:\…</c> →
    /// <c>\\?\X:\…</c>). Les chemins UNC (<c>\\serveur\partage</c>) et déjà préfixés
    /// sont renvoyés tels quels (comportement inchangé sur le réseau).
    /// </summary>
    public static string ToExtended(string path)
        => NeedsPrefix(path) ? @"\\?\" + path : path;

    private static bool NeedsPrefix(string path)
        => path.Length >= 2 && path[1] == ':'
           && !path.StartsWith(@"\\", StringComparison.Ordinal);
}
