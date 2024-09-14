Imports MySql.Data.MySqlClient
Imports System.Threading

Public Class DynConfig
    Dim db As MySqlConnection
    Public Sub New(connection As MySqlConnection)
        db = connection
    End Sub
    Public Function getProperty(key As String)
        Dim dynconfigCmd As New MySqlCommand("Select `data` from `config_props` where `key`=@key", db)
        dynconfigCmd.Parameters.AddWithValue("@key", key)
        SyncLock db
            getProperty = dynconfigCmd.ExecuteScalar()
        End SyncLock
        dynconfigCmd.Dispose()
    End Function

    Public Sub setProperty(key As String, data As String, Optional priv As Boolean = False)
        Dim dynconfigCmd As New MySqlCommand("Replace into `config_props`(`key`,`data`,`private`) values(@key,@data,@private)", db)
        dynconfigCmd.Parameters.AddWithValue("@key", key)
        dynconfigCmd.Parameters.AddWithValue("@data", data)
        dynconfigCmd.Parameters.AddWithValue("@private", priv)
        SyncLock db
            dynconfigCmd.ExecuteNonQuery()
        End SyncLock
        dynconfigCmd.Dispose()
    End Sub

    Public Sub unsetProperty(key As String)
        Dim dynconfigCmd As New MySqlCommand("Delete from `config_props` where `key` = @key", db)
        dynconfigCmd.Parameters.AddWithValue("@key", key)
        SyncLock db
            dynconfigCmd.ExecuteNonQuery()
        End SyncLock
        dynconfigCmd.CommandText = "Delete from `config_props` where `key` like @key"
        dynconfigCmd.Parameters.Clear()
        dynconfigCmd.Parameters.AddWithValue("@key", key & ".%")
        SyncLock db
            dynconfigCmd.ExecuteNonQuery()
        End SyncLock
        dynconfigCmd.Dispose()
    End Sub

End Class
