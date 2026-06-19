// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Collections.ObjectModel;
using MAAT.Storage;

namespace MAAT.App.ViewModels;

/// <summary>
/// Une identité (groupe AD ou utilisateur) pour la fenêtre Identités. Les membres
/// d'un groupe ne s'affichent qu'au clic (toggle), à la manière du rapport HTML.
/// </summary>
public sealed class IdentityItemViewModel : ObservableObject
{
    private bool _expanded;

    public IdentityItemViewModel(IdentityRow row)
    {
        Identity = row.Identity;
        ShortName = ExtractName(row.Identity);
        ItemCount = row.ItemCount;
        Members = new ObservableCollection<string>();
        if (!string.IsNullOrWhiteSpace(row.Members))
        {
            foreach (var m in row.Members.Split(", ", StringSplitOptions.RemoveEmptyEntries))
            {
                Members.Add(m.Trim());
            }
        }
        IsGroup = Members.Count > 0;
        ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded, () => IsGroup);
    }

    public string Identity { get; }
    public string ShortName { get; }
    public int ItemCount { get; }
    public bool IsGroup { get; }
    public ObservableCollection<string> Members { get; }

    public string CountText =>
        $"{ItemCount} " + Localization.LocalizationManager.T(ItemCount > 1 ? "Id_Items" : "Id_Item");
    public string MembersToggleText =>
        $"▸ {Members.Count} " + Localization.LocalizationManager.T(Members.Count > 1 ? "Ace_Members" : "Ace_Member");

    public bool IsExpanded
    {
        get => _expanded;
        set => SetProperty(ref _expanded, value);
    }

    public RelayCommand ToggleCommand { get; }

    /// <summary>Nom court (dernier segment après « \ »).</summary>
    public static string ExtractName(string identity)
    {
        int i = identity.LastIndexOf('\\');
        return i >= 0 ? identity[(i + 1)..] : identity;
    }
}