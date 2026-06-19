// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.ComponentModel;
using System.Windows;
using MAAT.App.ViewModels;
using MAAT.App.Views;

namespace MAAT.App;

/// <summary>Fenêtre principale : menu, espace de travail, ouverture du dialogue de configuration.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.NewAuditRequested += OnNewAuditRequested;
        _vm.AboutRequested += () => new AboutWindow { Owner = this }.ShowDialog();
        _vm.GuideRequested += () => new GuideWindow { Owner = this }.ShowDialog();

        // Fichier .maat double-cliqué (association) : ouverture au démarrage.
        Loaded += (_, _) =>
        {
            if (Application.Current is App { StartupProjectPath: { } path })
            {
                _vm.OpenProjectFile(path);
            }
        };
    }

    private void OnNewAuditRequested()
    {
        // Audit en cours non enregistré → proposer de l'enregistrer avant de le perdre.
        if (!ConfirmDiscardWorkspace())
        {
            return;
        }

        var config = new ConfigViewModel();
        var dialog = new ConfigWindow(config) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            return;
        }

        var scan = _vm.BeginAudit(dialog.Result);
        var progress = new ProgressWindow(scan) { Owner = this };
        progress.ShowDialog();

        // Audit annulé → on revient à l'espace vide (pas de résultats partiels).
        if (scan.IsCancelled)
        {
            _vm.DiscardCurrentScan();
            return;
        }

        // Audit terminé avec succès → rapport d'analyse (stats + éléments ignorés).
        if (scan.IsCompleted)
        {
            new AuditReportWindow(scan.BuildReport()) { Owner = this }.ShowDialog();
        }
    }

    /// <summary>
    /// Si un audit terminé n'est pas enregistré, demande à l'utilisateur s'il
    /// souhaite l'enregistrer. Renvoie false s'il annule l'opération.
    /// </summary>
    private bool ConfirmDiscardWorkspace()
    {
        if (!_vm.NeedsSavePrompt)
        {
            return true;
        }
        var result = MessageBox.Show(
            Localization.LocalizationManager.T("Confirm_Discard"),
            Localization.LocalizationManager.T("Confirm_Title"),
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        switch (result)
        {
            case MessageBoxResult.Cancel:
                return false;
            case MessageBoxResult.Yes:
                _vm.Workspace?.SaveProjectCommand.Execute(null);
                return _vm.Workspace?.IsSaved ?? true; // false si la sauvegarde a été annulée
            default:
                return true; // Non : on abandonne l'audit
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ConfirmDiscardWorkspace())
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        // Purge de la base temporaire si non sauvegardée (confidentialité).
        _vm.DiscardCurrentScan();
        base.OnClosed(e);
    }
}