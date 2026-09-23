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
/// États particuliers d'un élément audité, persistés avec lui (colonne <c>flags</c>).
/// Ils rendent visibles, dans l'arbre et les exports, les cas où l'audit n'a pas pu
/// tout lire — au lieu de faire disparaître silencieusement l'élément ou son contenu.
/// </summary>
[Flags]
public enum ItemFlags
{
    None = 0,

    /// <summary>ACL illisible (accès refusé, erreur) : l'élément est conservé dans l'arbre, sans ACE.</summary>
    AclUnreadable = 1,

    /// <summary>Contenu du dossier non listable, ou listé partiellement (accès refusé, erreur réseau…).</summary>
    ContentUnreadable = 2,

    /// <summary>Lien DFS : parcouru de façon transparente vers sa cible.</summary>
    DfsLink = 4,

    /// <summary>Boucle détectée (le dossier réapparaît dans sa propre ascendance) : non parcouru.</summary>
    Cycle = 8,

    /// <summary>Profondeur de sécurité atteinte : contenu non parcouru.</summary>
    DepthLimit = 16,

    /// <summary>DACL nulle : aucune restriction, accès total pour tous.</summary>
    NullDacl = 32,
}
