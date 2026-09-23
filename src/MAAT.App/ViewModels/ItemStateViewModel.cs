// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using MAAT.App.Localization;
using MAAT.Core.Models;

namespace MAAT.App.ViewModels;

/// <summary>
/// Un état particulier d'un élément (ACL illisible, contenu non listable, lien DFS…),
/// affiché en encart explicatif dans le volet de détail de l'explorateur.
/// </summary>
public sealed class ItemStateViewModel
{
    private ItemStateViewModel(string key, bool isWarning)
    {
        Title = LocalizationManager.T("State_" + key);
        Description = LocalizationManager.T("State_" + key + "Desc");
        IsWarning = isWarning;
    }

    public string Title { get; }
    public string Description { get; }

    /// <summary>Avertissement (l'audit est incomplet pour cet élément) ; sinon information.</summary>
    public bool IsWarning { get; }

    // Ordre d'affichage : du plus grave (lecture impossible) au plus informatif.
    private static readonly (ItemFlags Flag, string Key, bool Warning)[] Catalog =
    {
        (ItemFlags.AclUnreadable, "AclUnreadable", true),
        (ItemFlags.ContentUnreadable, "ContentUnreadable", true),
        (ItemFlags.Cycle, "Cycle", true),
        (ItemFlags.DepthLimit, "DepthLimit", true),
        (ItemFlags.NullDacl, "NullDacl", true),
        (ItemFlags.DfsLink, "DfsLink", false),
    };

    /// <summary>Drapeaux signalant un audit incomplet (pastille d'avertissement dans l'arbre).</summary>
    public const ItemFlags WarningFlags =
        ItemFlags.AclUnreadable | ItemFlags.ContentUnreadable | ItemFlags.Cycle | ItemFlags.DepthLimit;

    /// <summary>
    /// États à afficher pour un élément. <paramref name="noAce"/> : droits audités, ACL lue,
    /// mais aucune entrée retenue (DACL vide ou entrées exclues par le filtre d'identités).
    /// <paramref name="reparse"/> : jonction ou lien symbolique (audité, cible non parcourue).
    /// </summary>
    public static IReadOnlyList<ItemStateViewModel> For(ItemFlags flags, bool noAce, bool reparse = false)
    {
        var list = new List<ItemStateViewModel>();
        foreach (var (flag, key, warning) in Catalog)
        {
            if ((flags & flag) != 0) { list.Add(new ItemStateViewModel(key, warning)); }
        }
        if (reparse) { list.Add(new ItemStateViewModel("Reparse", isWarning: false)); }
        if (noAce && (flags & (ItemFlags.AclUnreadable | ItemFlags.NullDacl)) == 0)
        {
            list.Add(new ItemStateViewModel("NoAce", isWarning: false));
        }
        return list;
    }

    /// <summary>Libellés courts des états (info-bulle de l'arbre), ou null si aucun.</summary>
    public static string? Tooltip(ItemFlags flags, bool reparse = false)
    {
        var titles = Catalog.Where(c => (flags & c.Flag) != 0).Select(c => LocalizationManager.T("State_" + c.Key)).ToList();
        if (reparse) { titles.Add(LocalizationManager.T("State_Reparse")); }
        return titles.Count == 0 ? null : string.Join(" · ", titles);
    }
}
