// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace MAAT.App.Views;

/// <summary>Dialogue « À propos » : version, auteur et année lus depuis l'assembly.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var asm = Assembly.GetExecutingAssembly();
        string version = asm.GetName().Version?.ToString(3) ?? "1.1.0";
        VersionText.Text = Localization.LocalizationManager.T("About_Version", version);

        // Copyright (« © <année> <auteur> ») fourni par l'attribut d'assembly,
        // alimenté depuis le .csproj — repli sur la société + l'année si absent.
        string? copyright = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;
        if (string.IsNullOrWhiteSpace(copyright))
        {
            string author = asm.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "MAAT";
            copyright = $"© {System.DateTime.Now.Year} {author}";
        }
        CopyrightText.Text = copyright;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        // Ouvre le lien dans le navigateur par défaut.
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
