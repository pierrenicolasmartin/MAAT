// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Collections.Generic;

namespace MAAT.Export;

/// <summary>
/// Libellés localisés des exports (CSV + rapport HTML). Indépendant de la couche
/// UI : l'appelant passe un code de langue (« fr » / « en »). Évolutif : ajouter
/// un dictionnaire et l'enregistrer dans <see cref="Catalogs"/>.
/// </summary>
public static class ExportStrings
{
    // Catalogues par code de langue. Évolutif : ajouter un dictionnaire ici.
    private static IReadOnlyDictionary<string, string> Catalog(string code) => code switch
    {
        "fr" => Fr,
        _ => En,
    };

    public static string T(string code, string key)
    {
        if (Catalog(code).TryGetValue(key, out var v)) { return v; }
        return En.TryGetValue(key, out var e) ? e : key;
    }

    private static readonly IReadOnlyDictionary<string, string> Fr = new Dictionary<string, string>
    {
        // CSV + en-têtes communs
        ["Col_Folder"] = "Dossier",
        ["Col_Level"] = "Niveau",
        ["Col_Size"] = "Volumétrie",
        ["Col_Identity"] = "Identité",
        ["Col_Members"] = "Membres du groupe",
        ["Col_Type"] = "Type",
        ["Col_Rights"] = "Autorisations",
        ["Col_Scope"] = "Portée du droit",
        ["Col_Inherited"] = "Hérité",
        ["Col_Source"] = "Source héritage",
        ["Allow"] = "Autoriser",
        ["Deny"] = "Refuser",
        ["Yes"] = "Oui",
        ["No"] = "Non",
        // Métadonnées du rapport (valeurs)
        ["Mode_Folders"] = "Dossiers uniquement",
        ["Mode_FoldersFiles"] = "Dossiers + Fichiers",
        ["Content_Both"] = "Droits + Volumétrie",
        ["Content_Rights"] = "Droits uniquement",
        ["Content_Size"] = "Volumétrie uniquement",
        ["Depth_Unlimited"] = "Illimitée",
        ["Depth_RootOnly"] = "Racine seule",
        // HTML statique
        ["H_Loading"] = "Chargement du rapport…",
        ["H_Brand"] = "Rapport généré par",
        ["H_Subtitle"] = "Monitoring et Audit des Accès pour la Traçabilité",
        ["H_ThemeToggle"] = "Basculer le thème clair / sombre",
        ["H_Depth"] = "Profondeur",
        ["H_Mode"] = "Mode",
        ["H_Content"] = "Contenu",
        ["H_Elements"] = "éléments",
        ["H_SearchPlaceholder"] = "Rechercher un dossier, un fichier, une identité, un membre…",
        ["H_ClearSearch"] = "Effacer la recherche (Échap)",
        ["H_FilterTitle"] = "Filtrer les entrées ACL affichées",
        ["H_FilterAll"] = "Tous les droits",
        ["H_FilterExplicit"] = "Explicites uniquement",
        ["H_FilterInherited"] = "Hérités uniquement",
        ["H_ExpandAll"] = "Tout déplier",
        ["H_CollapseAll"] = "Tout replier",
        ["H_ExpandAllTip"] = "Déplier toute l'arborescence",
        ["H_CollapseAllTip"] = "Replier l'arborescence",
        ["H_ExportView"] = "Exporter la vue",
        ["H_ExportViewTip"] = "Exporter la vue courante (recherche et filtre appliqués) en CSV",
        ["H_Footer"] = "Les résultats peuvent contenir des informations sensibles ou confidentielles. | Rapport généré le",
        ["H_License"] = "Licence GPLv3",
        ["H_ModalTitle"] = "Identités — Groupes AD & Utilisateurs",
        ["H_ModalClose"] = "Fermer (Échap)",
        ["H_ModalSearch"] = "Rechercher un groupe ou un membre…",
        ["H_ModalExport"] = "Exporter CSV",
        // HTML / JS dynamique
        ["J_NoAcl"] = "Aucune entrée ACL pour ce filtre.",
        ["J_NoResult"] = "Aucun résultat.",
        ["J_Identity"] = "Identité",
        ["J_Members"] = "Membres",
        ["J_Type"] = "Type",
        ["J_Rights"] = "Autorisations",
        ["J_Scope"] = "Portée",
        ["J_Inheritance"] = "Héritage",
        ["J_Source"] = "Source",
        ["J_Explicit"] = "Explicite",
        ["J_Inherited"] = "Hérité",
        ["J_ExplicitTip"] = "Droit défini directement sur cet élément",
        ["J_AclToggleTip"] = "Afficher / masquer le détail des ACL",
        ["J_AceEntries"] = "Entrées ACL",
        ["J_IdentitiesGroups"] = "Identités / Groupes AD",
        ["J_ClickExplore"] = "Cliquez pour explorer les groupes et leurs membres",
        ["J_TotalIdentities"] = "Total identités",
        ["J_NoGroup"] = "Aucun groupe ou utilisateur trouvé.",
        ["J_DirectUsers"] = "Utilisateurs / Identités directes",
        ["J_Result"] = "résultat",
        ["J_Results"] = "résultats",
        ["J_Member"] = "membre",
        ["J_Members2"] = "membres",
        ["J_AdGroups"] = "Groupes AD",
        ["J_CsvMember"] = "Membre",
        ["J_CsvNbFolders"] = "Nb dossiers concernés",
        ["J_Folders"] = "Dossiers",
        ["J_Files"] = "Fichiers",
        ["J_TotalSize"] = "Volumétrie totale",
        ["J_ExplicitRights"] = "Droits explicites",
        ["J_ExpandWarn"] = "Le rapport contient {n} éléments. Tout déplier génère l'arborescence complète et peut figer le navigateur quelques secondes. Continuer ?",
        ["J_PartialSize"] = "Volumétrie partielle : certains sous-éléments étaient inaccessibles lors du calcul",
        ["J_CsvName"] = "Audit_Volumetrie",
        ["J_Users"] = "Utilisateurs",
        ["J_User"] = "Utilisateur",
        ["J_Group"] = "Groupe",
        ["J_Folder"] = "dossier",
        ["J_NoMember"] = "(aucun membre)",
    };

    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        ["Col_Folder"] = "Folder",
        ["Col_Level"] = "Level",
        ["Col_Size"] = "Size",
        ["Col_Identity"] = "Identity",
        ["Col_Members"] = "Group members",
        ["Col_Type"] = "Type",
        ["Col_Rights"] = "Permissions",
        ["Col_Scope"] = "Right scope",
        ["Col_Inherited"] = "Inherited",
        ["Col_Source"] = "Inheritance source",
        ["Allow"] = "Allow",
        ["Deny"] = "Deny",
        ["Yes"] = "Yes",
        ["No"] = "No",
        ["Mode_Folders"] = "Folders only",
        ["Mode_FoldersFiles"] = "Folders + Files",
        ["Content_Both"] = "Permissions + Size",
        ["Content_Rights"] = "Permissions only",
        ["Content_Size"] = "Size only",
        ["Depth_Unlimited"] = "Unlimited",
        ["Depth_RootOnly"] = "Root only",
        ["H_Loading"] = "Loading report…",
        ["H_Brand"] = "Report generated by",
        ["H_Subtitle"] = "Monitoring and Access Auditing for Traceability",
        ["H_ThemeToggle"] = "Toggle light / dark theme",
        ["H_Depth"] = "Depth",
        ["H_Mode"] = "Mode",
        ["H_Content"] = "Content",
        ["H_Elements"] = "items",
        ["H_SearchPlaceholder"] = "Search a folder, a file, an identity, a member…",
        ["H_ClearSearch"] = "Clear search (Esc)",
        ["H_FilterTitle"] = "Filter displayed ACL entries",
        ["H_FilterAll"] = "All permissions",
        ["H_FilterExplicit"] = "Explicit only",
        ["H_FilterInherited"] = "Inherited only",
        ["H_ExpandAll"] = "Expand all",
        ["H_CollapseAll"] = "Collapse all",
        ["H_ExpandAllTip"] = "Expand the whole tree",
        ["H_CollapseAllTip"] = "Collapse the tree",
        ["H_ExportView"] = "Export view",
        ["H_ExportViewTip"] = "Export the current view (search and filter applied) to CSV",
        ["H_Footer"] = "Results may contain sensitive or confidential information. | Report generated on",
        ["H_License"] = "GPLv3 License",
        ["H_ModalTitle"] = "Identities — AD Groups & Users",
        ["H_ModalClose"] = "Close (Esc)",
        ["H_ModalSearch"] = "Search a group or a member…",
        ["H_ModalExport"] = "Export CSV",
        ["J_NoAcl"] = "No ACL entry for this filter.",
        ["J_NoResult"] = "No result.",
        ["J_Identity"] = "Identity",
        ["J_Members"] = "Members",
        ["J_Type"] = "Type",
        ["J_Rights"] = "Permissions",
        ["J_Scope"] = "Scope",
        ["J_Inheritance"] = "Inheritance",
        ["J_Source"] = "Source",
        ["J_Explicit"] = "Explicit",
        ["J_Inherited"] = "Inherited",
        ["J_ExplicitTip"] = "Permission set directly on this item",
        ["J_AclToggleTip"] = "Show / hide ACL details",
        ["J_AceEntries"] = "ACL entries",
        ["J_IdentitiesGroups"] = "Identities / AD Groups",
        ["J_ClickExplore"] = "Click to explore groups and their members",
        ["J_TotalIdentities"] = "Total identities",
        ["J_NoGroup"] = "No group or user found.",
        ["J_DirectUsers"] = "Users / Direct identities",
        ["J_Result"] = "result",
        ["J_Results"] = "results",
        ["J_Member"] = "member",
        ["J_Members2"] = "members",
        ["J_AdGroups"] = "AD Groups",
        ["J_CsvMember"] = "Member",
        ["J_CsvNbFolders"] = "Affected folders",
        ["J_Folders"] = "Folders",
        ["J_Files"] = "Files",
        ["J_TotalSize"] = "Total size",
        ["J_ExplicitRights"] = "Explicit permissions",
        ["J_ExpandWarn"] = "The report contains {n} items. Expanding everything renders the whole tree and may freeze the browser for a few seconds. Continue?",
        ["J_PartialSize"] = "Partial size: some sub-items were inaccessible during computation",
        ["J_CsvName"] = "Audit_Size",
        ["J_Users"] = "Users",
        ["J_User"] = "User",
        ["J_Group"] = "Group",
        ["J_Folder"] = "folder",
        ["J_NoMember"] = "(no member)",
    };
}