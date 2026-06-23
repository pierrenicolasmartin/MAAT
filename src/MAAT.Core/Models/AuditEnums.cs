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
/// Type d'une entrée de contrôle d'accès (ACE).
/// Reprend la distinction Autoriser / Refuser introduite en V1.0.4 du script.
/// </summary>
public enum AceType
{
    /// <summary>Autoriser (Allow).</summary>
    Allow = 0,

    /// <summary>Refuser (Deny) — prioritaire sur les autorisations.</summary>
    Deny = 1,
}

/// <summary>
/// Étendue de l'audit : dossiers seuls ou dossiers + fichiers.
/// Équivalent du choix [D]/[F] (étape 3 du script).
/// </summary>
public enum AuditScope
{
    /// <summary>Dossiers uniquement (par défaut, recommandé pour gros volumes).</summary>
    FoldersOnly = 0,

    /// <summary>Dossiers + Fichiers.</summary>
    FoldersAndFiles = 1,
}

/// <summary>
/// Contenu de l'audit : droits, taille, ou les deux.
/// Équivalent du choix [1]/[2]/[3] (étape 4 du script).
/// </summary>
public enum AuditContent
{
    /// <summary>Droits NTFS uniquement (par défaut).</summary>
    RightsOnly = 1,

    /// <summary>Taille uniquement.</summary>
    SizeOnly = 2,

    /// <summary>Droits + Taille.</summary>
    RightsAndSize = 3,
}