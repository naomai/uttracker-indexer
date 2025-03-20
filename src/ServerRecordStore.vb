Imports Naomai.UTT.Indexer.Utt2Database

Module ServerRecordStore
    Private serverRecords As New Dictionary(Of String, Server)

    Public Function GetServerDbRecord(addressQuery As String) As Server
        If serverRecords.ContainsKey(addressQuery) Then
            Return serverRecords(addressQuery)
        End If
        Return Nothing
    End Function

    Public Sub RegisterServerDbRecord(record As Server)
        If Not serverRecords.ContainsKey(record.AddressQuery) Then
            serverRecords(record.AddressQuery) = record
        End If
    End Sub
End Module
