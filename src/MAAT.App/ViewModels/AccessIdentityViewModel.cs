// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using MAAT.Storage;

namespace MAAT.App.ViewModels;

/// <summary>
/// Une entrée du menu déroulant « contrôle d'accès » : une identité (groupe ou
/// utilisateur) affichée par son nom court, ou la sentinelle « tous les éléments ».
/// </summary>
public sealed class AccessIdentityViewModel : ObservableObject
{
    private readonly string _display;

    private AccessIdentityViewModel(string display, string identity, string sam, bool isGroup, bool isSentinel)
    {
        _display = display;
        Identity = identity;
        Sam = sam;
        IsGroup = isGroup;
        IsSentinel = isSentinel;
    }

    /// <summary>Libellé affiché ; la sentinelle est traduite à la volée selon la langue.</summary>
    public string Display => IsSentinel ? Localization.LocalizationManager.T("Access_All") : _display;
    public string Identity { get; }
    public string Sam { get; }
    public bool IsGroup { get; }
    public bool IsSentinel { get; }

    public override string ToString() => Display;

    public static AccessIdentityViewModel Sentinel { get; } =
        new("Tous les éléments", string.Empty, string.Empty, false, true);

    // La sentinelle se retraduit dynamiquement au changement de langue de l'interface.
    static AccessIdentityViewModel()
    {
        Localization.LocalizationManager.Instance.PropertyChanged +=
            (_, _) => Sentinel.OnPropertyChanged(nameof(Display));
    }

    public static AccessIdentityViewModel From(IdentityRow row)
    {
        bool isGroup = !string.IsNullOrWhiteSpace(row.Members);
        string name = IdentityItemViewModel.ExtractName(row.Identity);
        string icon = isGroup ? "👥 " : "👤 ";
        return new AccessIdentityViewModel(icon + name, row.Identity, name, isGroup, false);
    }
}