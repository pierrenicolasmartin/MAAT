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
/// Préfixage « chemin long » pour les API Win32 limitées à MAX_PATH (260).
/// Indispensable et cohérent entre l'<b>énumération</b> et la <b>lecture ACL</b> :
/// sans cela, des éléments profondément imbriqués seraient énumérés mais leur ACL
/// illisible — ou pas énumérés du tout. Le préfixe désactive aussi la normalisation
/// Win32, ce qui rend accessibles les noms à point ou espace final.
/// </summary>
internal static class LongPath
{
    /// <summary>
    /// Forme « extended-length » d'un chemin absolu :
    /// <c>X:\…</c> → <c>\\?\X:\…</c> ; <c>\\serveur\partage\…</c> → <c>\\?\UNC\serveur\partage\…</c>
    /// (partages réseau et espaces de noms DFS : même chemin NT, donc même résolution).
    /// Les chemins déjà préfixés sont renvoyés tels quels.
    /// </summary>
    public static string ToExtended(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return path;
        }
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return @"\\?\UNC\" + path[2..];
        }
        if (path.Length >= 2 && path[1] == ':')
        {
            return @"\\?\" + path;
        }
        return path;
    }
}

/// <summary>Normalisation du chemin racine d'un audit.</summary>
public static class PathNormalizer
{
    /// <summary>
    /// Forme canonique d'une racine d'audit : absolue, séparateurs « \ », « . » et
    /// « .. » résolus, espaces de bord retirés, sans antislash final (sauf racine de
    /// lecteur « C:\ »). Indispensable avant le préfixe <c>\\?\</c>, qui désactive toute
    /// normalisation Win32 : un « C:/Users » non normalisé donnerait un audit vide.
    /// </summary>
    public static string NormalizeRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) { return path; }
        string p = path.Trim();
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return p; // déjà sous forme étendue : pris tel quel
        }
        if (p.Length == 2 && p[1] == ':')
        {
            return p + '\\'; // « C: » seul désignerait le répertoire courant du lecteur
        }
        try
        {
            p = Path.GetFullPath(p);
        }
        catch
        {
            return path.Trim();
        }
        return p.Length > 3 ? p.TrimEnd('\\') : p;
    }
}
