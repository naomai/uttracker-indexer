Imports Naomai.UTT.Indexer.Utt2Database

Public Class ServerRepository
    Private serverRecords As New Dictionary(Of String, Server)
    Private dbCtx As Utt2Context

    Public Sub New(dbContext As Utt2Context)
        Me.dbCtx = dbContext
    End Sub


    Public Async Function LoadAsync() As Task

        Try
            Await dbCtx.Servers _
                .Select(Function(s) New With {
                    s,
                    .LatestMatch = s.ServerMatches.OrderByDescending(Function(m) m.Id).FirstOrDefault()
                }) _
            .ToListAsync()

            For Each server In dbCtx.Servers.Local
                AddServer(server)
            Next

        Catch e As Exception

        End Try
    End Function

    Public Function All() As IEnumerable(Of Server)
        Return serverRecords.Values.ToList()
    End Function
    Public Function GetServerByQueryAddress(addressQuery As String) As Server
        If serverRecords.ContainsKey(addressQuery) Then
            Return serverRecords(addressQuery)
        End If
        Return Nothing
    End Function

    Public Sub AddServer(record As Server)
        If Not serverRecords.ContainsKey(record.AddressQuery) Then
            serverRecords(record.AddressQuery) = record
        End If
    End Sub
End Class
