// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace MAAT.App.Behaviors;

/// <summary>
/// Propriétés attachées qui remplissent les <see cref="TextBlock.Inlines"/> d'un
/// <see cref="TextBlock"/> en surlignant les occurrences de <c>Query</c> dans
/// <c>Text</c> (recherche, façon rapport HTML). Le surlignage suit le thème via
/// les ressources dynamiques <c>AccentTintBrush</c> / <c>AccentBrush</c>.
/// </summary>
public static class TextHighlight
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(TextHighlight), new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached(
        "Query", typeof(string), typeof(TextHighlight), new PropertyMetadata(string.Empty, OnChanged));

    public static void SetText(DependencyObject o, string v) => o.SetValue(TextProperty, v);
    public static string GetText(DependencyObject o) => (string)o.GetValue(TextProperty);
    public static void SetQuery(DependencyObject o, string v) => o.SetValue(QueryProperty, v);
    public static string GetQuery(DependencyObject o) => (string)o.GetValue(QueryProperty);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb)
        {
            return;
        }

        string text = GetText(tb) ?? string.Empty;
        string query = GetQuery(tb) ?? string.Empty;

        tb.Inlines.Clear();

        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text))
        {
            tb.Inlines.Add(new Run(text));
            return;
        }

        int idx = 0;
        while (idx < text.Length)
        {
            int hit = text.IndexOf(query, idx, StringComparison.OrdinalIgnoreCase);
            if (hit < 0)
            {
                tb.Inlines.Add(new Run(text[idx..]));
                break;
            }
            if (hit > idx)
            {
                tb.Inlines.Add(new Run(text[idx..hit]));
            }
            var mark = new Run(text.Substring(hit, query.Length)) { FontWeight = FontWeights.SemiBold };
            mark.SetResourceReference(TextElement.BackgroundProperty, "AccentTintBrush");
            mark.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
            tb.Inlines.Add(mark);
            idx = hit + query.Length;
        }
    }
}