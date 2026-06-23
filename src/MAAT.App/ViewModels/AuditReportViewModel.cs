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
/// Rapport de fin d'audit, hiérarchisé : verdict, métriques clés, rangée de signaux
/// (où l'anomalie — accès refusés — ressort), puis table des éléments non audités.
/// Toutes les valeurs proviennent de l'<see cref="AuditSummary"/> et du journal réel.
/// </summary>
public sealed class AuditReportViewModel : ObservableObject
{
    private const int PreviewCount = 8;
    private readonly IReadOnlyList<ScanLogEntry> _log;
    private readonly string _suggestedName;
    private bool _showAll;

    public AuditReportViewModel(AuditSummary summary, IReadOnlyList<ScanLogEntry> log,
        string suggestedName, string? totalSizeText)
    {
        _log = log;
        _suggestedName = suggestedName;
        RootPath = summary.RootPath;
        AuditDate = DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm");
        FinishedText = L2("Rep_FinishedMeta", AuditDate);
        DurationText = L2("Rep_DurationMeta", FormatDuration(summary.Elapsed));

        // ── Compteurs réels ──
        ErrorCount = _log.Count(e => e.Severity == LogSeverity.Error);
        WarnCount = _log.Count(e => e.Severity == LogSeverity.Warn);
        IssueCount = _log.Count;
        int deniedCount = _log.Count(e => IssueRow.CategoryKey(e.Type) == "Log_AccessDenied");
        int readErrorCount = _log.Count(e => IssueRow.CategoryKey(e.Type) == "Log_ReadError");

        // ── Verdict (déduit du résultat) ──
        Verdict = ErrorCount > 0 ? "error" : IssueCount == 0 ? "success" : "warning";
        VerdictText = L("Verdict_" + Verdict);

        // ── Rangée de signaux : seules les anomalies réellement présentes (> 0) sont
        //    affichées ; Active Directory est un état (toujours montré si droits audités). ──
        Signals = new ObservableCollection<SignalTile>();
        if (deniedCount > 0)
        {
            Signals.Add(new(deniedCount.ToString("N0"), L("Rep_Sig_DeniedTitle"), L("Rep_Sig_DeniedSub"), tone: "alert"));
        }
        if (readErrorCount > 0)
        {
            Signals.Add(new(readErrorCount.ToString("N0"), L("Rep_Sig_ReadTitle"), L("Rep_Sig_ReadSub"), tone: "alert"));
        }
        if (summary.ReparseCount > 0)
        {
            Signals.Add(new(summary.ReparseCount.ToString("N0"), L("Rep_Sig_JunctionTitle"), L("Rep_Sig_JunctionSub"), tone: "neutral"));
        }
        if (summary.Parameters.AuditRights)
        {
            Signals.Add(summary.AdAvailable
                ? new SignalTile(null, L("Rep_Sig_AdTitle"), L("Rep_Sig_AdOn"), tone: "ok", icon: "✓")
                : new SignalTile(null, L("Rep_Sig_AdTitle"), L("Rep_Sig_AdOff"), tone: "warn", icon: "!"));
        }

        // ── Table des éléments non audités (erreurs d'abord) ──
        AllIssues = _log.OrderByDescending(e => e.Severity).Select(e => new IssueRow(e)).ToList();
        Issues = new ObservableCollection<IssueRow>(AllIssues.Take(PreviewCount));

        ShowAllCommand = new RelayCommand(ShowAll, () => HasMore);
        ExportLogCommand = new RelayCommand(ExportLog, () => _log.Count > 0);
    }

    public string RootPath { get; }
    public string AuditDate { get; }
    public string FinishedText { get; }
    public string DurationText { get; }

    /// <summary>« success » / « warning » / « error » — pilote la pastille de verdict.</summary>
    public string Verdict { get; }
    public string VerdictText { get; }

    public ObservableCollection<SignalTile> Signals { get; }
    public bool HasSignals => Signals.Count > 0;

    private IReadOnlyList<IssueRow> AllIssues { get; }
    public ObservableCollection<IssueRow> Issues { get; }

    public int IssueCount { get; }
    public int ErrorCount { get; }
    public int WarnCount { get; }
    public bool HasIssues => IssueCount > 0;
    public bool NoIssues => IssueCount == 0;
    /// <summary>Vrai si aucune erreur (compteur d'erreurs affiché en vert).</summary>
    public bool ErrorsClean => ErrorCount == 0;

    public string CountItemsText => L2("Rep_CountItems", IssueCount);
    public string CountErrorsText => L2("Rep_CountErrors", ErrorCount);
    public string CountWarnsText => L2("Rep_CountWarns", WarnCount);

    public bool HasMore => !_showAll && IssueCount > PreviewCount;
    public string RemainingText => L2("Rep_ShowRemaining", IssueCount - Issues.Count);

    public RelayCommand ShowAllCommand { get; }
    public RelayCommand ExportLogCommand { get; }

    private void ShowAll()
    {
        _showAll = true;
        Issues.Clear();
        foreach (var r in AllIssues) { Issues.Add(r); }
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(RemainingText));
        ShowAllCommand.RaiseCanExecuteChanged();
    }

    private static string FormatDuration(TimeSpan t)
        => t.TotalSeconds < 60 ? $"{t.TotalSeconds:N1} s" : t.ToString(@"mm\:ss");

    private static string L(string key) => LocalizationManager.T(key);
    private static string L2(string key, object arg) => LocalizationManager.T(key, arg);

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

/// <summary>Une tuile de la rangée de signaux (accès refusés, erreurs de lecture, jonctions, AD).</summary>
public sealed class SignalTile
{
    public SignalTile(string? number, string title, string subtitle, string tone, string? icon = null)
    {
        Number = number;
        Title = title;
        Subtitle = subtitle;
        Tone = tone;       // « alert » (rouge) / « warn » (ambre) / « ok » (vert) / « neutral »
        Icon = icon;
    }

    public string? Number { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Tone { get; }
    public string? Icon { get; }

    public bool HasNumber => !string.IsNullOrEmpty(Number);
    public bool HasIcon => !string.IsNullOrEmpty(Icon);
}

/// <summary>Une ligne « élément non audité » : motif localisé, chemin, gravité, teinte du badge.</summary>
public sealed class IssueRow
{
    public IssueRow(ScanLogEntry e)
    {
        IsError = e.Severity == LogSeverity.Error;
        Reason = CategoryLabel(e.Type);
        Path = string.IsNullOrEmpty(e.Path) ? "—" : e.Path;
        SeverityText = LocalizationManager.T(IsError ? "Sev_Error" : "Sev_Warn");

        string key = CategoryKey(e.Type);
        // Badge rouge pour les accès refusés et les erreurs ; ambre sinon (jonctions, autres avertissements).
        BadgeRed = key == "Log_AccessDenied" || IsError;
    }

    public bool IsError { get; }
    public bool BadgeRed { get; }
    public string Reason { get; }
    public string Path { get; }
    public string SeverityText { get; }

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
