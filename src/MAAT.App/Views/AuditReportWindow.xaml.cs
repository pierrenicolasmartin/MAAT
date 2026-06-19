// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Windows;
using MAAT.App.ViewModels;

namespace MAAT.App.Views;

/// <summary>
/// Fenêtre de fin d'audit : statistiques de synthèse, éléments non audités
/// (avec motif) et export du journal d'audit.
/// </summary>
public partial class AuditReportWindow : Window
{
    public AuditReportWindow(AuditReportViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}