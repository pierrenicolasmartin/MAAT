// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Collections.ObjectModel;
using MAAT.App.Localization;
using MAAT.Storage;

namespace MAAT.App.ViewModels;

/// <summary>
/// Une identité de l'explorateur (vue « Identités ») : groupe ou compte, nombre de
/// dossiers où elle a un droit, et (pour un groupe) ses membres. Un groupe se reconnaît
/// à la présence de membres résolus (seuls les groupes en ont).
/// </summary>
public sealed class IdentityListItemViewModel
{
    public IdentityListItemViewModel(IdentityRow row)
    {
        Identity = row.Identity;
        IsGroup = !string.IsNullOrWhiteSpace(row.Members);
        FolderCount = row.ItemCount;

        Members = new ObservableCollection<string>();
        if (!string.IsNullOrWhiteSpace(row.Members))
        {
            foreach (var m in row.Members.Split(", ", StringSplitOptions.RemoveEmptyEntries))
            {
                Members.Add(m.Trim());
            }
        }
    }

    public string Identity { get; }
    public bool IsGroup { get; }
    public int FolderCount { get; }
    public ObservableCollection<string> Members { get; }

    /// <summary>Marqueur : losange plein pour un groupe, rond plein pour un compte.</summary>
    public string Marker => IsGroup ? "◆" : "●";

    /// <summary>
    /// Groupe avéré : membres résolus (Active Directory), groupe local intégré
    /// (<c>BUILTIN\…</c>, domaine qui ne contient que des groupes) ou « Tout le monde ».
    /// Sans cette certitude, aucun type n'est affiché (plutôt qu'un type erroné).
    /// </summary>
    public bool IsGroupLike => IsGroup
        || Identity.StartsWith(@"BUILTIN\", StringComparison.OrdinalIgnoreCase)
        || WellKnownGroups.Contains(Identity[(Identity.LastIndexOf('\\') + 1)..]);

    /// <summary>Groupes bien connus (noms localisés FR / EN de Windows).</summary>
    private static readonly HashSet<string> WellKnownGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "Tout le monde", "Everyone", "Utilisateurs authentifiés", "Authenticated Users",
    };

    public string TypeText => IsGroupLike ? LocalizationManager.T("Scan_TypeGroup") : string.Empty;
    public bool HasTypeText => IsGroupLike;
    public string FolderCountText => LocalizationManager.T("Scan_FolderCount", FolderCount);
    public string PresentOnText => LocalizationManager.T("Scan_PresentOn", FolderCount);
    public string MembersCountText => LocalizationManager.T("Scan_MembersN", Members.Count);
    public bool HasMembers => Members.Count > 0;
}
