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
/// Un groupe AD et ses membres résolus (récursivement, groupes imbriqués aplatis).
/// Alimente le panneau « Identités » du rapport HTML.
/// </summary>
public sealed class IdentityGroup
{
    /// <summary>Nom SAM du groupe (sans le préfixe domaine).</summary>
    public required string Sam { get; init; }

    /// <summary>Nom d'affichage si disponible, sinon le SAM.</summary>
    public string? Display { get; init; }

    /// <summary>
    /// Membres résolus : libellés « Nom (sam) » ou « sam ».
    /// Vide si l'identité n'est pas un groupe ou si AD est indisponible.
    /// </summary>
    public List<string> Members { get; } = new();
}