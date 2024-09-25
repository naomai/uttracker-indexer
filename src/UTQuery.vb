' UT query protocol things
' might be also usable for other gamespy-based games



Public Class UTQueryPacket
    Inherits System.Collections.CollectionBase
    Implements IEnumerable(Of UTQueryKeyValuePair)
    Implements IDisposable
    Dim packetContent As New List(Of UTQueryKeyValuePair)
    Dim queryId As Integer = 0
    Public packetFlags As UTQueryPacketFlags

    Private disposedValue As Boolean

    Public Sub New(packet As String, Optional packetFlags As UTQueryPacketFlags = 0)
        Me.New(packetFlags)
        parseString(packet)
    End Sub

    Public Sub New(packetHashtable As Hashtable, Optional packetFlags As UTQueryPacketFlags = 0)
        createFromHashtable(packetHashtable)
    End Sub

    Public Sub New(Optional packetFlags As UTQueryPacketFlags = 0)
        Me.packetFlags = packetFlags
    End Sub

    Public Sub parseString(packet As String)
        packetContent = parseGamespyResponse(packet, CBool(Me.packetFlags And UTQueryPacketFlags.UTQP_NoQueryId))
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

    Protected Sub createFromHashtable(packetHashtable As Hashtable)
        packetContent = convertHashtablePacketToListPacket(packetHashtable)
    End Sub

    Protected Shared Function convertHashtablePacketToListPacket(packetHT As Hashtable) As List(Of UTQueryKeyValuePair)
        convertHashtablePacketToListPacket = New List(Of UTQueryKeyValuePair)
        Dim packetId As Integer
        For Each key In packetHT.Keys
            If packetHT(key).GetType Is GetType(List(Of String)) Then
                For Each valueSub As String In packetHT(key)
                    packetId = 1 + Math.Floor(convertHashtablePacketToListPacket.Count / 10)
                    convertHashtablePacketToListPacket.Add(staticCreateKVPair(key, valueSub, packetId))
                Next
            Else
                packetId = 1 + Math.Floor(convertHashtablePacketToListPacket.Count / 10)
                convertHashtablePacketToListPacket.Add(staticCreateKVPair(key, packetHT(key), packetId))
            End If
        Next
    End Function

    Protected Shared Function convertListPacketToHashtablePacket(packetList As List(Of UTQueryKeyValuePair)) As Hashtable
        convertListPacketToHashtablePacket = New Hashtable
        For Each variable In packetList
            convertListPacketToHashtablePacket.Item(variable.key) = variable.value
        Next
    End Function

    Public Function convertToHashtablePacket()
        Return convertListPacketToHashtablePacket(packetContent)
    End Function

    Public Sub setReadyToSend(Optional rtsFlag As Boolean = True)
        packetFlags = IIf(rtsFlag,
                          packetFlags Or UTQueryPacketFlags.UTQP_ReadyToSend,
                          packetFlags And (Not UTQueryPacketFlags.UTQP_ReadyToSend))
    End Sub


    Shared Function staticCreateKVPair(key As String, value As String, Optional packetId As Integer = 1) As UTQueryKeyValuePair
        staticCreateKVPair.key = key
        staticCreateKVPair.value = value
        staticCreateKVPair.sourcePacketId = packetId
    End Function

    Public Overrides Function ToString() As String
        Return createGamespyResponseString(packetFlags)
    End Function

    Public Sub Add(key As String, value As String)
        Dim packetId = 1 + Math.Floor(packetContent.Count / 10)
        packetContent.Add(staticCreateKVPair(key, value, packetId))
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
        For i = 0 To packetContent.Count - 1
            If packetContent(i).key = key Then
                packetContent.RemoveAt(i)
            End If
        Next
    End Sub

    Public Shadows Sub RemoveAt(key As String)
        Remove(key)
    End Sub

    Public Shadows Sub Clear()
        packetContent.Clear()
    End Sub

    Default Public Overloads Property Item(key As String) As String
        Get
            Dim variableList As List(Of UTQueryKeyValuePair) = findElementsByKey(key)
            If variableList.Count Then
                Return variableList(0).value
            Else
                Return Nothing
            End If
        End Get
        Set(value As String)
            Dim varId = findElementIdByKey(key)
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
            Return convertToHashtablePacket()
        End Get
        Set(value As Hashtable)
            packetContent = convertHashtablePacketToListPacket(value)
        End Set
    End Property

    Public Function ContainsKey(key As String)
        Return findElementIdByKey(key) <> -1
    End Function

    Public Shared Narrowing Operator CType(packetHT As Hashtable) As UTQueryPacket
        Return New UTQueryPacket(packetHT)
    End Operator

    Public Shared Widening Operator CType(packet As UTQueryPacket) As Hashtable
        Return packet.convertToHashtablePacket()
    End Operator

    Public Shared Widening Operator CType(packet As UTQueryPacket) As String
        Return packet.ToString()
    End Operator

    Protected Function findElementsByKey(key As String) As List(Of UTQueryKeyValuePair)
        Dim elements As New List(Of UTQueryKeyValuePair)
        For Each variable In packetContent
            If String.Compare(variable.key, key, True) = 0 Then elements.Add(variable)
        Next
        Return elements
    End Function

    Protected Function findElementIdByKey(key As String) As Integer
        For i = 0 To packetContent.Count - 1
            If String.Compare(packetContent(i).key, key, True) = 0 Then Return i
        Next
        Return -1
    End Function

    ''' <summary>
    ''' Reassemble and parse Gamespy protocol response into a Hashtable object
    ''' </summary>
    ''' <param name="responseString">Response received from server</param>
    ''' <param name="masterServer">Set to True to skip queryid checks when talking with master server</param>
    ''' <remarks></remarks>
    Protected Function parseGamespyResponse(ByVal responseString As String, Optional masterServer As Boolean = False) As List(Of UTQueryKeyValuePair)
        Dim packetContent As New Hashtable() ' temporary array of values from currently processed packet
        Dim responseResult As New List(Of UTQueryKeyValuePair)


        Dim packetsSequence(30) As Hashtable ' used to merge all packets into a full response
        Dim responseId As Integer?
        Dim packetExpectedCount As Integer = 0
        Dim receivedCount As Integer = 0
        Dim isFinalPacket = packetFlags.HasFlag(UTQueryPacketFlags.UTQP_NoFinal)
        Dim isMultiIndex = packetFlags.HasFlag(UTQueryPacketFlags.UTQP_MultiIndex)
        Dim isMasterServer = packetFlags.HasFlag(UTQueryPacketFlags.UTQP_MasterServer)

        Try

            Dim keyName As String, value As String
            Dim packetId As Integer?

            Dim response = PacketUnserialize(responseString)

            'chunks = Split(responseString, "\")
            'For i = 1 To chunks.Count - 2 Step 2

            Dim propPrevious As UTQueryKeyValuePair = Nothing
            Dim iteration = 0
            For Each prop In response
                keyName = prop.key
                value = prop.value

                Select Case keyName
                    Case "final", "wookie"
                        If Not IsNothing(propPrevious) AndAlso propPrevious.key = "queryid" Then
                            packetExpectedCount = packetId
                        Else
                            isFinalPacket = True
                        End If
                        ' we're not finished! there might be some more content from packet that might have arrived late

                    Case "queryid"
                        If isMasterServer Then
                            Throw New UTQueryInvalidResponseException("QueryID is not expected from Master Server")
                        End If
                        Dim sequenceIdChunks As String() = value.Split(".")
                        responseId = sequenceIdChunks(0)
                        packetId = sequenceIdChunks(1)
                        'prop.value = responseId

                        'packetContent(keyName) = prop
                        receivedCount += 1
                        If isFinalPacket Then
                            If IsNothing(packetId) Then
                                'empty (but complete and valid) response
                                'TODO investigate reason for this - are there any responses without packetId like \queryid\5\ ?
                                For Each var In packetContent.Keys
                                    prop.sourcePacketId = 1
                                    responseResult.Add(prop)
                                Next
                                Return responseResult
                            End If
                            packetExpectedCount = packetId
                            isFinalPacket = False
                        End If
                        packetsSequence(packetId) = packetContent.Clone()
                        packetContent.Clear()

                    Case Else
                        If keyName = "secure" AndAlso value = "wookie" Then ' special case for master-server response
                            isFinalPacket = True
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

                iteration += 1
                propPrevious = prop
            Next

            Dim propEdited As UTQueryKeyValuePair
            Dim hasQueryId = Not IsNothing(responseId)

            If Not hasQueryId AndAlso isFinalPacket AndAlso isMasterServer Then ' handle master server response
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

            ' incomplete/malformed response checks
            If packetExpectedCount = 0 OrElse receivedCount <> packetExpectedCount Then
                If Not IsNothing(packetsSequence(packetId)) AndAlso packetsSequence(packetId).Count = 0 Then
                    ' TODO investigate
                    'packetContent("queryid") = responseId
                    For Each var In packetContent.Keys
                        propEdited.key = var
                        propEdited.value = packetContent(var)
                        propEdited.sourcePacketId = 1
                        responseResult.Add(propEdited)
                    Next
                    Return responseResult
                ElseIf packetId <> 0 Then
                    Throw New UTQueryResponseIncompleteException("Missing packets in response")
                Else
                    If isMasterServer Then
                        Throw New UTQueryResponseIncompleteException("Missing packets in response")
                    Else
                        Throw New UTQueryInvalidResponseException("Packet is missing QueryID")
                    End If
                End If
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

    Protected Function createGamespyResponse(Optional packetFlags As UTQueryPacketFlags = 0) As List(Of String)

        Dim responsePackets As New List(Of String)
        Dim currentPacket As String
        Dim currentPacketId = 0, doneSomething As Boolean
        Dim orphanPacket As String = ""
        Do
            doneSomething = False
            currentPacket = ""
            For Each variable In packetContent
                If Not packetFlags.HasFlag(UTQueryPacketFlags.UTQP_NoQueryId) AndAlso variable.key = "queryid" AndAlso queryId = 0 Then
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
                If Not packetFlags.HasFlag(UTQueryPacketFlags.UTQP_NoQueryId) Then
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
            If Not packetFlags.HasFlag(UTQueryPacketFlags.UTQP_NoQueryId) Then
                If queryId = 0 Then
                    queryId = New System.Random().Next(10, 99)
                End If
                orphanPacket &= "\queryid\" & queryId & "." & currentPacketId
            End If
            responsePackets.Add(orphanPacket)
        End If

        If Not packetFlags.HasFlag(UTQueryPacketFlags.UTQP_NoFinal) Then
            responsePackets(responsePackets.Count - 1) &= "\final\"
        End If


        Return responsePackets
    End Function

    Protected Function createGamespyResponseString(Optional packetFlags As UTQueryPacketFlags = 0) As String
        Dim list = createGamespyResponse(packetFlags)
        createGamespyResponseString = ""
        For Each packet In list
            createGamespyResponseString &= packet
        Next
    End Function

    Public Shared Function escapeGsString(str As String)
        Return str.Replace("\", "_")
    End Function

    ' IEnumerable

    Public Overloads Function GetEnumerator() As IEnumerator(Of UTQueryKeyValuePair) Implements IEnumerable(Of UTQueryKeyValuePair).GetEnumerator
        Return packetContent.GetEnumerator()
    End Function

    <FlagsAttribute()> Public Enum UTQueryPacketFlags
        UTQP_NoQueryId = 1
        UTQP_NoFinal = 2
        UTQP_ReadyToSend = 4
        UTQP_MultiIndex = 8
        UTQP_MasterServer = 16 Or UTQP_NoQueryId
        UTQP_MasterServerIpList = 16 Or UTQP_MasterServer Or UTQP_MultiIndex
        UTQP_SimpleRequest = UTQP_NoQueryId Or UTQP_NoFinal
    End Enum

    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not disposedValue Then
            If disposing Then
                packetContent = Nothing
            End If
            disposedValue = True
        End If
    End Sub


    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(disposing:=True)
        GC.SuppressFinalize(Me)
    End Sub
End Class



Public Structure UTQueryKeyValuePair
    Dim key As String
    Dim value As String
    Dim sourcePacketId As Integer
    Public Overrides Function ToString() As String
        Return "\" & UTQueryPacket.escapeGsString(Me.key) & "\" & UTQueryPacket.escapeGsString(Me.value)
    End Function
End Structure

#Region "Legacy code"

Module UTQuery
    Public Function GetHost(ByVal addr As String)
        Dim tmpx() As String
        tmpx = Split(addr, ":", 2)
        GetHost = tmpx(0)
    End Function

    Public Function GetPort(ByVal addr As String)
        Dim tmpx() As String
        tmpx = Split(addr, ":", 2)
        GetPort = tmpx(1)
    End Function



#Region "GSMSALG"
    'GSMSALG 0.3.3
    'by Luigi Auriemma
    'e-mail: aluigi@autistici.org
    'web:    aluigi.org
    'Copyright 2004,2005,2006,2007,2008 Luigi Auriemma

    'This program is free software; you can redistribute it and/or modify
    'it under the terms of the GNU General Public License as published by
    'the Free Software Foundation; either version 2 of the License, or
    '(at your option) any later version.

    'This program is distributed in the hope that it will be useful,
    'but WITHOUT ANY WARRANTY; without even the implied warranty of
    'MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    'GNU General Public License for more details.

    'You should have received a copy of the GNU General Public License
    'along with this program; if not, write to the Free Software
    'Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307 USA

    'http://www.gnu.org/licenses/gpl.txt


    Private Function gsvalfunc(ByVal reg As Byte) As Byte
        If reg < 26 Then
            gsvalfunc = reg + &H41 'A'
        ElseIf reg < 52 Then
            gsvalfunc = reg + &H47 'G'
        ElseIf reg < 62 Then
            gsvalfunc = reg - 4
        ElseIf reg = 62 Then
            gsvalfunc = &H2B '+'
        ElseIf reg = 63 Then
            gsvalfunc = &H2F '/'
        Else
            gsvalfunc = &H20 ' '
        End If
    End Function



    ''' <summary>
    ''' Generates response needed to query master servers using Gamespy protocol.
    ''' </summary>
    ''' <param name="chal">the string containing the challenge received from the server.</param>
    ''' <param name="enkey">the gamekey or any other text string used as algorithm's key, usually it is the gamekey but "might" be another thing in some cases. Each game has its unique Gamespy gamekey which are available here: http://aluigi.org/papers/gslist.cfg </param>
    ''' <returns>
    ''' the destination buffer that will contain the calculated
    ''' response. Its length is 4/3 of the challenge size so if the
    ''' challenge is 6 bytes long, the response will be 8 bytes long
    ''' plus the final NULL byte which is required (to be sure of the
    ''' allocated space use 89 bytes or "((len * 4) / 3) + 3")
    ''' if this parameter is NULL the function will allocate the
    ''' memory for a new one automatically
    ''' </returns>
    ''' <remarks></remarks>
    Public Function gsenc(ByVal chal As String, Optional ByVal enkey As String = "Z5Nfb0") As String
        Dim resultBytes(0 To 7) As Byte, tmp(66) As Byte, enctmp(0 To 255) As Byte, size As Integer, chalBytes() As Byte, enkeyBytes() As Byte, a As Integer, b As Integer, x As Integer, y As Integer, z As Integer
        Dim ti As Integer

        chalBytes = System.Text.Encoding.ASCII.GetBytes(chal)
        enkeyBytes = System.Text.Encoding.ASCII.GetBytes(enkey)
        For i = 0 To 255
            enctmp(i) = i
        Next i
        a = 0
        For i = 0 To 255
            a = ((a + enctmp(i) + enkeyBytes(i Mod Len(enkey))) And &HFF)
            x = enctmp(a)
            enctmp(a) = enctmp(i)
            enctmp(i) = x
        Next i
        a = 0
        b = 0

        For i = 0 To Len(chal) - 1
            a = (a + chalBytes(i) + 1) And 255
            x = enctmp(a)
            b = (b + x) And 255
            y = enctmp(b)
            enctmp(b) = x
            enctmp(a) = y
            tmp(i) = chalBytes(i) Xor enctmp((x + y) And 255)
            ti = i
        Next i

        size = ti + 1
        While size Mod 3 <> 0
            tmp(size) = 0
            size = size + 1
        End While
        Dim p As Integer = 0
        For i = 0 To size - 1 Step 3
            x = tmp(i)
            y = tmp(i + 1)
            z = tmp(i + 2)
            resultBytes(p) = gsvalfunc((x And &HFC) / 4)
            resultBytes(p + 1) = gsvalfunc(((x And 3) * 16) Or ((y And &HF0) / 16))
            resultBytes(p + 2) = gsvalfunc(((y And 15) * 4) Or ((z And &HC0) / 64))
            resultBytes(p + 3) = gsvalfunc(z And 63)
            p = p + 4
        Next i
        gsenc = Trim(System.Text.Encoding.ASCII.GetString(resultBytes))
    End Function
#End Region

End Module

#End Region


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
