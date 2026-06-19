// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System;
using System.Threading.Tasks;
using System.Windows;

namespace MAAT.App.Views;

/// <summary>
/// Petit dialogue modal d'attente (barre indéterminée) pour les opérations
/// longues — exports CSV/HTML, enregistrement — sur de gros volumes. Le travail
/// s'exécute en arrière-plan ; la fenêtre se ferme automatiquement à la fin.
/// </summary>
public partial class BusyWindow : Window
{
    private readonly Action _work;
    private Exception? _error;

    private BusyWindow(string title, string message, Action work)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        _work = work;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Task.Run(_work);
        }
        catch (Exception ex)
        {
            _error = ex;
        }
        finally
        {
            Close();
        }
    }

    /// <summary>
    /// Exécute <paramref name="work"/> en arrière-plan derrière un dialogue modal
    /// d'attente. Bloque jusqu'à la fin et relance toute exception survenue.
    /// </summary>
    public static void Run(Window? owner, string title, string message, Action work)
    {
        var win = new BusyWindow(title, message, work) { Owner = owner };
        win.ShowDialog();
        if (win._error is not null)
        {
            throw win._error;
        }
    }
}