// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Collections.ObjectModel;
using System.IO;
using MAAT.App.Localization;
using MAAT.Core.Localization;
using MAAT.Storage;

namespace MAAT.App.ViewModels;

/// <summary>Contexte partagé par les nœuds de l'arbre (lecture, filtre, mode droits).</summary>
public sealed class TreeContext
{
    public required AuditReadRepository Repo { get; init; }
    public required AclFilterState Filter { get; init; }
    public bool RightsAudited { get; init; }
}

/// <summary>Sentinelle d'enfant non chargé (affichée « Chargement… » avant dépliage).</summary>
public sealed class LoadingPlaceholder
{
    public static readonly LoadingPlaceholder Instance = new();
    private LoadingPlaceholder() { }
}

/// <summary>
/// Nœud de l'arbre de résultats. L'arbre EST le résultat : déplier la flèche
/// montre les enfants (chargés paresseusement) ; cliquer le nom affiche/masque
/// les droits ACL en ligne sous l'élément (façon rapport HTML).
/// </summary>
public sealed class TreeNodeViewModel : ObservableObject, IDisposable
{
    private readonly TreeContext _ctx;
    private readonly FsItemRow _row;
    private readonly bool _leaf;
    private bool _isExpanded;
    private bool _childrenLoaded;
    private bool _aclExpanded;
    private bool _acesLoaded;

    /// <param name="leaf">Vrai pour un résultat de recherche à plat (sans enfants, libellé = chemin complet).</param>
    /// <param name="highlight">Terme à mettre en surbrillance dans le libellé (recherche), ou null.</param>
    public TreeNodeViewModel(FsItemRow row, TreeContext ctx, bool leaf = false, string? highlight = null)
    {
        _row = row;
        _ctx = ctx;
        _leaf = leaf;
        HighlightTerm = highlight;
        Children = new ObservableCollection<object>();
        AclLines = new ObservableCollection<AceLineViewModel>();
        HasChildren = !leaf && !row.IsFile && ctx.Repo.CountChildren(row.Id) > 0;
        if (HasChildren)
        {
            Children.Add(LoadingPlaceholder.Instance); // déclenche l'affichage de l'expandeur
        }
        ToggleAclCommand = new RelayCommand(ToggleAcl, () => CanShowAcl);
    }

    public long Id => _row.Id;
    public string Name => _row.Name;
    public string FullPath => _row.FullPath;

    /// <summary>Libellé affiché : nom en mode arbre, chemin complet en mode recherche.</summary>
    public string Label => _leaf ? _row.FullPath : _row.Name;

    /// <summary>Terme de recherche à surligner dans le libellé (null hors recherche).</summary>
    public string? HighlightTerm { get; }

    public bool IsFile => _row.IsFile;
    public bool IsReparse => _row.IsReparse;
    public bool HasChildren { get; }

    public ObservableCollection<object> Children { get; }

    /// <summary>Lignes ACL affichées sous l'élément (chargées au premier clic sur le nom).</summary>
    public ObservableCollection<AceLineViewModel> AclLines { get; }

    public string Icon => IsFile ? "📄" : (_row.ParentId is null ? "📁" : "📂");

    // ───── Capsule d'extension (fichiers) : couleur par famille ─────

    /// <summary>Extension sans point, en capitales (ex. « PDF »), vide pour un dossier.</summary>
    public string ExtensionText
    {
        get
        {
            if (!IsFile) { return string.Empty; }
            string ext = Path.GetExtension(_row.Name);
            return ext.Length > 1 ? ext[1..].ToUpperInvariant() : string.Empty;
        }
    }

    public bool HasExtension => ExtensionText.Length > 0;

    /// <summary>Couleur (hex) de la famille d'extension, pour la capsule.</summary>
    public string CapsuleColor => ExtensionCategories.ColorFor(ExtensionText);

    /// <summary>Taille formatée (vide si non calculée), préfixe ≈ si partielle.</summary>
    public string SizeText => SizeFormatter.Format(_row.SizeBytes, _row.SizePartial, LocalizationManager.Instance.ActiveCode);

