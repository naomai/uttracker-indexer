Imports MySql.Data.MySqlClient
Imports Naomai.UTT.Indexer.Utt2Database
Imports System.Threading


Public Class DynConfig
    Protected dbCtx As Utt2Context
    Protected nsName As String
    Public Sub New(context As Utt2Context, Optional ns As String = "")
        dbCtx = context
        nsName = ns
    End Sub
    Public Function GetProperty(key As String) As String
        Dim keyFull = GetFullyQualifiedName(key)
        Return dbCtx.ConfigProps.SingleOrDefault(Function(p) p.Key = keyFull).Data
    End Function

    Public Sub SetProperty(key As String, data As String, Optional priv As Boolean = False)
        Dim keyFull = GetFullyQualifiedName(key)
        Dim prop As ConfigProp = dbCtx.ConfigProps.SingleOrDefault(Function(p) p.Key = keyFull)

        If IsNothing(prop) Then
            prop = New ConfigProp With {
                .Key = keyFull,
                .Data = data,
                .IsPrivate = priv
            }
            dbCtx.ConfigProps.Add(prop)
        Else
            prop.Data = data
            prop.IsPrivate = priv
            dbCtx.ConfigProps.Update(prop)
        End If

        dbCtx.SaveChanges()
    End Sub

    Public Sub UnsetProperty(key As String)
        Dim keyFull = GetFullyQualifiedName(key)
        Dim keyGroupPrefix As String = keyFull + ".%"
        Dim propAffected = dbCtx.ConfigProps.Where(
            Function(p) p.Key = keyFull OrElse EF.Functions.Like(p.Key, keyGroupPrefix)
            ).ToList()

        dbCtx.ConfigProps.RemoveRange(propAffected)
        dbCtx.SaveChanges()
    End Sub

    Public Function Ns(subNs As String) As DynConfig
        Return New DynConfig(dbCtx, GetFullyQualifiedName(subNs))
    End Function

    Protected Function GetFullyQualifiedName(key As String) As String
        If nsName = "" Then
            Return key
        End If
        Return nsName + "." + key
    End Function

End Class
