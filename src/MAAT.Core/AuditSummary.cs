// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using MAAT.Core.Models;

namespace MAAT.Core;

/// <summary>
/// Compteurs de synthèse d'un audit (sans les éléments eux-mêmes, qui sont
/// streamés vers un <see cref="IAuditSink"/>). Équivaut aux totaux affichés en
/// fin de script.
/// </summary>
public sealed class AuditSummary
{
    public required AuditParameters Parameters { get; init; }

    /// <summary>Racine réellement auditée (résolue / canonisée).</summary>
    public required string RootPath { get; init; }

    public TimeSpan Elapsed { get; init; }

    /// <summary>Nombre de dossiers énumérés (avant filtrage ACL).</summary>
    public int FolderCount { get; init; }

    /// <summary>Nombre de fichiers énumérés (0 en mode Dossiers uniquement).</summary>
    public int FileCount { get; init; }

    /// <summary>Nombre d'éléments effectivement émis vers le puits.</summary>
    public int ItemCount { get; init; }

    /// <summary>Nombre total d'ACE émises (après filtrage des comptes système).</summary>
    public int AceTotal { get; init; }

    /// <summary>Jonctions / liens symboliques rencontrés (audités, non parcourus).</summary>
    public int ReparseCount { get; init; }

    /// <summary>Éléments dont la lecture ACL a échoué.</summary>
    public int AclErrorCount { get; init; }

    /// <summary>Erreurs de résolution AD (timeouts, échecs).</summary>
    public int AdErrorCount { get; init; }

    /// <summary>Identités AD résolues lors du pré-chargement du cache.</summary>
    public int AdResolved { get; init; }

    /// <summary>Vrai si Active Directory était disponible pendant l'audit.</summary>
    public bool AdAvailable { get; init; }
}