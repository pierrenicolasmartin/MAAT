// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Reflection;
using System.Text;
using MAAT.Core;
using MAAT.Core.Localization;
using MAAT.Core.Models;

namespace MAAT.Export;

/// <summary>
/// Génère le rapport HTML interactif (gabarit porté d'Audit Filer V1.0.5).
///
/// Écriture <b>en streaming</b> directement vers le fichier : les éléments sont
/// consommés un à un (jamais matérialisés en liste) et le HTML n'est jamais
/// construit en une seule chaîne géante — ce qui évitait auparavant d'énormes
/// allocations sur le Large Object Heap, non rendues à l'OS après l'export.
///
/// Données injectées sous forme compacte : une table de chaînes dédupliquée
/// <c>ST</c> (écrite APRÈS les nœuds, une fois collectée) et un tableau de nœuds
/// <c>D</c> ; chaque ACE est <c>[idIdx, mbIdx, drIdx, poIdx, heIdx, srIdx, type]</c>
/// (indices dans <c>ST</c>, <c>-1</c> si vide ; <c>type</c> = 0 Autoriser / 1 Refuser).
/// Les chemins sont stockés <b>relatifs</b> à la racine (préfixe commun retiré).
/// </summary>
public sealed class HtmlReportExporter
{
    private static readonly Lazy<string> Template = new(LoadTemplate);

    private readonly string _lang;

    /// <param name="lang">Code de langue du rapport (« fr » / « en »).</param>
    public HtmlReportExporter(string lang = "fr") => _lang = lang;

    private string L(string key) => ExportStrings.T(_lang, key);

