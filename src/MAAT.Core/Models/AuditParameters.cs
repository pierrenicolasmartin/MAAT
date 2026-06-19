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
/// Paramètres d'un audit, saisis par l'utilisateur avant le lancement.
/// Regroupe les choix des étapes 1 à 4 du script (chemin, profondeur,
/// dossiers/fichiers, contenu). Le format d'export et l'emplacement de
/// sauvegarde relèvent de la couche UI/Export, pas du moteur.
/// </summary>
public sealed class AuditParameters
{
    /// <summary>Chemin racine (UNC ou local) déjà résolu et nettoyé.</summary>
    public required string RootPath { get; init; }

    /// <summary>
    /// Profondeur d'analyse. 0 = illimitée. 1 = racine seule,
    /// 2 = racine + 1 sous-niveau, etc. (sémantique alignée sur V1.0.4).
    /// </summary>
    public int Depth { get; init; }

    /// <summary>Étendue : dossiers seuls ou dossiers + fichiers.</summary>
    public AuditScope Scope { get; init; } = AuditScope.FoldersOnly;

    /// <summary>Contenu : droits, taille ou les deux.</summary>
    public AuditContent Content { get; init; } = AuditContent.RightsOnly;

    /// <summary>
    /// Audit des droits : inclure les comptes/groupes locaux et intégrés
    /// (BUILTIN, AUTORITÉ NT, comptes de la machine, Tout le monde…). Si faux,
    /// seules les identités du domaine Active Directory sont conservées.
    /// Sans effet en mode « Taille uniquement ».
    /// </summary>
    public bool IncludeLocalAccounts { get; init; } = true;

    /// <summary>
    /// Code de langue figé au moment du scan (« fr » / « en ») : détermine la
    /// langue des libellés de droits et de portée matérialisés dans la base.
    /// </summary>
    public string Lang { get; init; } = "fr";

    /// <summary>Vrai si les fichiers doivent être audités (et pas seulement les dossiers).</summary>
    public bool AuditFiles => Scope == AuditScope.FoldersAndFiles;

    /// <summary>Vrai si les droits NTFS doivent être lus.</summary>
    public bool AuditRights => Content is AuditContent.RightsOnly or AuditContent.RightsAndSize;

    /// <summary>Vrai si les tailles doivent être calculées.</summary>
    public bool AuditSize => Content is AuditContent.SizeOnly or AuditContent.RightsAndSize;
}