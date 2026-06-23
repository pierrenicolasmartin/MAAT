// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Collections.ObjectModel;
using MAAT.App.Localization;
using MAAT.Core.Localization;
using MAAT.Storage;

namespace MAAT.App.ViewModels;

/// <summary>Contexte partagé par les nœuds de l'arbre (accès en lecture à la base).</summary>
public sealed class TreeContext
{
    public required AuditReadRepository Repo { get; init; }
}

/// <summary>Sentinelle d'enfant non chargé (affichée « Chargement… » avant dépliage).</summary>
public sealed class LoadingPlaceholder
{
    public static readonly LoadingPlaceholder Instance = new();
    private LoadingPlaceholder() { }
}

/// <summary>
/// Nœud de l'arbre de résultats : un dossier (ou un résultat de recherche à plat),
/// dont les enfants sont chargés paresseusement au dépliage. Sélectionner un nœud
/// affiche ses droits dans le volet de détail (voir <see cref="ScanViewModel"/>).
/// </summary>
public sealed class TreeNodeViewModel : ObservableObject, IDisposable
{
    private readonly TreeContext _ctx;
    private readonly FsItemRow _row;
    private readonly bool _leaf;
    private bool _isExpanded;
    private bool _childrenLoaded;

    /// <param name="leaf">Vrai pour un résultat de recherche à plat (sans enfants, libellé = chemin complet).</param>
    /// <param name="highlight">Terme à mettre en surbrillance dans le libellé (recherche), ou null.</param>
    public TreeNodeViewModel(FsItemRow row, TreeContext ctx, bool leaf = false, string? highlight = null)
    {
        _row = row;
        _ctx = ctx;
        _leaf = leaf;
        HighlightTerm = highlight;
        Children = new ObservableCollection<object>();
        HasChildren = !leaf && !row.IsFile && ctx.Repo.CountChildren(row.Id) > 0;
        if (HasChildren)
        {
            Children.Add(LoadingPlaceholder.Instance); // déclenche l'affichage de l'expandeur
        }
    }

    public long Id => _row.Id;
    public string Name => _row.Name;
    public string FullPath => _row.FullPath;

    /// <summary>Libellé affiché : nom en mode arbre, chemin complet en mode recherche.</summary>
    public string Label => _leaf ? _row.FullPath : _row.Name;

    /// <summary>Terme de recherche à surligner dans le libellé (null hors recherche).</summary>
    public string? HighlightTerm { get; }

    public bool HasChildren { get; }
    public ObservableCollection<object> Children { get; }

    /// <summary>Taille formatée (vide si non calculée), préfixe ≈ si partielle.</summary>
    public string SizeText => SizeFormatter.Format(_row.SizeBytes, _row.SizePartial, LocalizationManager.Instance.ActiveCode);

    public bool HasSize => _row.SizeBytes is not null;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value) && value)
            {
                LoadChildren();
            }
        }
    }

    private void LoadChildren()
    {
        if (_childrenLoaded)
        {
            return;
        }
        _childrenLoaded = true;
        Children.Clear();
        foreach (var child in _ctx.Repo.GetChildren(_row.Id))
        {
            Children.Add(new TreeNodeViewModel(child, _ctx));
        }
    }

    /// <summary>Libère récursivement les enfants chargés.</summary>
    public void Dispose()
    {
        foreach (var child in Children)
        {
            if (child is TreeNodeViewModel node)
            {
                node.Dispose();
            }
        }
    }
}
