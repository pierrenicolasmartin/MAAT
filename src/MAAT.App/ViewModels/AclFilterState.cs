// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

namespace MAAT.App.ViewModels;

/// <summary>Filtre des entrées ACL affichées (façon rapport HTML).</summary>
public enum AclFilter
{
    All,
    Explicit,
    Inherited,
}

/// <summary>
/// État de filtre partagé par toutes les lignes ACL : changer le filtre notifie
/// chaque ligne (qui recalcule sa visibilité), sans recharger l'arbre.
/// </summary>
public sealed class AclFilterState : ObservableObject
{
    private AclFilter _filter = AclFilter.All;

    public AclFilter Filter
    {
        get => _filter;
        set => SetProperty(ref _filter, value);
    }

    /// <summary>Vrai si une ACE (héritée ou non) doit être visible selon le filtre courant.</summary>
    public bool IsVisible(bool isInherited) => _filter switch
    {
        AclFilter.Explicit => !isInherited,
        AclFilter.Inherited => isInherited,
        _ => true,
    };
}