    /// <summary>
    /// Génère le rapport et l'écrit dans <paramref name="path"/> (UTF-8 avec BOM),
    /// en streaming. <paramref name="elementCount"/> est fourni par l'appelant (pas
    /// de pré-comptage : la source est consommée une seule fois, en flux).
    /// </summary>
    public void ExportToFile(
        string path, IEnumerable<AuditItem> items, AuditParameters p,
        string rootPath, DateTimeOffset auditDate, int elementCount, CancellationToken ct = default)
    {
        using var writer = new StreamWriter(
            path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        WriteReport(writer, items, p, rootPath, auditDate, elementCount, ct);
    }

    /// <summary>Écrit le rapport complet dans <paramref name="w"/>, en flux.</summary>
    public void WriteReport(
        TextWriter w, IEnumerable<AuditItem> items, AuditParameters p,
        string rootPath, DateTimeOffset auditDate, int elementCount, CancellationToken ct = default)
    {
        // Deux tables de déduplication, remplies pendant l'écriture des nœuds :
        //  • ST : chaînes distinctes (identités, droits, portées, sources…) ;
        //  • AL : listes d'ACL distinctes. La majorité des fichiers héritent d'une ACL
        //    identique → une même liste se répète des millions de fois. On la stocke
        //    une seule fois et chaque nœud n'en référence que l'index : réduction
        //    massive du poids du rapport et de la charge du navigateur.
        var stringTable = new List<string>(512);
        var stringIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var aclTable = new List<string>(1024);
        var aclIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        int Intern(string? value)
        {
            if (string.IsNullOrEmpty(value)) { return -1; }
            if (!stringIndex.TryGetValue(value, out int idx))
            {
                idx = stringTable.Count;
                stringIndex[value] = idx;
                stringTable.Add(value);
            }
            return idx;
        }

        // Construit la liste d'ACE d'un item (indices ST) et la déduplique → index AL.
        var aceSb = new StringBuilder(256);
        int InternAcl(AuditItem item)
        {
            if (item.Acl.Count == 0) { return -1; }
            aceSb.Clear();
            aceSb.Append('[');
            bool firstAce = true;
            foreach (var ace in item.Acl)
            {
                if (!firstAce) { aceSb.Append(','); }
                firstAce = false;
                aceSb.Append('[')
                     .Append(Intern(ace.Identity)).Append(',')
                     .Append(Intern(ace.ResolvedMembers)).Append(',')
                     .Append(Intern(ace.RightsFr)).Append(',')
                     .Append(Intern(ace.ScopeFr)).Append(',')
                     .Append(Intern(ace.IsInherited ? L("Yes") : L("No"))).Append(',')
                     .Append(Intern(ace.SourcePath)).Append(',')
                     .Append((int)ace.Type).Append(']');
            }
            aceSb.Append(']');
            string key = aceSb.ToString();
            if (!aclIndex.TryGetValue(key, out int idx))
            {
                idx = aclTable.Count;
                aclIndex[key] = idx;
                aclTable.Add(key);
            }
            return idx;
        }

        // Gabarit avec tous les petits jetons substitués ; restent les trois gros
        // jetons de données (D, AL, ST), autour desquels on découpe pour streamer.
        // Ordre dans le gabarit : D (nœuds) → AL (listes d'ACL) → ST (chaînes), pour
        // pouvoir écrire AL et ST une fois les nœuds parcourus (tables alors complètes).
        string tpl = ReplaceChrome(Template.Value, p, rootPath, auditDate, elementCount);
        int iNodes = tpl.IndexOf("@@NODES_JSON@@", StringComparison.Ordinal);
        int iAl = tpl.IndexOf("@@AL_JSON@@", StringComparison.Ordinal);
        int iSt = tpl.IndexOf("@@ST_JSON@@", StringComparison.Ordinal);
        string head = tpl[..iNodes];
        string midAl = tpl[(iNodes + "@@NODES_JSON@@".Length)..iAl];
        string midSt = tpl[(iAl + "@@AL_JSON@@".Length)..iSt];
        string tail = tpl[(iSt + "@@ST_JSON@@".Length)..];

        int rootLen = rootPath.Length;

        w.Write(head);

        // --- Nœuds (streaming) : chemins relatifs FRONT-CODÉS, champs vides omis, ACL déréférencée ---
        // Les éléments arrivent en ordre d'arbre (parent avant enfants) → les chemins
        // consécutifs partagent un long préfixe. On ne stocke que le suffixe ("p") et le
        // nombre de caractères repris du chemin précédent ("k", omis si 0). Le navigateur
        // reconstruit le chemin complet en une passe au chargement. Gain majeur sur les
        // arborescences profondes (le poste dominant du rapport).
        string prevRel = string.Empty;
        w.Write('[');
        bool firstNode = true;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            if (!firstNode) { w.Write(','); }
            firstNode = false;

            int aclIdx = InternAcl(item); // remplit AL/ST ; -1 si aucune ACE

            string rel = item.FullPath.Length > rootLen
                ? item.FullPath[rootLen..].TrimStart('\\')
                : string.Empty;

            int k = CommonPrefixLength(prevRel, rel);
            prevRel = rel;

            w.Write("{\"p\":");
            WriteJsonString(w, rel.Substring(k));
            if (k > 0)
            {
                w.Write(",\"k\":");
                w.Write(k);
            }
            w.Write(",\"d\":");
            w.Write(item.Depth);
            w.Write(",\"t\":");
            w.Write(item.IsFile ? '1' : '0');

            if (p.AuditSize)
            {
                string size = SizeFormatter.Format(item.SizeBytes, item.SizePartial, _lang);
                if (!string.IsNullOrEmpty(size))
                {
                    w.Write(",\"s\":");
                    WriteJsonString(w, size);
                }
            }

            if (aclIdx >= 0)
            {
                w.Write(",\"a\":");
                w.Write(aclIdx);
            }
            w.Write('}');
        }
        w.Write(']');

        w.Write(midAl);

        // --- Table des listes d'ACL (chaque entrée est déjà du JSON [[ace],…]) ---
        w.Write('[');
        for (int i = 0; i < aclTable.Count; i++)
        {
            if (i > 0) { w.Write(','); }
            w.Write(aclTable[i]);
        }
        w.Write(']');

        w.Write(midSt);

        // --- Table de chaînes (après collecte) ---
        w.Write('[');
        for (int i = 0; i < stringTable.Count; i++)
        {
            if (i > 0) { w.Write(','); }
            WriteJsonString(w, stringTable[i]);
        }
        w.Write(']');

        w.Write(tail);
    }

