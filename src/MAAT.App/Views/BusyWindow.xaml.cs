// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MAAT.App.Services;

namespace MAAT.App.Views;

/// <summary>
/// Dialogue modal d'une opération longue (export CSV/HTML, enregistrement de projet),
/// présenté comme une <b>étape</b> qui évolue, à la manière de la fenêtre d'audit :
///  • pendant le travail : anneau <b>orange</b> (accent) + barre indéterminée + bouton « Annuler » ;
///  • à la fin : l'anneau devient une <b>coche verte</b>. Pour un export, la fenêtre reste
///    ouverte et propose « Ouvrir » / « Ignorer » ; pour une sauvegarde, elle se ferme
///    après une courte pause (le temps de voir la coche).
///
/// « Annuler » annule réellement le travail (jeton de coopération) et supprime le
/// fichier partiel produit.
/// </summary>
public partial class BusyWindow : Window
{
    private readonly Action<CancellationToken> _work;
    private readonly string _doneText;
    private readonly string _displayPath;
    private readonly bool _offerOpen;
    private readonly CancellationTokenSource _cts = new();
    private bool _cancelled;
    private Exception? _error;

    private BusyWindow(string title, string message, Action<CancellationToken> work,
                       string doneText, string displayPath, bool offerOpen)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        _work = work;
        _doneText = doneText;
        _displayPath = displayPath;
        _offerOpen = offerOpen;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Task.Run(() => _work(_cts.Token), _cts.Token);
        }
        catch (OperationCanceledException)
        {
            _cancelled = true;
        }
        catch (Exception ex)
        {
            _error = ex; // relancée par Run (gérée par le handler global)
        }

        if (_cancelled)
        {
            TryDeletePartialFile();
            Close();
            return;
        }
        if (_error is not null)
        {
            Close();
            return;
        }

        // Bascule sur l'état « terminé » : anneau → coche verte.
        Ring.Visibility = Visibility.Collapsed;
        DoneDot.Visibility = Visibility.Visible;
        Bar.Visibility = Visibility.Collapsed;
        CancelRow.Visibility = Visibility.Collapsed;
        TitleText.Text = _doneText;
        MessageText.Visibility = Visibility.Collapsed;
        DonePath.Text = PathDisplay.Strip(_displayPath); // sans le préfixe \\?\
        DonePath.Visibility = Visibility.Visible;

        if (_offerOpen)
        {
            DoneButtons.Visibility = Visibility.Visible; // export : attend l'action utilisateur
        }
        else
        {
            await Task.Delay(900); // sauvegarde : laisser voir la coche puis fermer
            Close();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        CancelBtn.IsEnabled = false;
        _cts.Cancel();
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(PathDisplay.Strip(_displayPath)) { UseShellExecute = true });
        }
        catch
        {
            /* fichier introuvable / pas d'application associée : on ferme simplement */
        }
        Close();
    }

    private void OnIgnore(object sender, RoutedEventArgs e) => Close();

    private void TryDeletePartialFile()
    {
        try
        {
            if (File.Exists(_displayPath)) { File.Delete(_displayPath); }
        }
        catch
        {
            /* best-effort : fichier verrouillé nettoyé ultérieurement */
        }
    }

    /// <summary>
    /// Exécute <paramref name="work"/> en arrière-plan derrière un dialogue modal, puis
    /// affiche l'état « terminé ». Renvoie <c>true</c> si le travail s'est achevé, <c>false</c>
    /// s'il a été annulé. Relance toute exception survenue pendant le travail.
    /// </summary>
    public static bool Run(Window? owner, string title, string message, Action<CancellationToken> work,
                           string doneText, string displayPath, bool offerOpen)
    {
        var win = new BusyWindow(title, message, work, doneText, displayPath, offerOpen) { Owner = owner };
        win.ShowDialog();
        if (win._error is not null)
        {
            throw win._error;
        }
        return !win._cancelled;
    }
}
