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

    /// <summary>Racine auditée : les résultats de recherche s'affichent relativement à elle.</summary>
    public string RootPath { get; init; } = string.Empty;
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
        // « A des enfants ? » est fourni par la requête de l'arbre (EXISTS) : plus de
        // requête COUNT par nœud créé (N+1 coûteux sur les dossiers à large éventail).
        HasChildren = !leaf && !row.IsFile && (row.HasChildren ?? ctx.Repo.CountChildren(row.Id) > 0);
        if (HasChildren)
        {
            Children.Add(LoadingPlaceholder.Instance); // déclenche l'affichage de l'expandeur
        }
    }

    public long Id => _row.Id;
    public string Name => _row.Name;
    public string FullPath => _row.FullPath;

    /// <summary>
    /// Libellé affiché : nom en mode arbre ; en mode recherche, chemin relatif à la racine
    /// (« Partages\Finance\Paie ») — un chemin complet commencerait par le même long préfixe
    /// pour tous les résultats et serait tronqué avant la partie utile.
    /// </summary>
    public string Label => _leaf ? MAAT.App.Services.PathDisplay.Relative(_row.FullPath, _ctx.RootPath) : _row.Name;

    /// <summary>Terme de recherche à surligner dans le libellé (null hors recherche).</summary>
    public string? HighlightTerm { get; }

    public bool HasChildren { get; }
    public ObservableCollection<object> Children { get; }

    /// <summary>Fichier (pictogramme document) ou dossier (pictogramme dossier).</summary>
    public bool IsFile => _row.IsFile;

    /// <summary>États particuliers (ACL illisible, contenu non listable, lien DFS, boucle…).</summary>
    public MAAT.Core.Models.ItemFlags Flags => _row.Flags;

    /// <summary>Audit incomplet pour cet élément : pastille d'avertissement dans l'arbre.</summary>
    public bool HasWarning => (Flags & ItemStateViewModel.WarningFlags) != 0;

    /// <summary>Lien d'espace de noms DFS (étiquette discrète dans l'arbre).</summary>
    public bool IsDfsLink => (Flags & MAAT.Core.Models.ItemFlags.DfsLink) != 0;

    /// <summary>Jonction ou lien symbolique (hors DFS) : audité, cible non parcourue.</summary>
    public bool IsReparseLink => _row.IsReparse && !IsDfsLink;

    /// <summary>Info-bulle résumant les états de l'élément (null si aucun).</summary>
    public string? StateTooltip => ItemStateViewModel.Tooltip(Flags, IsReparseLink);

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
