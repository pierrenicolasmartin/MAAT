// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using MAAT.App.Localization;
using MAAT.Storage;

namespace MAAT.App.ViewModels;

/// <summary>
/// Un emplacement de la table de la vue Identités : le chemin où l'identité
/// possède une ACE, avec le type (Autoriser/Refuser coloré), les droits, la
/// portée et l'héritage.
/// </summary>
public sealed class IdentityLocationViewModel
{
    public IdentityLocationViewModel(IdentityLocationRow row)
    {
        FullPath = row.FullPath;
        IsFile = row.IsFile;
        IsDeny = row.AceType == 1;
        RightsFr = row.RightsFr;
        ScopeFr = row.ScopeFr;
        IsInherited = row.IsInherited;
    }

    public string FullPath { get; }
    public bool IsFile { get; }
    public bool IsDeny { get; }
    public string TypeText => LocalizationManager.T(IsDeny ? "Ace_Deny" : "Ace_Allow");
    public string RightsFr { get; }
    public string ScopeFr { get; }
    public bool IsInherited { get; }
    public string InheritedText => LocalizationManager.T(IsInherited ? "Ace_Inherited" : "Ace_Explicit");
}
