Module UnixTimestamp
    Public Function unixTime(Optional timestamp As DateTime = Nothing) As UInt64
        Static epochStart As DateTime = New DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        If timestamp = Nothing Then
            timestamp = DateTime.UtcNow
        End If

        Return (timestamp - epochStart).TotalSeconds
    End Function
End Module
