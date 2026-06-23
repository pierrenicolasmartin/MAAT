// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using MAAT.Core.Models;

namespace MAAT.Core.ActiveDirectory;

/// <summary>
/// Résout les membres des groupes Active Directory (récursivement, groupes
/// imbriqués). Toute implémentation doit se dégrader proprement (résultats vides)
/// quand AD est indisponible.
/// </summary>
public interface IAdGroupResolver
{
    /// <summary>Vrai si Active Directory est joignable (machine en domaine).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Résout les membres du groupe identifié par son nom SAM. Renvoie une chaîne
    /// « Nom (sam), ... », ou une chaîne vide si l'identité n'est pas un groupe,
    /// si AD est indisponible, ou en cas d'erreur. Résultat mis en cache.
    /// </summary>
    string Resolve(string sam);

    /// <summary>
    /// Renseigne <see cref="AceEntry.ResolvedMembers"/> de chaque ACE des éléments
    /// fournis (résolution mise en cache par groupe).
    /// </summary>
    void ApplyMembers(IEnumerable<AuditItem> items);

    /// <summary>Renseigne les membres AD des ACE d'un seul élément (accès cache).</summary>
    void ApplyMembers(AuditItem item);
}