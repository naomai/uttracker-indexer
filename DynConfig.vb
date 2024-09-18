Imports MySql.Data.MySqlClient
Imports Naomai.UTT.ScannerV2.Utt2Database
Imports System.Threading

Public Class DynConfig
    Dim dbCtx As Utt2Context
    Protected nsName As String
    Public Sub New(context As Utt2Context, Optional ns As String = "")
        dbCtx = context
        nsName = ns
    End Sub
    Public Function getProperty(key As String)
        Dim keyFull = GetFullyQualifiedName(key)
        Return dbCtx.ConfigProps.SingleOrDefault(Function(p) p.Key = keyFull).Data
    End Function

    Public Sub setProperty(key As String, data As String, Optional priv As Boolean = False)
        Dim keyFull = GetFullyQualifiedName(key)
        Dim prop As ConfigProp = dbCtx.ConfigProps.SingleOrDefault(Function(p) p.Key = keyFull)

        If IsNothing(prop) Then
            prop = New ConfigProp With {
                .Key = keyFull
            }
        End If

        prop.Data = data
        prop.Private = priv
        dbCtx.ConfigProps.Update(prop)
        dbCtx.SaveChanges()
    End Sub

    Public Sub unsetProperty(key As String)
        Dim keyFull = GetFullyQualifiedName(key)
        Dim keyGroupPrefix As String = keyFull + ".%"
        Dim propAffected = dbCtx.ConfigProps.Where(
            Function(p) p.Key = keyFull OrElse EF.Functions.Like(p.Key, keyGroupPrefix)
            ).ToList()

        dbCtx.ConfigProps.RemoveRange(propAffected)
        dbCtx.SaveChanges()
    End Sub

    Protected Function GetFullyQualifiedName(key As String)
        If nsName = "" Then
            Return key
        End If
        Return nsName + "." + key
    End Function

End Class
