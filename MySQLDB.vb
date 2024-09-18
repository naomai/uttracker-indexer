'Imports System.Data.SQLite
Imports MySql.Data.MySqlClient
Imports System.Threading
Imports System.Data

Public Class MySQLDB
    Public Shared Function makeConnectionStringFromConfigStruct(connectionConfig As MySQLDBConfig) As String
        Dim connectionString As String
        With connectionConfig
            If .protocol = "pipe" Then
                connectionString = String.Format("server=.;uid={1};pwd={2};database={3};protocol=pipe;pipename={0};charset=utf8", .host, .username, .password, .database)
            Else
                connectionString = String.Format("server={0};uid={1};pwd={2};database={3};protocol={4};charset=utf8", .host, .username, .password, .database, .protocol)
            End If
        End With
        Return connectionString
    End Function
End Class

Public Structure MySQLDBConfig
    Dim host As String
    Dim protocol As String
    Dim username As String
    Dim password As String
    Dim database As String
    ' Dim charset As String
End Structure