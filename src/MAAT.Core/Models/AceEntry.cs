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
/// Une entrée de contrôle d'accès (ACE) traduite, telle qu'affichée dans les
/// rapports. Les droits et la portée sont déjà localisés en français.
/// </summary>
public sealed class AceEntry
{
    /// <summary>Identité (compte ou groupe), ex. « DOMAINE\GroupeRH ».</summary>
    public required string Identity { get; init; }

    /// <summary>Autoriser ou Refuser.</summary>
    public AceType Type { get; init; }

    /// <summary>Droits NTFS traduits en français (ex. « Contrôle total »).</summary>
    public required string RightsFr { get; init; }

    /// <summary>Portée d'héritage traduite (ex. « Ce dossier, sous-dossiers et fichiers »).</summary>
    public required string ScopeFr { get; init; }

    /// <summary>Vrai si l'ACE est héritée, faux si elle est explicite.</summary>
    public bool IsInherited { get; init; }

    /// <summary>
    /// Source de l'ACE telle qu'affichée dans la colonne « Source » des exports :
    /// pour une ACE explicite, le chemin de l'élément lui-même ; pour une ACE
    /// héritée, le chemin de l'ancêtre le plus proche qui la définit, ou
    /// « Source inconnue » si aucun n'a été trouvé dans l'arborescence auditée.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Membres résolus si l'identité est un groupe AD (chaîne « Nom (sam), ... »),
    /// ou null/vide sinon. Rempli lors de la phase de résolution AD.
    /// </summary>
    public string? ResolvedMembers { get; set; }
}