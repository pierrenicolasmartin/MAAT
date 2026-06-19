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
using MAAT.App.Localization;
using MAAT.Core;
using MAAT.Core.Diagnostics;
using Microsoft.Win32;

namespace MAAT.App.ViewModels;

/// <summary>
/// Rapport d'analyse affiché à la fin d'un audit : statistiques de synthèse et
/// liste des éléments qui n'ont pas pu être audités (accès refusés, jonctions
/// ignorées, erreurs AD…), avec leur motif localisé. Propose l'export du journal.
/// </summary>
public sealed class AuditReportViewModel : ObservableObject
{
    private readonly IReadOnlyList<ScanLogEntry> _log;
    private readonly string _suggestedName;

    public AuditReportViewModel(AuditSummary summary, IReadOnlyList<ScanLogEntry> log,
        string suggestedName, string? totalSizeText)
    {
        _log = log;
        _suggestedName = suggestedName;
        RootPath = summary.RootPath;
        AuditDate = DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm");

        Stats = new ObservableCollection<StatCard>(BuildStats(summary, totalSizeText));

        // Une carte par type d'incident RÉELLEMENT rencontré (compteur > 0),
        // libellé traduit. Remplace l'unique carte « accès refusés ».
        foreach (var grp in _log.GroupBy(e => IssueRow.CategoryKey(e.Type)))
        {
            bool isError = grp.Any(e => e.Severity == LogSeverity.Error);
            Stats.Add(new StatCard(LocalizationManager.T(grp.Key), grp.Count().ToString("N0"), isAlert: isError));
        }

        Issues = new ObservableCollection<IssueRow>();
        foreach (var e in _log.OrderByDescending(e => e.Severity)) // erreurs d'abord
        {
            Issues.Add(new IssueRow(e));
        }
        IssueCount = Issues.Count;
        ErrorCount = _log.Count(e => e.Severity == LogSeverity.Error);
        WarnCount = _log.Count(e => e.Severity == LogSeverity.Warn);

        ExportLogCommand = new RelayCommand(ExportLog, () => _log.Count > 0);
    }

    public string RootPath { get; }
    public string AuditDate { get; }

    public ObservableCollection<StatCard> Stats { get; }
    public ObservableCollection<IssueRow> Issues { get; }

    public int IssueCount { get; }
    public int ErrorCount { get; }
    public int WarnCount { get; }
    public bool HasIssues => IssueCount > 0;
    public bool NoIssues => IssueCount == 0;

    /// <summary>Sous-titre de la section des éléments ignorés (motifs).</summary>
    public string IssuesSummary => HasIssues
        ? LocalizationManager.T("Rep_IssuesSummary", IssueCount, ErrorCount, WarnCount)
        : LocalizationManager.T("Rep_AllAudited");

    public RelayCommand ExportLogCommand { get; }

    private static IEnumerable<StatCard> BuildStats(AuditSummary s, string? totalSizeText)
    {
        yield return new StatCard(L("Rep_Stat_Folders"), s.FolderCount.ToString("N0"));
        if (s.Parameters.AuditFiles)
        {
            yield return new StatCard(L("Rep_Stat_Files"), s.FileCount.ToString("N0"));
        }
        if (s.Parameters.AuditRights)
        {
            yield return new StatCard(L("Rep_Stat_Aces"), s.AceTotal.ToString("N0"));
        }
        if (s.Parameters.AuditSize && !string.IsNullOrEmpty(totalSizeText))
        {
            yield return new StatCard(L("Rep_Stat_Size"), totalSizeText);
        }
        if (s.ReparseCount > 0)
        {
            yield return new StatCard(L("Rep_Stat_Junctions"), s.ReparseCount.ToString("N0"));
        }
        yield return new StatCard(L("Rep_Stat_Duration"), $"{s.Elapsed.TotalSeconds:N1} s");
        if (s.Parameters.AuditRights)
        {
            yield return s.AdAvailable
                ? new StatCard(L("Rep_Stat_AdResolved"), s.AdResolved.ToString("N0"))
                : new StatCard(L("Rep_Stat_Ad"), L("Rep_Stat_AdOff"));
        }
        // Les incidents (accès refusés, jonctions ignorées, erreurs…) sont
        // ajoutés dynamiquement (une carte par type rencontré) dans le constructeur.
    }

