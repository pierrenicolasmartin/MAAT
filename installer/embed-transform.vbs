' Embarque un transform (.mst) dans un MSI comme sous-stockage nommé par LCID,
' et ajoute la langue au Template du résumé (MSI multilingue).
' Usage : cscript //nologo embed-transform.vbs <msi> <mst> <lcid>
Option Explicit
Const msiOpenDatabaseModeTransact = 1
Const msiViewModifyAssign = 3
Const PID_TEMPLATE = 7

Dim installer, db, view, rec, si, msi, mst, lcid, template
Set installer = CreateObject("WindowsInstaller.Installer")
msi  = WScript.Arguments(0)
mst  = WScript.Arguments(1)
lcid = WScript.Arguments(2)

Set db = installer.OpenDatabase(msi, msiOpenDatabaseModeTransact)

' 1) Insère le transform comme sous-stockage nommé par le LCID.
Set view = db.OpenView("SELECT `Name`,`Data` FROM _Storages")
Set rec = installer.CreateRecord(2)
rec.StringData(1) = lcid
view.Execute rec
rec.SetStream 2, mst
view.Modify msiViewModifyAssign, rec
view.Close

' 2) Déclare la langue dans le Template (« x64;1033 » -> « x64;1033,1036 »).
Set si = db.SummaryInformation(3)
template = si.Property(PID_TEMPLATE)
If InStr(template, lcid) = 0 Then
  si.Property(PID_TEMPLATE) = template & "," & lcid
  si.Persist
End If

db.Commit
WScript.Echo "Embedded " & mst & " (" & lcid & ") ; Template = " & template & "," & lcid
