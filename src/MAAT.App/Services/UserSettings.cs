// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System;
using System.IO;
using System.Text.Json;

namespace MAAT.App.Services;

/// <summary>
/// Préférences utilisateur persistées dans <c>%APPDATA%\MAAT\settings.json</c>
/// (niveau utilisateur, indépendant de l'installation). Lecture/écriture
/// tolérantes aux pannes : tout échec retombe sur les valeurs par défaut.
/// </summary>
public sealed class UserSettings
{
    /// <summary>Thème de l'interface : « Auto » (système), « Light » ou « Dark ».</summary>
    public string Theme { get; set; } = "Auto";

    /// <summary>Langue : « Auto » (système), « French » ou « English ».</summary>
    public string Language { get; set; } = "Auto";

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MAAT");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new UserSettings();
            }
        }
        catch
        {
            // Fichier illisible / corrompu : on repart des valeurs par défaut.
        }
        return new UserSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Préférence non enregistrable (droits, disque) : sans gravité.
        }
    }
}