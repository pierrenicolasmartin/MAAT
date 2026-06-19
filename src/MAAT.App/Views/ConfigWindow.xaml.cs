// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Windows;
using MAAT.App.Models;
using MAAT.App.ViewModels;
using MAAT.Core.Models;

namespace MAAT.App.Views;

/// <summary>Dialogue de configuration d'un audit (Fichier → Nouvel audit).</summary>
public partial class ConfigWindow : Window
{
    public ConfigWindow(ConfigViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.StartRequested += OnStart;
    }

    /// <summary>Paramètres validés (null si annulé).</summary>
    public AuditParameters? Result { get; private set; }

    private void OnStart(AuditParameters parameters)
    {
        Result = parameters;
        DialogResult = true; // ferme le dialogue
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}