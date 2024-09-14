Module JsonSerialize

    Public Function jsonSerialize(obj As Hashtable)
        jsonSerialize = "{"
        Dim first As Boolean = True
        For Each ikey In obj.Keys
            If first Then
                first = False
            Else
                jsonSerialize &= ","
            End If

            jsonSerialize &= """" & jsonEscape(ikey) & """:""" & jsonEscape(obj(ikey)) & """"
        Next
        jsonSerialize &= "}"
    End Function

    Public Function jsonSerialize(obj As Collection)
        jsonSerialize = "["
        Dim first As Boolean = True
        For Each ival In obj
            If first Then
                first = False
            Else
                jsonSerialize &= ","
            End If

            jsonSerialize &= """" & jsonEscape(ival) & """"
        Next
        jsonSerialize &= "]"
    End Function

    Private Function jsonEscape(str As String)
        Return Replace(Replace(str, "\", "\\"), """", "\""")
    End Function
End Module
