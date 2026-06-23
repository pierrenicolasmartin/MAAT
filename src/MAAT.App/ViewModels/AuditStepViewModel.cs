// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using MAAT.App.Localization;
using MAAT.Core.Progress;

namespace MAAT.App.ViewModels;

/// <summary>État visuel d'une étape d'audit dans la fenêtre de progression.</summary>
public enum AuditStepState
{
    /// <summary>Pas encore commencée.</summary>
    Pending,

    /// <summary>En cours : affiche sa sous-barre et son compteur.</summary>
    Active,

    /// <summary>Terminée.</summary>
    Done,
}

/// <summary>
/// Une étape de l'audit telle qu'affichée dans la fenêtre de progression. La liste
/// d'étapes est construite à partir des phases réellement émises par le moteur
/// (cf. <c>ScanViewModel.PhaseOrder</c>), donc son nombre varie selon la configuration.
/// </summary>
public sealed class AuditStepViewModel : ObservableObject
{
    private AuditStepState _state;
    private string _countText = string.Empty;
    private double _stepPercent;
    private bool _stepIndeterminate = true;

    public AuditStepViewModel(ScanPhase phase, string label)
    {
        Phase = phase;
        Label = label;
    }

    /// <summary>Phase moteur à laquelle cette étape correspond.</summary>
    public ScanPhase Phase { get; }

    /// <summary>Libellé localisé de l'étape.</summary>
    public string Label { get; }

    public AuditStepState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsDone));
                OnPropertyChanged(nameof(IsPending));
                OnPropertyChanged(nameof(RightStatus));
            }
        }
    }

    public bool IsActive => _state == AuditStepState.Active;
    public bool IsDone => _state == AuditStepState.Done;
    public bool IsPending => _state == AuditStepState.Pending;

    /// <summary>Compteur « traités / total » (étape active déterminée), sinon vide.</summary>
    public string CountText { get => _countText; set => SetProperty(ref _countText, value); }

    /// <summary>Avancement de la sous-barre (0–100), pour l'étape active.</summary>
    public double StepPercent { get => _stepPercent; set => SetProperty(ref _stepPercent, value); }

    /// <summary>Sous-barre indéterminée (phase sans total connu).</summary>
    public bool StepIndeterminate { get => _stepIndeterminate; set => SetProperty(ref _stepIndeterminate, value); }

    /// <summary>Statut affiché à droite pour les étapes terminées / en attente.</summary>
    public string RightStatus => _state switch
    {
        AuditStepState.Done => LocalizationManager.T("Step_Done"),
        AuditStepState.Pending => LocalizationManager.T("Step_Pending"),
        _ => string.Empty,
    };
}
