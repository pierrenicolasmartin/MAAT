// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

namespace MAAT.App.Localization;

/// <summary>
/// Langues sélectionnables. <see cref="Auto"/> suit la langue du système.
/// Évolutif : ajouter une valeur ici puis un catalogue dans <see cref="Strings"/>.
/// </summary>
public enum AppLanguage
{
    Auto,
    French,
    English,
}