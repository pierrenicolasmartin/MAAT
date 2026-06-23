// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.IO;
using MAAT.Core.Models;
using Microsoft.Win32;

namespace MAAT.App.ViewModels;

/// <summary>
/// Une option de profondeur du menu déroulant. <see cref="EngineDepth"/> est la
/// valeur attendue par le moteur (0 = illimité, 1 = racine seule, 2 = racine + 1
/// sous-niveau…) ; le libellé suit la nomenclature utilisateur (0 = racine seule).
/// </summary>
public sealed class DepthOption
{
    public required string Display { get; init; }
    public required int EngineDepth { get; init; }

    public override string ToString() => Display;
}

/// <summary>
/// Assistant de configuration d'un audit : chemin, profondeur (liste déroulante),
/// dossiers/fichiers et contenu. L'export se fait après l'audit (menu Fichier).
/// </summary>
public sealed class ConfigViewModel : ObservableObject
{
    private const int MaxDepthLevels = 15;

    private string _rootPath = string.Empty;
    private string? _pathError;
    private AuditScope _scope = AuditScope.FoldersOnly;
    private AuditContent _content = AuditContent.RightsOnly;
    private bool _includeLocalAccounts = false;
    private DepthOption _selectedDepth;

    public ConfigViewModel()
    {
        DepthOptions = BuildDepthOptions();
        _selectedDepth = DepthOptions[0]; // « Illimité » en premier
        BrowseCommand = new RelayCommand(Browse);
        StartCommand = new RelayCommand(Start, () => CanStart);
    }

    /// <summary>Déclenché quand l'utilisateur lance un audit valide.</summary>
    public event Action<AuditParameters>? StartRequested;

    public string RootPath
    {
        get => _rootPath;
        set { if (SetProperty(ref _rootPath, value)) { Validate(); StartCommand.RaiseCanExecuteChanged(); } }
    }

    /// <summary>Options de profondeur (« Illimité », puis 0, 1, 2…).</summary>
    public IReadOnlyList<DepthOption> DepthOptions { get; }

    public DepthOption SelectedDepth
    {
        get => _selectedDepth;
        set => SetProperty(ref _selectedDepth, value ?? DepthOptions[0]);
    }

    public string? PathError
    {
        get => _pathError;
        private set { SetProperty(ref _pathError, value); OnPropertyChanged(nameof(HasPathError)); }
    }

    public bool HasPathError => !string.IsNullOrEmpty(_pathError);

    public AuditScope Scope
    {
        get => _scope;
        set => SetProperty(ref _scope, value);
    }

    public AuditContent Content
    {
        get => _content;
        set
        {
            if (SetProperty(ref _content, value))
            {
                OnPropertyChanged(nameof(AuditsRights));
            }
        }
    }

    /// <summary>Vrai si le contenu choisi inclut les droits NTFS (active l'étendue des identités).</summary>
    public bool AuditsRights => _content is AuditContent.RightsOnly or AuditContent.RightsAndSize;

    /// <summary>
    /// Inclure les comptes/groupes locaux et intégrés dans l'audit des droits.
    /// Si faux, seules les identités du domaine Active Directory sont auditées.
    /// </summary>
    public bool IncludeLocalAccounts
    {
        get => _includeLocalAccounts;
        set => SetProperty(ref _includeLocalAccounts, value);
    }

    public bool CanStart => !string.IsNullOrWhiteSpace(_rootPath) && !HasPathError;

    public RelayCommand BrowseCommand { get; }
    public RelayCommand StartCommand { get; }

    private static List<DepthOption> BuildDepthOptions()
    {
        var list = new List<DepthOption>(MaxDepthLevels + 2)
        {
            new() { Display = Localization.LocalizationManager.T("Depth_Unlimited"), EngineDepth = 0 },
            new() { Display = Localization.LocalizationManager.T("Depth_RootOnly"), EngineDepth = 1 },
        };
        for (int level = 1; level <= MaxDepthLevels; level++)
        {
            string key = level > 1 ? "Depth_LevelN" : "Depth_Level1";
            list.Add(new DepthOption
            {
                Display = Localization.LocalizationManager.T(key, level),
                EngineDepth = level + 1,
            });
        }
        return list;
    }

    private void Browse()
    {
        var dialog = new OpenFolderDialog { Title = Localization.LocalizationManager.T("Dlg_PickFolder") };
        if (dialog.ShowDialog() == true)
        {
            RootPath = dialog.FolderName;
        }
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(_rootPath))
        {
            PathError = null;
            return;
        }
        try
        {
            PathError = Directory.Exists(_rootPath)
                ? null
                : Localization.LocalizationManager.T("Cfg_PathError");
        }
        catch (Exception ex)
        {
            PathError = Localization.LocalizationManager.T("Cfg_PathInvalid", ex.Message);
        }
    }

    private void Start()
    {
        if (!CanStart) { return; }
        var parameters = new AuditParameters
        {
            RootPath = _rootPath.TrimEnd('\\').Length == 2 && _rootPath.EndsWith(':')
                ? _rootPath + '\\' // « C: » → « C:\ »
                : _rootPath,
            Depth = _selectedDepth.EngineDepth,
            Scope = _scope,
            Content = _content,
            IncludeLocalAccounts = _includeLocalAccounts,
            Lang = Localization.LocalizationManager.Instance.ActiveCode,
        };
        StartRequested?.Invoke(parameters);
    }
}