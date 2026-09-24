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
    {
        ApplyPaneWidth(UserSettings.Load().ExplorerPaneWidth);
        if (Window.GetWindow(this) is { } w)
        {
            w.PreviewKeyDown -= OnWindowKeyDown;
            w.PreviewKeyDown += OnWindowKeyDown;
        }
    }

    /// <summary>Ctrl+F : place le curseur dans la recherche.</summary>
    private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F
            && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control
            && IsVisible)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    /// <summary>Rapport d'analyse de l'audit (ce qui a été couvert / non audité).</summary>
    private void OnReportClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ScanViewModel { IsCompleted: true } vm)
        {
            new AuditReportWindow(vm.BuildReport()) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
    }

    /// <summary>Menu d'export (rapport HTML / CSV) ancré sous le bouton.</summary>
    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (ExportButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = ExportButton;
            menu.Placement = PlacementMode.Bottom;
            menu.VerticalOffset = 4;
            menu.DataContext = DataContext;
            menu.IsOpen = true;
        }
    }

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
