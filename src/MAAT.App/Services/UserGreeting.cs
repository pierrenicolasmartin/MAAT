// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using MAAT.App.Localization;

namespace MAAT.App.Services;

/// <summary>
/// Construit le message d'accueil de l'écran de démarrage : une salutation adaptée
/// au moment de la journée (sans nom d'utilisateur).
/// </summary>
internal static class UserGreeting
{
    /// <summary>« Bonjour, », « Bon après-midi, », « Bonsoir, » ou « Bonne nuit, » selon l'heure.</summary>
    public static string Build() => LocalizationManager.T(SalutationKey()) + ",";

    private static string SalutationKey()
    {
        int h = DateTime.Now.Hour;
        return h switch
        {
            >= 5 and < 12 => "Greet_Morning",    // matin
            >= 12 and < 18 => "Greet_Afternoon", // après-midi
            >= 18 and < 22 => "Greet_Evening",   // soir
            _ => "Greet_Night",                   // nuit
        };
    }
}
