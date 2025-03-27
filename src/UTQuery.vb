' UT query protocol things
' might be also usable for other gamespy-based games


Imports System.Diagnostics.Metrics
Imports System.Text

Public Class UTQueryPacket
    Inherits System.Collections.CollectionBase
    Implements IEnumerable(Of UTQueryKeyValuePair)
    Protected packetContent As New List(Of UTQueryKeyValuePair)
    Protected packetFlags As Flags
    Protected queryId As Integer = 0

    Shared metric As New Meter("UTQuery")
    'Shared mtSerializationLength As Histogram(Of Integer) = metric.CreateHistogram(Of Integer)("SerializationLength")


    Public Sub New(packet As String, Optional flags As Flags = 0)
        Me.New(flags)
        ReplaceContentFromString(packet)
    End Sub

    Public Sub New(packetHashtable As Hashtable, Optional flags As Flags = 0)
        Me.New(flags)
        ReplaceContentFromHashtable(packetHashtable)
    End Sub

    Public Sub New(Optional flags As Flags = 0)
        Me.packetFlags = flags
    End Sub

    Protected Sub ReplaceContentFromString(packet As String)
        packetContent = ParseGamespyResponse(packet, Me.packetFlags)
        For Each variable In packetContent
            If variable.key = "queryid" Then
                queryId = variable.value
                Exit For
            End If
        Next
        packetContent.RemoveAll(Function(value As UTQueryKeyValuePair)
                                    Return (value.key = "queryid")
                                End Function)
    End Sub

    Protected Sub ReplaceContentFromHashtable(packetHashtable As Hashtable)
        packetContent = ConvertHashtablePacketToListPacket(packetHashtable)
    End Sub

    Protected Shared Function ConvertHashtablePacketToListPacket(packetHT As Hashtable) As List(Of UTQueryKeyValuePair)
        Dim result As New List(Of UTQueryKeyValuePair)
        Dim packetId As Integer
        For Each key In packetHT.Keys
            If packetHT(key).GetType Is GetType(List(Of String)) Then
                For Each valueSub As String In packetHT(key)
                    packetId = 1 + Math.Floor(result.Count / 10)
                    result.Add(CreateKVPair(key, valueSub, packetId))
                Next
            Else
                packetId = 1 + Math.Floor(result.Count / 10)
                result.Add(CreateKVPair(key, packetHT(key), packetId))
            End If
        Next
        Return result
    End Function

    Protected Shared Function ConvertListPacketToDictionary(packetList As List(Of UTQueryKeyValuePair)) As Dictionary(Of String, String)
        Dim result As New Dictionary(Of String, String)
        For Each variable In packetList
            result.Item(variable.key) = variable.value
        Next
        Return result
    End Function

    Public Function ConvertToHashtablePacket() As Hashtable
        Return New Hashtable(ConvertToDictionary())
    End Function
    Public Function ConvertToDictionary() As Dictionary(Of String, String)
        Return ConvertListPacketToDictionary(packetContent)
    End Function

    Shared Function CreateKVPair(key As String, value As String, Optional packetId As Integer = 1) As UTQueryKeyValuePair
        CreateKVPair.key = key
        CreateKVPair.value = value
        CreateKVPair.sourcePacketId = packetId
    End Function

    Public Overrides Function ToString() As String
        Return CreateGamespyResponseString(packetFlags)
    End Function

    Public Sub Add(key As String, value As String)
        Dim packetId = 1 + Math.Floor(packetContent.Count / 10)
        packetContent.Add(CreateKVPair(key, value, packetId))
    End Sub

    Public Sub Add(serializedString As String)
        Dim unserialized = PacketUnserialize(serializedString)
        For Each field In unserialized
            Me.Add(field.key, field.value)
        Next
    End Sub

    Public Sub AddIndexed(key As String, value As String, Optional index As Integer = -1)
        If index = -1 Then
            index = 0
            While ContainsKey(key & "_" & index)
                index += 1
            End While

        End If
        Me.Item(key & "_" & index) = value
    End Sub

    Public Sub Remove(key As String)
        packetContent.RemoveAll(Function(pair) pair.key = key)
    End Sub

    Public Shadows Sub RemoveAt(key As String)
        Remove(key)
    End Sub

    Public Shadows Sub Clear()
        packetContent.Clear()
    End Sub

    Default Public Overloads Property Item(key As String) As String
        Get
            Dim variableList As List(Of UTQueryKeyValuePair) = GetElementsByKey(key)
            If variableList.Count Then
                Return variableList(0).value
            Else
                Return Nothing
            End If
        End Get
        Set(value As String)
            Dim varId = GetElementIdByKey(key)
            Dim tmpKVPair As UTQueryKeyValuePair
            If varId <> -1 Then
                tmpKVPair = packetContent(varId)
                tmpKVPair.value = value
                packetContent(varId) = tmpKVPair
            Else
                tmpKVPair.key = key
                tmpKVPair.value = value
                packetContent.Add(tmpKVPair)
            End If
        End Set
    End Property

    Public Shadows Property Count As Integer
        Get
            Return packetContent.Count
        End Get
        Set(value As Integer)

        End Set
    End Property

    Public Shadows Property List As Hashtable
        Get
            Return ConvertToHashtablePacket()
        End Get
        Set(value As Hashtable)
            packetContent = ConvertHashtablePacketToListPacket(value)
        End Set
    End Property

    Public Function ContainsKey(key As String) As Boolean
        Return GetElementIdByKey(key) <> -1
    End Function

    Public Shared Narrowing Operator CType(packetHT As Hashtable) As UTQueryPacket
        Return New UTQueryPacket(packetHT)
    End Operator

    Public Shared Widening Operator CType(packet As UTQueryPacket) As Hashtable
        Return packet.ConvertToHashtablePacket()
    End Operator

    Public Shared Widening Operator CType(packet As UTQueryPacket) As String
        Return packet.ToString()
    End Operator

    Protected Function GetElementsByKey(key As String) As List(Of UTQueryKeyValuePair)
        Dim elements As New List(Of UTQueryKeyValuePair)
        For Each variable In packetContent
            If String.Compare(variable.key, key, True) = 0 Then elements.Add(variable)
        Next
        Return elements
    End Function

    Protected Function GetElementIdByKey(key As String) As Integer
        For i = 0 To packetContent.Count - 1
            If String.Compare(packetContent(i).key, key, True) = 0 Then Return i
        Next
        Return -1
    End Function

    ''' <summary>
    ''' Reassemble and parse Gamespy protocol response into a Hashtable object
    ''' </summary>
    ''' <param name="response">Response received from server</param>
    ''' <param name="flags">Flags determining the behavior of parser</param>
    ''' <remarks></remarks>
    Protected Shared Function ParseGamespyResponse(
                            ByVal response As String,
                            Optional flags As Flags = Flags.UTQP_SimpleRequest
                        ) As List(Of UTQueryKeyValuePair)

        Dim packetContent As New Hashtable() ' temporary array of values from currently processed packet
        Dim responseResult As New List(Of UTQueryKeyValuePair)


        Dim packetsSequence(30) As Hashtable ' used to merge all packets into a full response
        Dim responseId As Integer?
        Dim packetExpectedCount As Integer = 0
        Dim receivedCount As Integer = 0
        Dim expectQueryId = Not flags.HasFlag(Flags.UTQP_NoQueryId)
        Dim expectFinal = Not flags.HasFlag(Flags.UTQP_NoFinal)

        Dim isResponseComplete = Not expectFinal
        Dim isMultiIndex = flags.HasFlag(Flags.UTQP_MultiIndex)
        Dim isMasterServer = flags.HasFlag(Flags.UTQP_MasterServer)

        Try

            Dim keyName As String, value As String
            Dim packetId As Integer = -1

            Dim responseList = PacketUnserialize(response)

            ValidateResponseWithFlags(responseList, flags)

            'chunks = Split(responseString, "\")
            'For i = 1 To chunks.Count - 2 Step 2

            Dim propPrevious As UTQueryKeyValuePair = Nothing
            For Each prop In responseList
                keyName = prop.key
                value = prop.value

                Select Case keyName
                    Case "final", "wookie"
                        If Not IsNothing(propPrevious) AndAlso propPrevious.key = "queryid" Then
                            packetExpectedCount = packetId
                        Else
                            isResponseComplete = True
                        End If
                        ' we're not finished! there might be some more content from packet that might have arrived late

                    Case "queryid"
                        Dim sequenceIdChunks As String() = value.Split(".")
                        If Not IsNothing(responseId) AndAlso sequenceIdChunks(0) <> responseId Then
                            ' discard current packet - different response ID
                            packetContent.Clear()
                        End If
                        responseId = sequenceIdChunks(0)
                        packetId = sequenceIdChunks(1)
                        'prop.value = responseId

                        'packetContent(keyName) = prop
                        receivedCount += 1
                        If isResponseComplete Then
                            If IsNothing(packetId) Then
                                'empty (but complete and valid) response
                                'TODO investigate reason for this - are there any responses without packetId like \queryid\5\ ?
                                For Each var In packetContent.Keys
                                    prop.sourcePacketId = 1
                                    responseResult.Add(prop)
                                Next
                                Return responseResult
                            End If
                            packetExpectedCount = Math.Max(packetId, packetExpectedCount)
                            isResponseComplete = False
                        End If
                        packetsSequence(packetId) = packetContent.Clone()
                        packetContent.Clear()

                    Case Else
                        If keyName = "secure" AndAlso value = "wookie" Then ' special case for master-server response
                            isResponseComplete = True
                        End If

                        If isMultiIndex Then ' multi-index = has multiple keys with the same name, like \ip\123\ip\456\ip\...
                            If Not packetContent.ContainsKey(keyName) Then
                                packetContent(keyName) = New List(Of UTQueryKeyValuePair)
                            End If
                            packetContent(keyName).Add(prop)
                        Else
                            packetContent(keyName) = prop
                        End If

                End Select

                propPrevious = prop
            Next

            Dim propEdited As UTQueryKeyValuePair
            Dim hasQueryId = Not IsNothing(responseId)

            If Not hasQueryId AndAlso isResponseComplete AndAlso isMasterServer Then ' handle master server response
                For Each var In packetContent.Keys

                    If packetContent(var).GetType = GetType(List(Of UTQueryKeyValuePair)) Then
                        For Each variableIndex As UTQueryKeyValuePair In packetContent(var)
                            variableIndex.sourcePacketId = 1
                            responseResult.Add(variableIndex)
                        Next
                    ElseIf packetContent(var).GetType = GetType(UTQueryKeyValuePair) Then
                        propEdited = packetContent(var)
                        propEdited.sourcePacketId = 1
                        responseResult.Add(propEdited)
                    Else
                        propEdited.key = var
                        propEdited.value = packetContent(var)
                        propEdited.sourcePacketId = 1
                        responseResult.Add(propEdited)
                    End If

                Next
                Return responseResult
            End If


            If packetExpectedCount = 0 Then
                ' no QueryId, append straight away
                For Each var In packetContent.Keys
                    propEdited.key = var
                    propEdited.value = packetContent(var).value
                    propEdited.sourcePacketId = 1
                    responseResult.Add(propEdited)
                Next
                Return responseResult
            End If

            ' incomplete/malformed response checks
            If expectQueryId AndAlso receivedCount < packetExpectedCount Then
                Throw New UTQueryResponseIncompleteException("Missing packets in response")
            End If

            ' put all the pieces in correct order
            For packetId = LBound(packetsSequence) To UBound(packetsSequence)
                Dim packetTable As Hashtable = packetsSequence(packetId)
                If Not IsNothing(packetTable) AndAlso packetTable.Count > 0 Then
                    For Each packetElement As DictionaryEntry In packetTable
                        propEdited = packetElement.Value
                        propEdited.sourcePacketId = packetId

                        responseResult.Add(propEdited)
                    Next
                End If
            Next
            If hasQueryId Then
                propEdited.key = "queryid"
                propEdited.value = responseId
                propEdited.sourcePacketId = Nothing
                responseResult.Add(propEdited)
            End If
        Catch ex As IndexOutOfRangeException
            Throw New UTQueryResponseIncompleteException
        Catch ex As NullReferenceException
            Debugger.Break()
        End Try

        Return responseResult
    End Function

    ''' <summary>
    ''' Check packet content for required fields, according to given flags. 
    ''' If Flags.UTQP_NoQueryId is set: expects **zero** occurences of `queryid` field; otherwise: **at least one**.
    ''' If Flags.UTQP_NoFinal is set: expects **zero** occurences of `final` or `wookie` field; otherwise: **exactly one**.
    ''' </summary>
    ''' <param name="response">list of key/pair values representing packet content</param>
    ''' <param name="flags">flags against which the packet will be validated </param>
    Protected Shared Sub ValidateResponseWithFlags(response As List(Of UTQueryKeyValuePair), flags As Flags)
        Dim queryIdCount As Integer = (From field In response Where field.key = "queryid" Select field).Count()
        Dim finalCount As Integer = (From field In response Where field.key = "final" OrElse field.key = "wookie" Select field).Count()

        Dim expectsQueryId = Not CBool(flags And Flags.UTQP_NoQueryId)
        Dim expectsFinal = Not CBool(flags And Flags.UTQP_NoFinal)

        If Not expectsFinal AndAlso finalCount <> 0 Then
            Throw New UTQueryInvalidResponseException("Unexpected Final field")
        ElseIf expectsFinal AndAlso finalCount = 0 Then
            Throw New UTQueryResponseIncompleteException("Missing packets in response")
        ElseIf expectsFinal AndAlso finalCount > 1 Then
            Throw New UTQueryInvalidResponseException($"Expected one Final field, got {finalCount}")
        End If

        If Not expectsQueryId AndAlso queryIdCount <> 0 Then
            Throw New UTQueryInvalidResponseException("Unexpected QueryId field")
        ElseIf expectsQueryId AndAlso queryIdCount = 0 Then
            Throw New UTQueryInvalidResponseException("No QueryId field present")
        End If
    End Sub

    Protected Shared Function PacketUnserialize(packetString As String) As List(Of UTQueryKeyValuePair)
        Dim result As New List(Of UTQueryKeyValuePair)

        If packetString = "" Then
            Throw New UTQueryResponseIncompleteException("Empty packet")
        ElseIf packetString(0) <> "\"c Then
            Throw New UTQueryInvalidResponseException("Packet should start with backslash, got '" & Asc(packetString(0)) & "'")
        End If

        Dim offset = 1
        Do
            Dim newEntry As UTQueryKeyValuePair
            Dim kvSeparatorIdx = packetString.IndexOf("\"c, offset)

            If kvSeparatorIdx = -1 Then
                Throw New UTQueryInvalidResponseException("Unexpected end of packet")
            End If

            newEntry.key = packetString.Substring(offset, kvSeparatorIdx - offset).ToLower()

            Dim valueStartingIdx As Integer = kvSeparatorIdx + 1
            Dim valueEndingIdx As Integer = valueStartingIdx
            Dim doubleBackslash As Boolean
            Do ' get entire value with escape sequences "\\"
                valueEndingIdx = packetString.IndexOf("\"c, valueEndingIdx) + 1
                doubleBackslash = valueEndingIdx <> 0 AndAlso valueEndingIdx < packetString.Length AndAlso packetString(valueEndingIdx) = "\"c
                If doubleBackslash Then
                    valueEndingIdx += 1
                End If
            Loop While doubleBackslash

            If valueEndingIdx = 0 Then
                valueEndingIdx = packetString.Length
            Else
                valueEndingIdx -= 1
            End If

            Dim valueLength = valueEndingIdx - valueStartingIdx

            offset = valueEndingIdx + 1

            newEntry.value = packetString.Substring(valueStartingIdx, valueLength).Replace("\\", "\")
            newEntry.sourcePacketId = Nothing
            result.Add(newEntry)
        Loop While offset < packetString.Length

        Return result
    End Function

    Protected Function CreateGamespyResponse(Optional packetFlags As Flags = 0) As List(Of String)

        Dim responsePackets As New List(Of String)
        Dim currentPacket As String
        Dim currentPacketId = 0, doneSomething As Boolean
        Dim orphanPacket As String = ""
        Do
            doneSomething = False
            currentPacket = ""
            For Each variable In packetContent
                If Not packetFlags.HasFlag(Flags.UTQP_NoQueryId) AndAlso variable.key = "queryid" AndAlso queryId = 0 Then
                    queryId = variable.value
                Else
                    If currentPacketId = 0 AndAlso variable.sourcePacketId = 0 Then
                        orphanPacket &= variable.ToString
                        doneSomething = True
                    ElseIf variable.sourcePacketId = currentPacketId Then
                        currentPacket &= variable.ToString
                        doneSomething = True
                    End If
                End If
            Next

            If currentPacket <> "" Then
                If Not packetFlags.HasFlag(Flags.UTQP_NoQueryId) Then
                    currentPacket &= "\queryid\" & queryId & "." & currentPacketId
                End If
                responsePackets.Add(currentPacket)
            End If

            If doneSomething OrElse currentPacketId = 0 Then
                currentPacketId += 1
                doneSomething = True
            End If
        Loop While doneSomething

        If orphanPacket <> "" Then
            If Not packetFlags.HasFlag(Flags.UTQP_NoQueryId) Then
                If queryId = 0 Then
                    queryId = New System.Random().Next(10, 99)
                End If
                orphanPacket &= "\queryid\" & queryId & "." & currentPacketId
            End If
            responsePackets.Add(orphanPacket)
        End If

        If Not packetFlags.HasFlag(Flags.UTQP_NoFinal) Then
            responsePackets(responsePackets.Count - 1) &= "\final\"
        End If


        Return responsePackets
    End Function

    Protected Function CreateGamespyResponseString(Optional packetFlags As Flags = 0) As String
        Dim list = CreateGamespyResponse(packetFlags)
        Dim builder = New StringBuilder(capacity:=58, value:="")
        For Each packet In list
            builder.Append(packet)
        Next
        'mtSerializationLength.Record(CreateGamespyResponseString.Length)
        Return builder.ToString()
    End Function

    Public Shared Function EscapeGsString(str As String)
        Return str.Replace("\", "_")
    End Function

    Public Function Validate(rules As Dictionary(Of String, String)) As Hashtable
        Dim validator = UTQueryValidator.FromRuleDict(rules)
        Return validator.Validate(Me)
    End Function

    ' IEnumerable

    Public Overloads Function GetEnumerator() As IEnumerator(Of UTQueryKeyValuePair) Implements IEnumerable(Of UTQueryKeyValuePair).GetEnumerator
        Return packetContent.GetEnumerator()
    End Function

    <FlagsAttribute()> Public Enum Flags
        UTQP_NoQueryId = 1
        UTQP_NoFinal = 2
        UTQP_ReadyToSend = 4
        UTQP_MultiIndex = 8
        UTQP_MasterServer = 16 Or UTQP_NoQueryId
        UTQP_MasterServerIpList = 16 Or UTQP_MasterServer Or UTQP_MultiIndex
        UTQP_SimpleRequest = UTQP_NoQueryId Or UTQP_NoFinal
    End Enum
End Class


Public Structure UTQueryKeyValuePair
    Dim key As String
    Dim value As String
    Dim sourcePacketId As Integer
    Public Overrides Function ToString() As String
        Return "\" & UTQueryPacket.EscapeGsString(Me.key) & "\" & UTQueryPacket.EscapeGsString(Me.value)
    End Function
End Structure


Public Class UTQueryResponseIncompleteException
    Inherits Exception

    Sub New()
        MyBase.New("Missing packets in response")
    End Sub

    Sub New(message As String)
        MyBase.New(message)
    End Sub
End Class

Public Class UTQueryInvalidResponseException
    Inherits Exception

    Sub New()
        MyBase.New("Malformed response")
    End Sub

    Sub New(message As String)
        MyBase.New(message)
    End Sub
End Class
