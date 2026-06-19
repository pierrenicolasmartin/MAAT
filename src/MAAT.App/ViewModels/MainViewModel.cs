// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Reflection;
using System.Windows;
using MAAT.App.Localization;
using MAAT.App.Services;
using MAAT.Core.Models;
using MAAT.Storage;
using Microsoft.Win32;

namespace MAAT.App.ViewModels;

/// <summary>
/// ViewModel racine : barre de menu (Fichier / Préférences / Aide), espace de
/// travail (vide au démarrage), thème. Le scan et les résultats vivent dans le
/// <see cref="Workspace"/> ; la configuration d'un nouvel audit se fait via un
/// dialogue (ouvert par la vue sur l'évènement <see cref="NewAuditRequested"/>).
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private ScanViewModel? _workspace;
    private string _themePref = "Auto"; // « Auto » | « Light » | « Dark »
    private AppLanguage _language = AppLanguage.Auto;

    public MainViewModel()
    {
        NewAuditCommand = new RelayCommand(() => NewAuditRequested?.Invoke());
        OpenProjectCommand = new RelayCommand(OpenProject);
        ThemeAutoCommand = new RelayCommand(() => SetThemePref("Auto"));
        ThemeLightCommand = new RelayCommand(() => SetThemePref("Light"));
        ThemeDarkCommand = new RelayCommand(() => SetThemePref("Dark"));
        AboutCommand = new RelayCommand(() => AboutRequested?.Invoke());
        GuideCommand = new RelayCommand(() => GuideRequested?.Invoke());
        ExitCommand = new RelayCommand(() => Application.Current.Shutdown());
        LanguageAutoCommand = new RelayCommand(() => SetLanguage(AppLanguage.Auto));
        LanguageFrenchCommand = new RelayCommand(() => SetLanguage(AppLanguage.French));
        LanguageEnglishCommand = new RelayCommand(() => SetLanguage(AppLanguage.English));

        // Réapplique les préférences utilisateur (thème + langue).
        var settings = UserSettings.Load();
        _themePref = settings.Theme is "Light" or "Dark" ? settings.Theme : "Auto";
        ThemeManager.ApplyPreference(_themePref);

        _language = LocalizationManager.Parse(settings.Language);
        LocalizationManager.Instance.Apply(_language);
    }

    /// <summary>Demande d'ouverture du dialogue de configuration (gérée par la fenêtre).</summary>
    public event Action? NewAuditRequested;

    /// <summary>Demande d'ouverture de la fenêtre « Guide » (gérée par la fenêtre).</summary>
    public event Action? GuideRequested;

    /// <summary>Demande d'ouverture de la fenêtre « À propos » (gérée par la fenêtre).</summary>
    public event Action? AboutRequested;

    public ScanViewModel? Workspace
    {
        get => _workspace;
        private set
        {
            var previous = _workspace;
            if (SetProperty(ref _workspace, value))
            {
                if (previous is not null) { previous.PropertyChanged -= OnWorkspacePropertyChanged; }
                if (_workspace is not null) { _workspace.PropertyChanged += OnWorkspacePropertyChanged; }
                OnPropertyChanged(nameof(HasWorkspace));
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(HasResults));
            }
        }
    }

    public bool HasWorkspace => _workspace is not null;
    public bool ShowEmptyState => _workspace is null;

    /// <summary>Vrai quand un audit terminé (ou projet chargé) est disponible : active Export/Enregistrer.</summary>
    public bool HasResults => _workspace is { IsCompleted: true };

    /// <summary>
    /// Vrai si l'espace courant contient un audit terminé non encore enregistré
    /// (base temporaire) : il faut alors proposer de l'enregistrer avant de le perdre.
    /// </summary>
    public bool NeedsSavePrompt =>
        _workspace is { IsCompleted: true, IsSaved: false }
        && (_workspace.Database?.IsTemporary ?? false);

    private void OnWorkspacePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScanViewModel.IsCompleted))
        {
            OnPropertyChanged(nameof(HasResults));
        }
    }

    public bool IsThemeAuto => _themePref == "Auto";
    public bool IsThemeLight => _themePref == "Light";
    public bool IsThemeDark => _themePref == "Dark";

    public RelayCommand NewAuditCommand { get; }
    public RelayCommand OpenProjectCommand { get; }
    public RelayCommand ThemeAutoCommand { get; }
    public RelayCommand ThemeLightCommand { get; }
    public RelayCommand ThemeDarkCommand { get; }
    public RelayCommand AboutCommand { get; }
    public RelayCommand GuideCommand { get; }
    public RelayCommand ExitCommand { get; }

    /// <summary>
    /// Prépare un nouvel espace de travail pour l'audit et le renvoie. Le scan
    /// lui-même est lancé par la fenêtre de progression (« Audit en cours »).
    /// </summary>
    public ScanViewModel BeginAudit(AuditParameters parameters)
    {
        DiscardCurrentScan();
        var scan = new ScanViewModel(parameters);
        Workspace = scan;
        return scan;
    }

    private void OpenProject()
    {
        var dialog = new OpenFileDialog
        {
            Title = Localization.LocalizationManager.T("Dlg_OpenProject"),
            Filter = Localization.LocalizationManager.T("Filter_MaatOpen"),
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        OpenProjectFile(dialog.FileName);
    }

    /// <summary>Ouvre un fichier projet .maat (menu ou argument de ligne de commande).</summary>
    public void OpenProjectFile(string path)
    {
        try
        {
            var db = AuditDatabase.Open(path);
            DiscardCurrentScan();
            Workspace = ScanViewModel.LoadProject(db);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible d'ouvrir le projet :\n{ex.Message}",
                "MAAT", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetThemePref(string pref)
    {
        _themePref = pref;
        ThemeManager.ApplyPreference(pref);
        OnPropertyChanged(nameof(IsThemeAuto));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));
        var s = UserSettings.Load();
        s.Theme = pref;
        s.Save();
    }

    public RelayCommand LanguageAutoCommand { get; }
    public RelayCommand LanguageFrenchCommand { get; }
    public RelayCommand LanguageEnglishCommand { get; }

    public bool IsLangAuto => _language == AppLanguage.Auto;
    public bool IsLangFrench => _language == AppLanguage.French;
    public bool IsLangEnglish => _language == AppLanguage.English;

    private void SetLanguage(AppLanguage language)
    {
        _language = language;
        LocalizationManager.Instance.Apply(language);
        OnPropertyChanged(nameof(IsLangAuto));
        OnPropertyChanged(nameof(IsLangFrench));
        OnPropertyChanged(nameof(IsLangEnglish));
        var s = UserSettings.Load();
        s.Language = language.ToString();
        s.Save();
    }

    /// <summary>Libère l'espace courant (supprime la base si temporaire/non sauvegardée).</summary>
    public void DiscardCurrentScan()
    {
        if (_workspace is null)
        {
            return;
        }
        var db = _workspace.Database;
        _workspace.Dispose();
        if (db is not null)
        {
            ProjectService.DiscardIfTemporary(db);
        }
        Workspace = null;
    }
}