    /// <summary>Substitue tous les jetons « chrome » (métadonnées, libellés, logos),
    /// en laissant intacts les deux gros jetons de données (@@NODES_JSON@@/@@ST_JSON@@).</summary>
    private string ReplaceChrome(string template, AuditParameters p, string rootPath, DateTimeOffset auditDate, int elementCount)
    {
        bool rights = p.AuditRights, sizeMode = p.AuditSize;
        string contentLabel = rights && sizeMode ? L("Content_Both")
            : rights ? L("Content_Rights") : L("Content_Size");
        string modeLabel = p.AuditFiles ? L("Mode_FoldersFiles") : L("Mode_Folders");
        // Profondeur retraduite vers la nomenclature de l'interface (racine = niveau 0).
        string depthLabel = p.Depth switch
        {
            0 => L("Depth_Unlimited"),
            1 => L("Depth_RootOnly"),
            int d => (d - 1).ToString(),
        };
        string title = "MAAT — " + contentLabel;

        return template
            .Replace("@@TITLE_PLAIN@@", title)
            .Replace("@@TITLE_HTML@@", HtmlEscape(title))
            .Replace("@@ROOT_ESCAPED@@", HtmlEscape(rootPath))
            .Replace("@@AUDIT_DATE@@", auditDate.ToString("dd/MM/yyyy HH:mm"))
            .Replace("@@DEPTH_LABEL@@", depthLabel)
            .Replace("@@MODE_LABEL@@", modeLabel)
            .Replace("@@CONTENT_LABEL@@", contentLabel)
            .Replace("@@ELEMENT_COUNT@@", elementCount.ToString())
            .Replace("@@AF@@", p.AuditFiles ? "true" : "false")
            .Replace("@@AD@@", rights ? "true" : "false")
            .Replace("@@AT@@", sizeMode ? "true" : "false")
            // Chemins stockés relatifs → racine JS vide (getRel renvoie la valeur telle quelle).
            .Replace("@@ROOT_PATH_JSON@@", "\"\"")
            // Vrai nom de la racine (dernier segment, ex. « C:\ » → « C: »), affiché dans l'arbre.
            .Replace("@@ROOT_NAME_JSON@@", JsonString(rootPath.TrimEnd('\\').Split('\\')[^1]))
            .Replace("@@L_JSON@@", BuildLabelJson())
            .Replace("@@LOGO_DARK_B64@@", LogoDarkB64.Value)
            .Replace("@@LOGO_LIGHT_B64@@", LogoLightB64.Value)
            .Replace("@@FAVICON_B64@@", FaviconB64.Value)
            .Replace("@@T_LOADING@@", He("H_Loading"))
            .Replace("@@T_BRAND@@", He("H_Brand"))
            .Replace("@@T_SUBTITLE@@", He("H_Subtitle"))
            .Replace("@@T_THEME_TOGGLE@@", He("H_ThemeToggle"))
            .Replace("@@T_DEPTH@@", He("H_Depth"))
            .Replace("@@T_MODE@@", He("H_Mode"))
            .Replace("@@T_CONTENT@@", He("H_Content"))
            .Replace("@@T_ELEMENTS@@", He("H_Elements"))
            .Replace("@@T_SEARCH_PH@@", He("H_SearchPlaceholder"))
            .Replace("@@T_CLEAR@@", He("H_ClearSearch"))
            .Replace("@@T_FILTER_TITLE@@", He("H_FilterTitle"))
            .Replace("@@T_FILTER_ALL@@", He("H_FilterAll"))
            .Replace("@@T_FILTER_EXPLICIT@@", He("H_FilterExplicit"))
            .Replace("@@T_FILTER_INHERITED@@", He("H_FilterInherited"))
            .Replace("@@T_EXPORT_VIEW@@", He("H_ExportView"))
            .Replace("@@T_EXPORT_VIEW_TIP@@", He("H_ExportViewTip"))
            .Replace("@@T_FOOTER@@", He("H_Footer"))
            .Replace("@@T_LICENSE@@", He("H_License"))
            .Replace("@@T_MODAL_TITLE@@", He("H_ModalTitle"))
            .Replace("@@T_MODAL_CLOSE@@", He("H_ModalClose"))
            .Replace("@@T_MODAL_SEARCH@@", He("H_ModalSearch"))
            .Replace("@@T_MODAL_EXPORT@@", He("H_ModalExport"));
    }

    /// <summary>Libellé localisé échappé pour insertion HTML.</summary>
    private string He(string key) => HtmlEscape(L(key));