    public bool HasSize => _row.SizeBytes is not null;

    /// <summary>Vrai si l'élément peut afficher des droits (audit des droits actif).</summary>
    public bool CanShowAcl => _ctx.RightsAudited;

    public RelayCommand ToggleAclCommand { get; }

    /// <summary>Vrai quand le panneau ACL est déplié sous l'élément.</summary>
    public bool IsAclExpanded
    {
        get => _aclExpanded;
        private set => SetProperty(ref _aclExpanded, value);
    }

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

    private void ToggleAcl()
    {
        if (!CanShowAcl)
        {
            return;
        }
        if (!_acesLoaded)
        {
            _acesLoaded = true;
            foreach (var ace in _ctx.Repo.GetAces(_row.Id))
            {
                AclLines.Add(new AceLineViewModel(ace, _ctx.Filter));
            }
        }
        IsAclExpanded = !IsAclExpanded;
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

    /// <summary>Libère les abonnements (lignes ACL) et descend récursivement dans les enfants chargés.</summary>
    public void Dispose()
    {
        foreach (var line in AclLines)
        {
            line.Dispose();
        }
        foreach (var child in Children)
        {
            if (child is TreeNodeViewModel node)
            {
                node.Dispose();
            }
        }
    }
}

/// <summary>
/// Familles d'extensions de fichiers et leur couleur (partagées arbre WPF /
/// rapport HTML — garder la liste alignée avec extCat() du gabarit HTML).
/// </summary>
public static class ExtensionCategories
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase);

    private static void Add(string color, params string[] exts)
    {
        foreach (var e in exts) { Map[e] = color; }
    }

    static ExtensionCategories()
    {
        Add("#3B82F6", "DOC", "DOCX", "ODT", "RTF", "PDF", "TXT", "MD", "TEX", "EPUB", "XPS", "ONE", "PAGES"); // documents
        Add("#34D399", "XLS", "XLSX", "XLSM", "CSV", "ODS", "TSV", "NUMBERS");                                  // tableurs
        Add("#F5B400", "PPT", "PPTX", "PPS", "PPSX", "ODP", "KEY");                                             // présentations
        Add("#8B5CF6", "JPG", "JPEG", "PNG", "GIF", "BMP", "SVG", "WEBP", "ICO", "TIF", "TIFF", "RAW", "HEIC", "PSD", "AI"); // images
        Add("#22D3EE", "MP3", "WAV", "FLAC", "OGG", "M4A", "AAC", "WMA", "MID", "MIDI");                        // audio
        Add("#EC4899", "MP4", "AVI", "MKV", "MOV", "WMV", "FLV", "WEBM", "M4V", "MPG", "MPEG", "3GP");          // vidéo
        Add("#F59E0B", "ZIP", "RAR", "7Z", "TAR", "GZ", "BZ2", "XZ", "ISO", "CAB", "ARJ");                      // archives
        Add("#F87171", "EXE", "MSI", "BAT", "CMD", "PS1", "COM", "SCR", "DLL", "SYS", "DRV", "VBS", "JAR", "APP", "MSP"); // exécutables
        Add("#2DD4BF", "CS", "JS", "TS", "PY", "JAVA", "CPP", "C", "H", "HPP", "HTML", "HTM", "CSS", "XML", "JSON",
            "YAML", "YML", "SQL", "SH", "PHP", "RB", "GO", "RS", "XAML", "TOML", "PL", "LUA", "KT", "SWIFT");   // code
        Add("#94A3B8", "DB", "SQLITE", "MDB", "ACCDB", "BAK", "DAT", "LOG", "INI", "CFG", "CONF", "TMP", "LNK", "MAAT"); // données/système
        Add("#A78BFA", "TTF", "OTF", "WOFF", "WOFF2", "EOT", "FON");                                            // polices
        Add("#60A5FA", "MSG", "EML", "PST", "OST", "ICS", "VCF");                                               // messagerie
    }

    /// <summary>Couleur de la famille (gris ardoise par défaut).</summary>
    public static string ColorFor(string ext)
        => Map.TryGetValue(ext, out var c) ? c : "#7E8CA8";
}