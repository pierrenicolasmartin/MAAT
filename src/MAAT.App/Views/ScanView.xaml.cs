// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Windows;
using System.Windows.Controls;
using MAAT.App.ViewModels;

namespace MAAT.App.Views;

public partial class ScanView : UserControl
{
    public ScanView()
    {
        InitializeComponent();
    }

    private void OnIdentitiesClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ScanViewModel vm)
        {
            return;
        }
        var window = new IdentitiesWindow(vm.LoadIdentities())
        {
            Owner = Window.GetWindow(this),
        };
        window.ShowDialog();
    }
}