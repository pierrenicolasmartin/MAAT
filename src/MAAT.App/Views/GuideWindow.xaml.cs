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
/// Fenêtre d'aide en deux panneaux : sommaire à gauche, sujet sélectionné à droite,
/// navigation précédent/suivant en pied. Le contenu est piloté par <see cref="GuideViewModel"/>.
/// </summary>
public partial class GuideWindow : Window
{
    private readonly GuideViewModel _vm = new();

    public GuideWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        // À chaque changement de sujet, ramener le détail en haut.
        _vm.SelectionChanged += () => DetailScroll.ScrollToTop();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
