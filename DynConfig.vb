Imports MySql.Data.MySqlClient
Imports Naomai.UTT.ScannerV2.Utt2Database
Imports System.Threading

Public Class DynConfig
    Dim dbCtx As Utt2Context
    Public Sub New(context As Utt2Context)
        dbCtx = context
    End Sub
    Public Function getProperty(key As String)
        Return dbCtx.ConfigProps.SingleOrDefault(Function(p) p.Key = key).Data
    End Function

    Public Sub setProperty(key As String, data As String, Optional priv As Boolean = False)
        Dim prop As ConfigProp = dbCtx.ConfigProps.SingleOrDefault(Function(p) p.Key = key)

        If IsNothing(prop) Then
            prop = New ConfigProp With {
                .Key = key
            }
        End If

        prop.Data = data
        prop.Private = priv
        dbCtx.ConfigProps.Update(prop)
        dbCtx.SaveChanges()
    End Sub

    Public Sub unsetProperty(key As String)
        Dim keyGroupPrefix As String = key + ".%"
        Dim propAffected = dbCtx.ConfigProps.Where(
            Function(p) p.Key = key OrElse EF.Functions.Like(p.Key, keyGroupPrefix)
            ).ToList()

        dbCtx.ConfigProps.RemoveRange(propAffected)
        dbCtx.SaveChanges()
    End Sub

End Class
