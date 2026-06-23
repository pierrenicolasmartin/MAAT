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
/// Détail d'une identité sélectionnée (vue Identités) : informations d'en-tête
/// (type, présence, membres) et table <b>paginée</b> des emplacements où elle
/// possède une ACE directe. La pagination interroge SQLite page par page pour
/// rester réactive sur les identités présentes sur des dizaines de milliers
/// d'éléments. La taille de page choisie est mémorisée pour la session.
/// </summary>
public sealed class IdentityDetailViewModel : ObservableObject
{
    /// <summary>Tailles de page proposées dans le sélecteur.</summary>
    public static readonly int[] PageSizes = { 25, 50, 100, 200 };

    // Mémorisation de la taille de page pour la durée de la session (évite de
    // re-régler le sélecteur à chaque changement d'identité).
    private static int s_pageSize = 50;

    private readonly AuditReadRepository _repo;
    private readonly long _runId;
    private readonly IdentityListItemViewModel _identity;
    private readonly int _total;
    private int _page;

    public IdentityDetailViewModel(AuditReadRepository repo, long runId, IdentityListItemViewModel identity)
    {
        _repo = repo;
        _runId = runId;
        _identity = identity;
        _total = repo.CountIdentityLocations(runId, identity.Identity);

        PrevPageCommand = new RelayCommand(() => GoToPage(_page - 1), () => _page > 0);
        NextPageCommand = new RelayCommand(() => GoToPage(_page + 1), () => (_page + 1) * PageSize < _total);
        LoadPage();
    }

    // ── En-tête (relais depuis l'identité de la liste maître) ──
    public string Identity => _identity.Identity;
    public string TypeText => _identity.TypeText;
    public string PresentOnText => _identity.PresentOnText;
    public string MembersCountText => _identity.MembersCountText;
    public bool HasMembers => _identity.HasMembers;
    public ObservableCollection<string> Members => _identity.Members;

    // ── Table des emplacements (page courante) ──
    public ObservableCollection<IdentityLocationViewModel> Locations { get; } = new();
    public bool HasLocations => _total > 0;

    public RelayCommand PrevPageCommand { get; }
    public RelayCommand NextPageCommand { get; }

    /// <summary>Taille de page (sélecteur). Mémorisée pour la session ; recharge à la première page.</summary>
    public int PageSize
    {
        get => s_pageSize;
        set
        {
            if (value <= 0 || value == s_pageSize) { return; }
            s_pageSize = value;
            OnPropertyChanged();
            _page = 0;
            LoadPage();
        }
    }

    /// <summary>Plage affichée, ex. « 1–50 sur 320 ».</summary>
    public string RangeText
    {
        get
        {
            if (_total == 0) { return LocalizationManager.T("Scan_Range", 0, 0, 0); }
            int from = _page * PageSize + 1;
            int to = Math.Min(_total, (_page + 1) * PageSize);
            return LocalizationManager.T("Scan_Range", from, to, _total);
        }
    }

    private void GoToPage(int page)
    {
        int maxPage = Math.Max(0, (_total - 1) / PageSize);
        _page = Math.Clamp(page, 0, maxPage);
        LoadPage();
    }

    private void LoadPage()
    {
        Locations.Clear();
        foreach (var row in _repo.GetIdentityLocations(_runId, _identity.Identity, _page * PageSize, PageSize))
        {
            Locations.Add(new IdentityLocationViewModel(row));
        }
        OnPropertyChanged(nameof(RangeText));
        PrevPageCommand.RaiseCanExecuteChanged();
        NextPageCommand.RaiseCanExecuteChanged();
    }
}
