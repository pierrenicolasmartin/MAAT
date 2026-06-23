// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Collections.ObjectModel;
using System.ComponentModel;
using MAAT.App.Localization;
using MAAT.Storage;

namespace MAAT.App.ViewModels;

/// <summary>
/// Une entrée ACL affichée en ligne sous un élément (façon rapport HTML).
/// Le Type est coloré (Autoriser = vert, Refuser = rouge) sans autre mise en
/// évidence. Les membres d'un groupe AD ne s'affichent qu'au clic.
/// </summary>
public sealed class AceLineViewModel : ObservableObject, IDisposable
{
    private const int MemberPreviewMax = 12;
    private readonly AclFilterState _filter;
    private bool _membersExpanded;
    private bool _sourceExpanded;

    public AceLineViewModel(AceRow row, AclFilterState filter)
    {
        _filter = filter;
        Identity = row.Identity;
        IsDeny = row.AceType == 1;
        RightsFr = row.RightsFr;
        ScopeFr = row.ScopeFr;
        IsInherited = row.IsInherited;
        SourcePath = row.SourcePath;

        Members = row.ResolvedMembers ?? string.Empty;
        MemberList = new ObservableCollection<string>();
        if (!string.IsNullOrWhiteSpace(Members))
        {
            foreach (var m in Members.Split(", ", StringSplitOptions.RemoveEmptyEntries))
            {
                MemberList.Add(m.Trim());
            }
        }
        // Aperçu tronqué pour les très gros groupes (le reste est résumé en « +N autres… »).
        MemberPreview = new ObservableCollection<string>(MemberList.Take(MemberPreviewMax));
        HiddenMemberCount = Math.Max(0, MemberList.Count - MemberPreviewMax);

        ToggleMembersCommand = new RelayCommand(() => IsMembersExpanded = !IsMembersExpanded, () => HasMembers);
        ToggleSourceCommand = new RelayCommand(() => IsSourceExpanded = !IsSourceExpanded, () => IsInherited);
        _filter.PropertyChanged += OnFilterChanged;
    }

    public string Identity { get; }
    public bool IsDeny { get; }
    public string TypeText => LocalizationManager.T(IsDeny ? "Ace_Deny" : "Ace_Allow");
    public string RightsFr { get; }
    public string ScopeFr { get; }
    public bool IsInherited { get; }
    public string InheritedText => LocalizationManager.T(IsInherited ? "Ace_Inherited" : "Ace_Explicit");
    public string SourcePath { get; }

    public string Members { get; }
    public ObservableCollection<string> MemberList { get; }
    public bool HasMembers => MemberList.Count > 0;

    /// <summary>Nombre de membres seul (lien cliquable de la colonne « Membres »), « — » pour un compte.</summary>
    public string MemberCountText => HasMembers ? MemberList.Count.ToString() : LocalizationManager.T("Scan_NoMembers");

    /// <summary>Aperçu (tronqué) des membres + reliquat résumé.</summary>
    public ObservableCollection<string> MemberPreview { get; }
    public int HiddenMemberCount { get; }
    public bool HasMoreMembers => HiddenMemberCount > 0;
    public string MoreMembersText => LocalizationManager.T("Ace_MoreMembers", HiddenMemberCount);

    /// <summary>Libellé du panneau de membres : « Membres · IDENTITÉ (N) ».</summary>
    public string MembersPanelLabel =>
        $"{LocalizationManager.T("Ace_MembersLabel")} · {Identity} ({MemberList.Count})";

    public bool IsMembersExpanded
    {
        get => _membersExpanded;
        set => SetProperty(ref _membersExpanded, value);
    }

    public RelayCommand ToggleMembersCommand { get; }

    // ───── Source d'héritage (dépli sous la ligne, « Hérité ») ─────
    public bool IsSourceExpanded
    {
        get => _sourceExpanded;
        set => SetProperty(ref _sourceExpanded, value);
    }

    public RelayCommand ToggleSourceCommand { get; }
    public string InheritedFromLabel => LocalizationManager.T("Ace_InheritedFrom");

    /// <summary>Visibilité selon le filtre courant (Tous / Explicites / Hérités).</summary>
    public bool IsVisible => _filter.IsVisible(IsInherited);

    private void OnFilterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AclFilterState.Filter))
        {
            OnPropertyChanged(nameof(IsVisible));
        }
    }

    /// <summary>Désabonne du filtre partagé (évite de garder la ligne en vie).</summary>
    public void Dispose() => _filter.PropertyChanged -= OnFilterChanged;
}