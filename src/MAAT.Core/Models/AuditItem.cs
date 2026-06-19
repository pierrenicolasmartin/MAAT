// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

namespace MAAT.Core.Models;

/// <summary>
/// Un élément audité (dossier ou fichier) avec ses ACL et sa taille.
/// C'est l'unité produite par le moteur et écrite dans SQLite au fil de l'eau.
/// </summary>
public sealed class AuditItem
{
    /// <summary>Chemin complet de l'élément.</summary>
    public required string FullPath { get; init; }

    /// <summary>Nom court (dernier segment), ou la racine elle-même.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Niveau de l'élément dans l'arbre, base 0 : la racine vaut 0, un enfant
    /// direct 1, etc. (sert à l'indentation du rapport HTML — c'est le
    /// <c>depthLevel</c> du script, distinct du paramètre de profondeur d'audit).
    /// </summary>
    public int Depth { get; init; }

    /// <summary>Vrai si c'est un fichier, faux si c'est un dossier.</summary>
    public bool IsFile { get; init; }

    /// <summary>
    /// Vrai si l'élément est un point de reparse (jonction / lien symbolique) :
    /// audité pour ses droits mais non parcouru (anti-boucle, anti-doublon).
    /// </summary>
    public bool IsReparse { get; init; }

    /// <summary>
    /// Taille en octets. Null si la taille n'a pas été calculée (mode Droits seuls)
    /// ou si elle est inconnue suite à un accès refusé.
    /// </summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// Vrai si la taille est partielle (des sous-éléments étaient inaccessibles).
    /// Marquée « ≈ » dans les exports.
    /// </summary>
    public bool SizePartial { get; set; }

    /// <summary>Vrai si au moins une ACE de type Refuser porte sur cet élément.</summary>
    public bool HasDeny { get; set; }

    /// <summary>Entrées ACL traduites associées à cet élément.</summary>
    public List<AceEntry> Acl { get; } = new();
}