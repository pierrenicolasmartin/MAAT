// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Globalization;

namespace MAAT.Core.Localization;

/// <summary>
/// Formate une taille en octets vers une unité lisible, dans la langue demandée :
/// français « o / Ko / Mo / Go / To » (virgule décimale) ou anglais
/// « B / KB / MB / GB / TB » (point décimal). Deux décimales.
/// Portage de <c>Format-SizeFR</c> (V1.0.5).
/// </summary>
public static class SizeFormatter
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    private const double Kb = 1024d;
    private const double Mb = 1024d * 1024d;
    private const double Gb = 1024d * 1024d * 1024d;
    private const double Tb = 1024d * 1024d * 1024d * 1024d;

    private static bool IsFr(string lang) => !"en".Equals(lang, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Formate un nombre d'octets (ex. FR « 12,34 Mo », EN « 12.34 MB »).</summary>
    public static string Format(long bytes, string lang = "fr")
    {
        bool fr = IsFr(lang);
        var c = fr ? Fr : En;
        // Unités : FR octet (o), EN byte (B).
        string u(string frU, string enU) => fr ? frU : enU;

        if (bytes >= 1000 * Gb) { return string.Format(c, "{0:N2} " + u("To", "TB"), bytes / Tb); }
        if (bytes >= Gb) { return string.Format(c, "{0:N2} " + u("Go", "GB"), bytes / Gb); }
        if (bytes >= Mb) { return string.Format(c, "{0:N2} " + u("Mo", "MB"), bytes / Mb); }
        return string.Format(c, "{0:N2} " + u("Ko", "KB"), bytes / Kb);
    }

    /// <summary>
    /// Comme <see cref="Format(long, string)"/> mais préfixe « ≈ » si la taille est
    /// partielle, et rend une chaîne vide si la valeur est inconnue.
    /// </summary>
    public static string Format(long? bytes, bool partial, string lang = "fr")
    {
        if (bytes is null)
        {
            return string.Empty;
        }
        string formatted = Format(bytes.Value, lang);
        return partial ? "≈ " + formatted : formatted;
    }
}