    private static string L(string key) => LocalizationManager.T(key);

    private void ExportLog()
    {
        var dialog = new SaveFileDialog
        {
            Title = L("Rep_DlgExportLog"),
            Filter = L("Filter_LogCsv"),
            FileName = _suggestedName + ".log.csv",
            DefaultExt = ".csv",
        };
        if (dialog.ShowDialog() != true) { return; }

        using var writer = new StreamWriter(dialog.FileName, append: false,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine(L("Rep_LogHeader"));
        foreach (var e in _log)
        {
            writer.WriteLine(string.Join(';',
                Csv(e.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                Csv(L(e.Severity == LogSeverity.Error ? "Sev_Error" : "Sev_Warn")),
                Csv(IssueRow.CategoryLabel(e.Type)),
                Csv(e.Path)));
        }
        ExportMessage = LocalizationManager.T("Rep_LogExported", dialog.FileName);
    }

    private string _exportMessage = string.Empty;
    public string ExportMessage { get => _exportMessage; private set => SetProperty(ref _exportMessage, value); }

    private static string Csv(string value)
        => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
}

/// <summary>Carte de statistique affichée dans le rapport (libellé localisé, valeur).</summary>
public sealed class StatCard
{
    public StatCard(string label, string value, bool isAlert = false)
    {
        Label = label;
        Value = value;
        IsAlert = isAlert;
    }

    public string Label { get; }
    public string Value { get; }

    /// <summary>Vrai pour mettre la carte en évidence (ex. accès refusés &gt; 0).</summary>
    public bool IsAlert { get; }
}

/// <summary>Une ligne « élément non audité » : motif localisé, chemin et détail.</summary>
public sealed class IssueRow
{
    public IssueRow(ScanLogEntry e)
    {
        IsError = e.Severity == LogSeverity.Error;
        Type = CategoryLabel(e.Type);
        Path = string.IsNullOrEmpty(e.Path) ? "—" : e.Path;
        Message = e.Message;
    }

    public bool IsError { get; }
    public string Type { get; }
    public string Path { get; }
    public string Message { get; }

    /// <summary>Clé de catalogue (non localisée) du motif, pour regrouper et traduire.</summary>
    public static string CategoryKey(string code) => code switch
    {
        "ENUM_ACCES_REFUSE" or "ACL_ACCES_REFUSE" or "TAILLE_ACCES_REFUSE" => "Log_AccessDenied",
        "ENUM_CHEMIN_INTROUVABLE" or "ACL_CHEMIN_INTROUVABLE" or "TAILLE_CHEMIN_INTROUVABLE" => "Log_PathNotFound",
        "ENUM_REPARSE_IGNORE" or "TAILLE_REPARSE_IGNORE" => "Log_ReparseSkipped",
        "ACL_INACCESSIBLE" => "Log_AclUnreadable",
        "ERREUR_ACE" => "Log_AceError",
        "AD_CYCLE" or "AD_ERREUR" or "AD_PROFONDEUR_MAX" or "AD_TIMEOUT" => "Log_AdResolution",
        "ENUM_ERREUR" or "ENUM_FICHIERS_ERREUR" or "ACL_ERREUR" or "ACL_ACCESS_ERREUR"
            or "TAILLE_ERREUR_ENUM" or "TAILLE_ENUM_FICHIERS" => "Log_ReadError",
        _ => "Log_Other",
    };

    /// <summary>Traduit un code de type de journal en motif lisible et localisé.</summary>
    public static string CategoryLabel(string code) => LocalizationManager.T(CategoryKey(code));
}