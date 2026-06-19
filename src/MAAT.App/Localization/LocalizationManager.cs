// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;

namespace MAAT.App.Localization;

/// <summary>
/// Service de localisation à chaud. Expose un indexeur <c>this[clé]</c> consommé
/// par <see cref="LocExtension"/> ; un changement de langue lève
/// <see cref="PropertyChanged"/> sur l'indexeur, ce qui rafraîchit toutes les
/// liaisons. Évolutif : les catalogues viennent de <see cref="Strings"/>,
/// ajouter une langue = ajouter un catalogue + une valeur d'enum.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    public static LocalizationManager Instance { get; } = new();

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _catalogs = Strings.Catalogs;
    private string _code = "fr";

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Langue effective demandée (Auto, French, English).</summary>
    public AppLanguage Current { get; private set; } = AppLanguage.Auto;

    /// <summary>Code de langue réellement appliqué (« fr », « en »…).</summary>
    public string ActiveCode => _code;

    /// <summary>Traduction d'une clé (repli : anglais, puis la clé brute).</summary>
    public string this[string key]
    {
        get
        {
            if (_catalogs.TryGetValue(_code, out var d) && d.TryGetValue(key, out var v)) { return v; }
            if (_catalogs.TryGetValue("en", out var en) && en.TryGetValue(key, out var e)) { return e; }
            return key;
        }
    }

    /// <summary>Traduction directe (pour le code C# / ViewModels).</summary>
    public static string T(string key) => Instance[key];

    /// <summary>Traduction formatée (<c>string.Format</c>).</summary>
    public static string T(string key, params object[] args) => string.Format(Instance[key], args);

    public void Apply(AppLanguage language)
    {
        Current = language;
        _code = language switch
        {
            AppLanguage.French => "fr",
            AppLanguage.English => "en",
            _ => DetectSystem(),
        };
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
    }

    private static string DetectSystem()
        => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("fr", System.StringComparison.OrdinalIgnoreCase)
            ? "fr"
            : "en";

    public static AppLanguage Parse(string? value) => value switch
    {
        "French" => AppLanguage.French,
        "English" => AppLanguage.English,
        _ => AppLanguage.Auto,
    };
}