    /// <summary>Objet JS des libellés dynamiques pour le code du rapport.</summary>
    private string BuildLabelJson()
    {
        (string Js, string Key)[] map =
        {
            ("noAcl", "J_NoAcl"), ("noResult", "J_NoResult"), ("identity", "J_Identity"),
            ("members", "J_Members"), ("type", "J_Type"), ("rights", "J_Rights"), ("scope", "J_Scope"),
            ("inheritance", "J_Inheritance"), ("source", "J_Source"), ("explicit", "J_Explicit"),
            ("inherited", "J_Inherited"), ("explicitTip", "J_ExplicitTip"), ("aclTip", "J_AclToggleTip"),
            ("aceEntries", "J_AceEntries"), ("idGroups", "J_IdentitiesGroups"), ("clickExplore", "J_ClickExplore"),
            ("totalId", "J_TotalIdentities"), ("noGroup", "J_NoGroup"), ("directUsers", "J_DirectUsers"),
            ("result", "J_Result"), ("results", "J_Results"), ("member", "J_Member"), ("members2", "J_Members2"),
            ("adGroups", "J_AdGroups"), ("csvMember", "J_CsvMember"), ("csvNbFolders", "J_CsvNbFolders"),
            ("folders", "J_Folders"), ("files", "J_Files"), ("totalSize", "J_TotalSize"), ("explicitRights", "J_ExplicitRights"),
            ("partialSize", "J_PartialSize"), ("csvName", "J_CsvName"),
            ("users", "J_Users"), ("user", "J_User"), ("group", "J_Group"), ("folder", "J_Folder"), ("noMember", "J_NoMember"),
            ("allow", "Allow"), ("deny", "Deny"), ("yes", "Yes"), ("no", "No"),
            ("cFolder", "Col_Folder"), ("cSize", "Col_Size"), ("cIdentity", "Col_Identity"),
            ("cMembers", "Col_Members"), ("cType", "Col_Type"), ("cRights", "Col_Rights"),
            ("cScope", "Col_Scope"), ("cInherited", "Col_Inherited"), ("cSource", "Col_Source"),
        };
        var sb = new StringBuilder("{");
        for (int i = 0; i < map.Length; i++)
        {
            if (i > 0) { sb.Append(','); }
            sb.Append('"').Append(map[i].Js).Append("\":").Append(JsonString(L(map[i].Key)));
        }
        return sb.Append('}').ToString();
    }

    // Logos (noir pour le thème clair, blanc pour le sombre) et favicon embarqués
    // en base64 : le rapport reste un fichier unique, autonome.
    private static readonly Lazy<string> LogoDarkB64 = new(() => LoadResourceB64("logo_report_dark.png"));
    private static readonly Lazy<string> LogoLightB64 = new(() => LoadResourceB64("logo_report_light.png"));
    private static readonly Lazy<string> FaviconB64 = new(() => LoadResourceB64("favicon.png"));

    private static string LoadResourceB64(string suffix)
    {
        var asm = Assembly.GetExecutingAssembly();
        string name = asm.GetManifestResourceNames()
            .First(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    private static string LoadTemplate()
    {
        var asm = Assembly.GetExecutingAssembly();
        string name = asm.GetManifestResourceNames()
            .First(n => n.EndsWith("report.html", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // Codes des terminateurs de ligne JS, échappés pour une injection sûre.
    private const int LineSeparator = 0x2028;      // U+2028
    private const int ParagraphSeparator = 0x2029; // U+2029

    /// <summary>Écrit une chaîne JSON échappée directement dans <paramref name="w"/>
    /// (RFC 8259 + neutralisation de &lt;, &gt;, &amp;, ', U+2028/U+2029), sans
    /// allouer de chaîne intermédiaire.</summary>
    private static void WriteJsonString(TextWriter w, string? value)
    {
        w.Write('"');
        if (!string.IsNullOrEmpty(value))
        {
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': w.Write("\\\\"); break;
                    case '"': w.Write("\\\""); break;
                    case '\n': w.Write("\\n"); break;
                    case '\r': w.Write("\\r"); break;
                    case '\t': w.Write("\\t"); break;
                    case '<': w.Write("\\u003c"); break;
                    case '>': w.Write("\\u003e"); break;
                    case '&': w.Write("\\u0026"); break;
                    case '\'': w.Write("\\u0027"); break;
                    default:
                        int code = c;
                        if (code == LineSeparator) { w.Write("\\u2028"); }
                        else if (code == ParagraphSeparator) { w.Write("\\u2029"); }
                        else { w.Write(c); }
                        break;
                }
            }
        }
        w.Write('"');
    }

    /// <summary>Variante chaîne (pour les libellés courts du gabarit).</summary>
    private static string JsonString(string? value)
    {
        using var sw = new StringWriter();
        WriteJsonString(sw, value);
        return sw.ToString();
    }

    /// <summary>Nombre de caractères de tête communs à deux chaînes (pour le front-coding des chemins).</summary>
    private static int CommonPrefixLength(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length);
        int i = 0;
        while (i < n && a[i] == b[i]) { i++; }
        return i;
    }

    private static string HtmlEscape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
