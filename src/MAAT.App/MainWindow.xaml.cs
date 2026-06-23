// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MAAT.App.ViewModels;
using MAAT.App.Views;

namespace MAAT.App;

/// <summary>Fenêtre principale : menu, espace de travail, ouverture du dialogue de configuration.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private bool _greetingStarted; // l'intro d'accueil ne se joue qu'une fois, au lancement
    private bool _greetingDone;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.NewAuditRequested += OnNewAuditRequested;
        _vm.AboutRequested += () => new AboutWindow { Owner = this }.ShowDialog();
        _vm.GuideRequested += () => new GuideWindow { Owner = this }.ShowDialog();
        _vm.PropertyChanged += OnVmPropertyChanged;

        // Fichier .maat double-cliqué (association) : ouverture au démarrage.
        Loaded += (_, _) =>
        {
            if (Application.Current is App { StartupProjectPath: { } path })
            {
                _vm.OpenProjectFile(path);
            }
        };
    }

    private void OnNewAuditRequested()
    {
        // Audit en cours non enregistré → proposer de l'enregistrer avant de le perdre.
        if (!ConfirmDiscardWorkspace())
        {
            return;
        }

        var config = new ConfigViewModel();
        var dialog = new ConfigWindow(config) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            return;
        }

        var scan = _vm.BeginAudit(dialog.Result);
        var progress = new ProgressWindow(scan) { Owner = this };
        progress.ShowDialog();

        // Audit annulé → on revient à l'espace vide (pas de résultats partiels).
        if (scan.IsCancelled)
        {
            _vm.DiscardCurrentScan();
            return;
        }

        // Audit terminé avec succès → rapport d'analyse (stats + éléments ignorés).
        if (scan.IsCompleted)
        {
            new AuditReportWindow(scan.BuildReport()) { Owner = this }.ShowDialog();
        }
    }

    /// <summary>
    /// Si un audit terminé n'est pas enregistré, demande à l'utilisateur s'il
    /// souhaite l'enregistrer. Renvoie false s'il annule l'opération.
    /// </summary>
    private bool ConfirmDiscardWorkspace()
    {
        if (!_vm.NeedsSavePrompt)
        {
            return true;
        }
        var result = MessageBox.Show(
            Localization.LocalizationManager.T("Confirm_Discard"),
            Localization.LocalizationManager.T("Confirm_Title"),
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        switch (result)
        {
            case MessageBoxResult.Cancel:
                return false;
            case MessageBoxResult.Yes:
                _vm.Workspace?.SaveProjectCommand.Execute(null);
                return _vm.Workspace?.IsSaved ?? true; // false si la sauvegarde a été annulée
            default:
                return true; // Non : on abandonne l'audit
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ConfirmDiscardWorkspace())
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        // Purge de la base temporaire si non sauvegardée (confidentialité).
        _vm.DiscardCurrentScan();
        base.OnClosed(e);
    }

    // ───────── Animation d'accueil (curseur clignotant → frappe → virgule) ─────────

    /// <summary>Salutation sans la virgule finale (la virgule est dessinée par le morph).</summary>
    private string GreetingWord()
    {
        string g = (_vm.Greeting ?? string.Empty).TrimEnd();
        if (g.EndsWith(",", StringComparison.Ordinal)) { g = g[..^1].TrimEnd(); }
        return g;
    }

    private async void OnGreetingLoaded(object sender, RoutedEventArgs e)
    {
        if (_greetingStarted) { return; } // une seule fois, au démarrage
        _greetingStarted = true;
        await PlayGreetingIntroAsync();
    }

    private async Task PlayGreetingIntroAsync()
    {
        GreetingText.Text = string.Empty;

        // 1) Curseur clignotant (3 cycles ~0,5 s) avant que quoi que ce soit ne s'écrive.
        await BlinkCursorAsync(3);
        GreetingCursor.BeginAnimation(UIElement.OpacityProperty, null);
        GreetingCursor.Opacity = 1;

        // 2) Frappe lettre par lettre, rythme volontairement irrégulier.
        var rnd = new Random();
        foreach (char c in GreetingWord())
        {
            GreetingText.Text += c;
            int delay = rnd.Next(90, 221);                          // base 90–220 ms
            if (rnd.Next(3) == 0) { delay += rnd.Next(130, 401); }  // ~1/3 : hésitation +130–400 ms
            await Task.Delay(delay);
        }

        // 3) Courte pause, puis métamorphose du curseur en virgule.
        await Task.Delay(480);
        await MorphCursorToCommaAsync();

        // 4) Révélation de la tagline en fondu.
        GreetingTagline.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromSeconds(0.5))));

        _greetingDone = true;
    }

    /// <summary>
    /// Fait clignoter le curseur un nombre fixe de cycles. Le clignotement est
    /// <b>instantané</b> (allumé/éteint sec, comme un vrai caret) grâce à des images
    /// clés discrètes ; seule sa disparition finale (au morph) se fait en fondu.
    /// </summary>
    private Task BlinkCursorAsync(int cycles)
    {
        var tcs = new TaskCompletionSource<bool>();
        var blink = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromSeconds(0.8)),
            RepeatBehavior = new RepeatBehavior(cycles),
        };
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.4))));
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.8))));
        blink.Completed += (_, _) => tcs.TrySetResult(true);
        GreetingCursor.BeginAnimation(UIElement.OpacityProperty, blink);
        return tcs.Task;
    }

    /// <summary>
    /// Storyboard combiné : le curseur pivote, descend et disparaît, tandis que la
    /// virgule apparaît en fondu + zoom élastique (BackEase) à la même place.
    /// </summary>
    private Task MorphCursorToCommaAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        var dur = new Duration(TimeSpan.FromSeconds(0.34));
        var sb = new Storyboard();

        void Add(AnimationTimeline a, DependencyObject target, DependencyProperty prop)
        {
            Storyboard.SetTarget(a, target);
            Storyboard.SetTargetProperty(a, new PropertyPath(prop));
            sb.Children.Add(a);
        }

        // Curseur : rotation ~22°, descente, fondu en sortie.
        Add(new DoubleAnimation(0, 22, dur), GreetingCursorRotate, RotateTransform.AngleProperty);
        Add(new DoubleAnimation(0, 6, dur), GreetingCursorTranslate, TranslateTransform.YProperty);
        Add(new DoubleAnimation(1, 0, new Duration(TimeSpan.FromSeconds(0.22))), GreetingCursor, UIElement.OpacityProperty);

        // Virgule : fondu + zoom 0,5 → 1 avec rebond élastique.
        var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
        var begin = TimeSpan.FromSeconds(0.08);
        Add(new DoubleAnimation(0, 1, dur) { BeginTime = begin }, GreetingComma, UIElement.OpacityProperty);
        Add(new DoubleAnimation(0.5, 1, dur) { BeginTime = begin, EasingFunction = back }, GreetingCommaScale, ScaleTransform.ScaleXProperty);
        Add(new DoubleAnimation(0.5, 1, dur) { BeginTime = begin, EasingFunction = back }, GreetingCommaScale, ScaleTransform.ScaleYProperty);

        sb.Completed += (_, _) => tcs.TrySetResult(true);
        sb.Begin();
        return tcs.Task;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Après l'intro, garder la salutation synchrone avec la langue (sans rejouer l'anim).
        if (_greetingDone && e.PropertyName == nameof(MainViewModel.Greeting))
        {
            GreetingText.Text = GreetingWord();
        }
    }
}