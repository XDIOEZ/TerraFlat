Set objFSO = CreateObject("Scripting.FileSystemObject")
Set objFolder = objFSO.GetFolder("d:\_Unity\_UnityProject\FlatWorld\Assets\4_ScriptObjects\4-5_Cook")

arrFiles = Array("原木=木炭.asset", "生肉=熟肉.asset", "粗铁锭=铁锭.asset", _
                 "铁矿+碳=钢.asset", "铁矿=铁锭.asset", "铜+锡=青铜 1.asset", _
                 "铜+锡=青铜.asset", "铁矿=粗铁锭.asset", "铜矿=铜.asset", _
                 "锡矿=锡.asset", "鸡蛋=煎鸡蛋.asset")

modifiedCount = 0

For Each fileName In arrFiles
    filepath = objFolder.Path & "\" & fileName
    If objFSO.FileExists(filepath) Then
        Set objFile = objFSO.OpenTextFile(filepath, 1)
        content = objFile.ReadAll()
        objFile.Close()
        
        If InStr(content, "enableMirrorCrafting") = 0 Then
            newContent = Replace(content, "  action: []", "   enableMirrorCrafting: 1" & vbCrLf & "  action: []")
            
            If newContent <> content Then
                Set objFile = objFSO.OpenTextFile(filepath, 2)
                objFile.Write newContent
                objFile.Close()
                WScript.Echo "Modified: " & fileName
                modifiedCount = modifiedCount + 1
            End If
        End If
    End If
Next

WScript.Echo "Total modified: " & modifiedCount & " files"
