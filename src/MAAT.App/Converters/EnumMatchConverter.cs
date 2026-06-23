// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Globalization;
using System.Windows.Data;

namespace MAAT.App.Converters;

/// <summary>
/// Lie un <see cref="System.Windows.Controls.RadioButton"/> à une propriété enum :
/// <c>IsChecked</c> est vrai si la valeur égale le paramètre (nom de la valeur enum).
/// </summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && parameter is string p
           && string.Equals(value.ToString(), p, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string p && targetType.IsEnum)
        {
            return Enum.Parse(targetType, p);
        }
        return Binding.DoNothing;
    }
}