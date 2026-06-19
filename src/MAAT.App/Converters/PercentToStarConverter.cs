// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MAAT.App.Converters;

/// <summary>
/// Convertit une valeur de pourcentage (0–100) en <see cref="GridLength"/> étoilée,
/// pour piloter le remplissage d'une barre de progression par colonnes (méthode
/// auto-suffisante, indépendante des parties internes de <c>ProgressBar</c>).
/// Paramètre « fill » → part remplie ; « rest » → part restante.
/// </summary>
public sealed class PercentToStarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double v = value is double d ? d : 0;
        v = Math.Clamp(v, 0, 100);
        bool fill = parameter as string == "fill";
        return new GridLength(fill ? v : 100 - v, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}