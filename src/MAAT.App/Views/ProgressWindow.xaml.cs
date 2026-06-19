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

namespace MAAT.App.Views;

/// <summary>Dialogue modal de progression d'un audit. Lance le scan puis se ferme à la fin.</summary>
public partial class ProgressWindow : Window
{
    private readonly ScanViewModel _scan;
    private bool _done;

    public ProgressWindow(ScanViewModel scan)
    {
        InitializeComponent();
        _scan = scan;
        DataContext = scan;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _scan.StartAsync();
        _done = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Fermeture via la croix pendant le scan → on annule et on attend la fin.
        if (!_done && _scan.IsRunning)
        {
            _scan.CancelCommand.Execute(null);
            e.Cancel = true;
        }
    }
}