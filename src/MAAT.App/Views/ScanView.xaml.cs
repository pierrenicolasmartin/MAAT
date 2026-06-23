// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MAAT.App.Services;
using MAAT.App.ViewModels;

namespace MAAT.App.Views;

/// <summary>
/// Explorateur de résultats maître-détail : arbre des dossiers (ou liste d'identités)
/// à gauche, détail (ACL / identité) à droite. La sélection de l'arbre est relayée
/// au ViewModel via l'évènement de sélection (TreeView.SelectedItem est en lecture seule).
/// La largeur du panneau maître est partagée entre les deux vues et mémorisée.
/// </summary>
public partial class ScanView : UserControl
{
    private const double MinPane = 240, MaxPane = 640;

    public ScanView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
        => ApplyPaneWidth(UserSettings.Load().ExplorerPaneWidth);

    private void OnTreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ScanViewModel vm)
        {
            vm.SelectedNode = e.NewValue as TreeNodeViewModel;
        }
    }

    /// <summary>Synchronise les deux vues sur la largeur réglée et mémorise la préférence.</summary>
    private void OnSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        double w = sender is GridSplitter { Parent: Grid g } && g.ColumnDefinitions.Count > 0
            ? g.ColumnDefinitions[0].ActualWidth
            : FoldersPaneColumn.ActualWidth;
        ApplyPaneWidth(w);
        var s = UserSettings.Load();
        s.ExplorerPaneWidth = Math.Clamp(w, MinPane, MaxPane);
        s.Save();
    }

    private void ApplyPaneWidth(double w)
    {
        var len = new GridLength(Math.Clamp(w, MinPane, MaxPane));
        FoldersPaneColumn.Width = len;
        IdentitiesPaneColumn.Width = len;
    }
}
