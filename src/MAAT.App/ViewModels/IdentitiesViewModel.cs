// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using MAAT.Storage;
using Microsoft.Win32;

namespace MAAT.App.ViewModels;

/// <summary>
/// Contenu de la fenêtre Identités : groupes AD (avec membres au clic) puis
/// utilisateurs, recherche et export CSV (façon panneau Identités du rapport HTML).
/// </summary>
public sealed class IdentitiesViewModel : ObservableObject
{
    private readonly List<IdentityItemViewModel> _all;
    private string _searchText = string.Empty;

    public IdentitiesViewModel(IReadOnlyList<IdentityRow> rows)
    {
        _all = rows.Select(r => new IdentityItemViewModel(r)).ToList();
        Groups = new ObservableCollection<IdentityItemViewModel>();
        Users = new ObservableCollection<IdentityItemViewModel>();
        ExportCsvCommand = new RelayCommand(ExportCsv);
        ApplyFilter();
    }

    public ObservableCollection<IdentityItemViewModel> Groups { get; }
    public ObservableCollection<IdentityItemViewModel> Users { get; }

    public RelayCommand ExportCsvCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) { ApplyFilter(); } }
    }

    public string Summary =>
        Localization.LocalizationManager.T("Id_Summary", Groups.Count, Users.Count);

    private void ApplyFilter()
    {
        string s = _searchText.Trim();
        bool Match(IdentityItemViewModel i) =>
            string.IsNullOrEmpty(s)
            || i.Identity.Contains(s, StringComparison.OrdinalIgnoreCase)
            || i.Members.Any(m => m.Contains(s, StringComparison.OrdinalIgnoreCase));

        Groups.Clear();
        Users.Clear();
        foreach (var i in _all.Where(Match))
        {
            (i.IsGroup ? Groups : Users).Add(i);
        }
        OnPropertyChanged(nameof(Summary));
    }

    private void ExportCsv()
    {
        var dialog = new SaveFileDialog
        {
            Title = Localization.LocalizationManager.T("Id_DlgExport"),
            Filter = Localization.LocalizationManager.T("Filter_Csv"),
            FileName = $"{Localization.LocalizationManager.T("Id_FileName")}_{System.DateTime.Now:yyyy-MM-dd_HHmm}.csv",
            DefaultExt = ".csv",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string grp = Localization.LocalizationManager.T("Id_CsvGroup");
        string usr = Localization.LocalizationManager.T("Id_CsvUser");
        string noMember = Localization.LocalizationManager.T("Id_CsvNoMember");

        var sb = new StringBuilder();
        sb.Append('﻿'); // BOM
        sb.AppendLine(Localization.LocalizationManager.T("Id_CsvHeader"));
        foreach (var g in _all.Where(i => i.IsGroup))
        {
            if (g.Members.Count == 0)
            {
                sb.AppendLine($"\"{grp}\";\"{Esc(g.Identity)}\";\"{noMember}\";\"{g.ItemCount}\"");
            }
            else
            {
                foreach (var m in g.Members)
                {
                    sb.AppendLine($"\"{grp}\";\"{Esc(g.Identity)}\";\"{Esc(m)}\";\"{g.ItemCount}\"");
                }
            }
        }
        foreach (var u in _all.Where(i => !i.IsGroup))
        {
            sb.AppendLine($"\"{usr}\";\"{Esc(u.Identity)}\";\"-\";\"{u.ItemCount}\"");
        }
        File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(false));
    }

    private static string Esc(string s) => s.Replace("\"", "\"\"");
}