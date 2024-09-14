' UT query protocol things
' might be also usable for other gamespy-based games

Imports System
Imports System.Threading
Imports System.Text.RegularExpressions
Imports System.Text.Encoding
Imports System.Security.Cryptography

Class UTQuery2
    Public Sub New()

    End Sub


End Class

Class UTQueryPacket
    Inherits System.Collections.CollectionBase
    Implements IEnumerable(Of UTQueryKeyValuePair)
    Dim packetContent As New List(Of UTQueryKeyValuePair)
    Dim queryId As Integer = 0
    Public packetFlags As UTQueryPacketFlags

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
            'If packetHT(key).GetType.IsSubclassOf(GetType(CollectionBase)) Then
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
        packetFlags = IIf(rtsFlag, _
                          packetFlags Or UTQueryPacketFlags.UTQP_ReadyToSend, _
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
            While containsKey(key & "_" & index)
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
    ''' <param name="q">Response received from server</param>
    ''' <param name="masterServer">Set to True to skip queryid checks when talking with master server</param>
    ''' <remarks></remarks>
    Protected Function parseGamespyResponse(ByVal q As String, Optional masterServer As Boolean = False) As List(Of UTQueryKeyValuePair)
        Dim info As New Hashtable() ' temporary array of values from currently processed packet
        Dim packet As New List(Of UTQueryKeyValuePair)
        Dim currentVariable As UTQueryKeyValuePair
        Dim chunks() As String
        Dim queryarr(30) As Hashtable ' used to merge all packets into a full response
        Dim queryPacketId As Integer, queryResponseId As Integer, sqw As String()
        Dim queryin As Hashtable, qui As DictionaryEntry
        Dim errors As Integer = 0
        Dim packetCount As Integer = 0
        Dim receivedCount As Integer = 0
        Dim isFinalPacket = packetFlags.HasFlag(UTQueryPacketFlags.UTQP_NoFinal)
        Dim isMultiIndex = packetFlags.HasFlag(UTQueryPacketFlags.UTQP_MultiIndex)
        Dim isMasterServer = packetFlags.HasFlag(UTQueryPacketFlags.UTQP_MasterServer)
        Dim keyName As String = "", value As String

        If q = "" Then
            Throw New UTQueryResponseIncompleteException
        ElseIf q(0) <> "\"c Then
            Throw New UTQueryInvalidResponseException
        End If

        Try
            chunks = Split(q, "\")
            For i = 1 To chunks.Count - 2 Step 2
                keyName = LCase(chunks(i))
                value = chunks(i + 1)
                If keyName = "final" OrElse keyName = "wookie" Then
                    If i >= 2 AndAlso chunks(i - 2) = "queryid" Then
                        packetCount = queryPacketId
                    Else
                        isFinalPacket = True
                    End If
                    Continue For ' we're not finished! there might be some more content from packet that might have arrived late
                ElseIf i < chunks.Count - 1 AndAlso keyName = "secure" AndAlso value = "wookie" Then ' special case for master-server response
                    info(keyName) = value
                    isFinalPacket = True
                    Continue For
                End If
                If keyName = "queryid" Then ' we just bumped into the packet end, let's put it in correct place of queryarr

                    sqw = value.Split(".")
                    queryPacketId = sqw(1)
                    queryResponseId = sqw(0)
                    info("queryid") = queryResponseId
                    receivedCount += 1
                    If isFinalPacket Then
                        If queryPacketId = 0 Then 'empty (but complete and valid) response
                            'info("queryid") = queryResponseId
                            'Return info
                            For Each var In info.Keys
                                currentVariable.key = var
                                currentVariable.value = info(var)
                                currentVariable.sourcePacketId = 1
                                packet.Add(currentVariable)
                            Next
                            Return packet
                        End If
                        packetCount = queryPacketId
                        isFinalPacket = False
                    End If
                    queryarr(queryPacketId) = info.Clone()
                    info.Clear()

                Else
                    If isMultiIndex Then ' multi-index = has multiple keys with the same name, like \ip\123\ip\456\ip\...
                        If Not info.ContainsKey(keyName) Then
                            info(keyName) = New List(Of String)
                        End If
                        info(keyName).Add(value)
                    Else
                        info(keyName) = value
                    End If

                End If
            Next
            If queryPacketId = 0 AndAlso isFinalPacket Then 'no queryid, packet from master server?
                'Return info
                For Each var In info.Keys
                    If info(var).GetType = GetType(List(Of String)) Then
                        For Each variableIndex As String In info(var)
                            currentVariable.key = var
                            currentVariable.value = variableIndex
                            currentVariable.sourcePacketId = 1
                            packet.Add(currentVariable)
                        Next
                    Else
                        currentVariable.key = var
                        currentVariable.value = info(var)
                        currentVariable.sourcePacketId = 1
                        packet.Add(currentVariable)
                    End If
                   
                Next
                Return packet
            End If
            'info.Clear()
            If packetCount = 0 OrElse receivedCount <> packetCount Then
                If Not IsNothing(queryarr(queryPacketId)) AndAlso queryarr(queryPacketId).Count = 0 Then
                    info("queryid") = queryResponseId
                    'Return info
                    For Each var In info.Keys
                        currentVariable.key = var
                        currentVariable.value = info(var)
                        currentVariable.sourcePacketId = 1
                        packet.Add(currentVariable)
                    Next
                    Return packet
                ElseIf queryPacketId <> 0 Then
                    Throw New UTQueryResponseIncompleteException()
                Else
                    'If masterServer Then
                    If isMasterServer Then
                        Throw New UTQueryResponseIncompleteException()
                    Else
                        Throw New UTQueryInvalidResponseException()
                    End If
                End If
            End If
            ' put all the pieces in correct order
            'For Each queryin In queryarr
            Dim hasQueryId = False
            For packetId = LBound(queryarr) To UBound(queryarr)
                queryin = queryarr(packetId)
                If Not IsNothing(queryin) AndAlso queryin.Count > 0 Then
                    For Each qui In queryin
                        'info(Trim(qui.Key)) = Trim(qui.Value)
                        If qui.Key = "queryid" Then
                            If Not hasQueryId Then
                                hasQueryId = True
                            Else
                                Continue For
                            End If
                        End If

                        currentVariable.key = qui.Key
                        currentVariable.value = qui.Value
                        currentVariable.sourcePacketId = packetId
                        packet.Add(currentVariable)
                    Next
                End If
            Next
        Catch ex As IndexOutOfRangeException
            Throw New UTQueryResponseIncompleteException
        Catch ex As NullReferenceException
            Debugger.Break()
        End Try
        'info("queryid") = queryResponseId
        'Return info
        Return packet
    End Function

    Protected Function createGamespyResponse(Optional packetFlags As UTQueryPacketFlags = 0) As List(Of String)

        Dim responsePackets As New List(Of String)
        Dim currentPacket As String
        Dim currentPacketId = 1, doneSomething As Boolean
        Do
            doneSomething = False
            currentPacket = ""
            For Each variable In packetContent
                If Not packetFlags.HasFlag(UTQueryPacketFlags.UTQP_NoQueryId) AndAlso variable.key = "queryid" AndAlso queryId = 0 Then
                    queryId = variable.value
                ElseIf variable.sourcePacketId = currentPacketId Then
                    currentPacket &= variable.ToString
                    doneSomething = True
                End If
            Next
            If Not packetFlags.HasFlag(UTQueryPacketFlags.UTQP_NoQueryId) Then
                currentPacket &= "\queryid\" & queryId & "." & currentPacketId
            End If

            If Not doneSomething AndAlso Not packetFlags.HasFlag(UTQueryPacketFlags.UTQP_NoFinal) Then
                responsePackets(currentPacketId - 2) &= "\final\"
            Else
                responsePackets.Add(currentPacket)
            End If

            currentPacketId += 1
        Loop While doneSomething
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


End Class



Public Structure UTQueryKeyValuePair
    Dim key As String
    Dim value As String ' this comment is not needed temporarily, don't read;                it can be either String or List(Of String)
    Dim sourcePacketId As Integer
    Public Overrides Function ToString() As String
        'If value.GetType.Name = "String" Then
        Return "\" & UTQueryPacket.escapeGsString(Me.key) & "\" & UTQueryPacket.escapeGsString(Me.value)
        'Else
        '    ToString = ""
        '    For Each valueSub As String In value
        '        ToString &= "\" & escapeGsString(Me.key) & "\" & escapeGsString(valueSub)
        '    Next
        'End If

    End Function
End Structure

#Region "Legacy code"

Module UTQuery
    Public Structure utKey
        Dim name As String
        Dim value As String
    End Structure
    Private Declare Function GetTickCount Lib "kernel32" () As Long
    Private Declare Sub Sleep Lib "kernel32.dll" (ByVal Milliseconds As Integer)
    Public globalSock As New JulkinNet
    Public lastUtQueryState As Boolean

    Public Sub UTQueryInit()
        globalSock.timeout = 1000
        globalSock.setProto(JulkinNet.jnProt.jnUDP)
        globalSock.bind()
        'utServerQuery2("94.23.167.108:6500", "") 'testing
    End Sub

    Public Sub UTQueryFinalize()
        globalSock.Dispose()
        globalSock = Nothing
        globalSock = New JulkinNet
    End Sub


    Public Function getRawServerListProt2(gamename As String, cdkey As String, gametype As String, Optional version As Integer = 2206, Optional ByVal address As String = "master0.gamespy.com:28900") As String
        ' with some of help from: http://aluigi.altervista.org/papers/gskey-auth.txt
        ' this one's a bit different
        ' no CLIENT_TOKEN here, the second hash is just [CDKEY][SERVER_TOKEN]

        Dim lol As JulkinNet, serverToken As String, tx As Long, packet() As Byte, e As Exception
        Dim tempMd5() As Byte, cdkeyMd5 As String = "", cdkeyWithServerToken As String = ""
        Dim stringsBin As List(Of String)
        Dim md5prov = New MD5CryptoServiceProvider()
        lol = New JulkinNet
        lol.timeout = 5000
        Try

            lol.connect(address)
            packet = lol.sreadNextBytes()

            If packet.Length = 0 Then Throw New Exception("No response from " & address)
            serverToken = decodeUT2Packet(packet).Item(0)
            tempMd5 = md5prov.ComputeHash(ASCII.GetBytes(cdkey))
            For Each keyByte In tempMd5
                cdkeyMd5 &= LCase(Hex(keyByte).PadLeft(2, "0"))

            Next

            tempMd5 = md5prov.ComputeHash(ASCII.GetBytes(cdkey & serverToken))
            For Each keyByte In tempMd5
                cdkeyWithServerToken &= LCase(Hex(keyByte).PadLeft(2, "0"))
            Next
            stringsBin = New List(Of String)
            stringsBin.Add(cdkeyMd5)
            stringsBin.Add(cdkeyWithServerToken)
            stringsBin.Add("CLIENT")
            stringsBin.Add("INTINTINT" & version)
            stringsBin.Add("BYTBYTBYT5")
            stringsBin.Add("int")
            packet = encodeUT2PacketStrings(stringsBin)

            lol.swrite(packet)

            packet = lol.sreadNextBytes()

            If decodeUT2Packet(packet).Item(0) <> "APPROVED" Then Throw New Exception("Auth failed from server " & address)

            stringsBin.Clear()

            stringsBin.Add("BYTBYTBYT0")
            stringsBin.Add("BYTBYTBYT2")
            stringsBin.Add("gametype")
            stringsBin.Add(gametype)
            stringsBin.Add("BYTBYTBYT0")
            stringsBin.Add("password")
            stringsBin.Add("false")
            stringsBin.Add("BYTBYTBYT0")
        Catch e ' lazyErrorHandlingTODO

        End Try
        getRawServerListProt2 = ""
        tx = GetTickCount
        Do
            getRawServerListProt2 &= lol.sreadNext()
        Loop While GetTickCount - tx < 7000 AndAlso InStr(getRawServerListProt2, "\final\") = 0
        lol.disconnect()


    End Function

    Public Function getServerInfoRaw(ByVal ip) As String
        Dim qstr As String = "", start As Integer

        globalSock.swriteTo("\info\xserverquery", ip)
        start = GetTickCount
        Do
            qstr &= globalSock.sreadFrom(ip)
            Sleep(20)
        Loop While GetTickCount - start < 3000 AndAlso InStrRev(qstr, "\final\") = 0

        getServerInfoRaw = qstr

    End Function
    Public Function getServerPlayers(ByVal ip) As Hashtable
        Dim qstr As String = "", start As Integer

        Dim info As Hashtable

        globalSock.swriteTo("\players\xserverquery", ip)
        start = GetTickCount
        Do
            qstr &= globalSock.sreadFrom(ip)
            Sleep(50)
        Loop While GetTickCount - start < 3000 AndAlso InStrRev(qstr, "\final\") = 0
        info = parseQuery(qstr)

        getServerPlayers = info
    End Function

    'uttracker v4 will have ut3 superpowers
    Public Function utServerQuery2(ByVal ip As String, ByVal query As String) As Hashtable
        Dim qstr As String, start As Integer, challengeNum As Int32
        Dim request() As Byte

        Dim info As Hashtable
        qstr = ""
        ReDim request(6)
        request(0) = &HFE
        request(1) = &HFD
        request(2) = &H9
        request(3) = Asc("U")
        request(4) = Asc("T")
        request(5) = Asc("R")
        request(6) = Asc("K")
        globalSock.swriteTo(request, ip)

        start = GetTickCount
        Do
            qstr &= globalSock.sreadFrom(ip)
            Thread.Sleep(10)
        Loop While GetTickCount - start < 3000 AndAlso Len(qstr) = 0
        If qstr = "" OrElse qstr(0) <> Chr(9) Then
            Return New Hashtable
        End If
        challengeNum = Mid(qstr, 6)
        ReDim request(14)
        qstr = ""
        request(0) = &HFE
        request(1) = &HFD
        request(2) = &H0
        request(3) = Asc("U")
        request(4) = Asc("T")
        request(5) = Asc("R")
        request(6) = Asc("2")
        request(7) = (challengeNum >> 24) And &HFF
        request(8) = (challengeNum >> 16) And &HFF
        request(9) = (challengeNum >> 8) And &HFF
        request(10) = challengeNum And &HFF
        request(11) = &HFF
        request(12) = &HFF
        request(13) = &HFF
        request(14) = &H1

        globalSock.swriteTo(request, ip)

        start = GetTickCount
        Do
            qstr &= globalSock.sreadFrom(ip)
            Thread.Sleep(10)
        Loop While GetTickCount - start < 3000 AndAlso Len(qstr) = 0

        info = parseQuery2(Mid(qstr, 17))
        lastUtQueryState = (qstr <> "")

        Return info
    End Function

    Public Function utServerQuery(ByVal ip As String, ByVal query As String) As Hashtable
        Dim qstr As String = "", start As Integer

        Dim info As Hashtable

        globalSock.swriteTo(query, ip)

        start = GetTickCount
        Do
            qstr &= globalSock.sreadFrom(ip)
            Thread.Sleep(50)
        Loop While GetTickCount - start < 1500 AndAlso InStrRev(qstr, "\final\") = 0



        info = parseQuery(qstr)
        lastUtQueryState = (qstr <> "")

        Return info
    End Function

    Public Function utQ(ByRef ar As Hashtable, ByVal key As String, Optional ByVal index As Long = -1) As String
        If index <> -1 Then key &= "_" & index
        If Not ar.ContainsKey(LCase(key)) Then Return ""
        Return ar(LCase(key))
    End Function

    Function utGetServerProperty(ip As String, type As String, propname As String, Optional returnType As String = "s", Optional defaultVal As String = "")
        Dim res = Nothing
        Dim plx = utServerQuery(ip, "\" & type & "_property\" & propname & "\")
        utGetServerProperty = utQ(plx, propname)
        If returnType = "s" Then
            Exit Function
        ElseIf returnType = "i" AndAlso Long.TryParse(utGetServerProperty, res) Then
            Return res
        ElseIf returnType = "f" AndAlso Single.TryParse(utGetServerProperty, res) Then
            Return res
        End If
        utGetServerProperty = defaultVal

    End Function

    ''' <summary>
    ''' Reassemble and parse Gamespy protocol response into a Hashtable object
    ''' </summary>
    ''' <param name="q">Response received from server</param>
    ''' <remarks></remarks>
    Function parseQuery(ByVal q As String, Optional masterServer As Boolean = False) As Hashtable
        Dim info As New Hashtable() ' temporary array of values from currently processed packet
        Dim chunks() As String
        Dim queryarr(30) As Hashtable ' used to merge all packets into a full response
        Dim queryid As Integer, queridMajor As Integer, sqw As String()
        Dim queryin As Hashtable, qui As DictionaryEntry
        Dim errors As Integer = 0
        Dim packetCount As Integer = 0
        Dim receivedCount As Integer = 0
        Dim isFinalPacket = False
        If q = "" Then
            Throw New UTQueryResponseIncompleteException
        ElseIf q(0) <> "\"c Then
            Throw New UTQueryInvalidResponseException
        End If

        Try
            chunks = Split(q, "\")

            For i = 1 To chunks.Count - 2 Step 2
                If chunks(i) = "final" OrElse chunks(i) = "wookie" Then
                    If i >= 2 AndAlso chunks(i - 2) = "queryid" Then
                        packetCount = queryid
                    Else
                        isFinalPacket = True
                    End If
                    Continue For ' we're not finished! there might be some more content from packet that might have arrived late
                ElseIf i < chunks.Count - 1 AndAlso chunks(i) = "secure" AndAlso chunks(i + 1) = "wookie" Then ' special case for master-server response
                    info(LCase(chunks(i))) = chunks(i + 1)
                    isFinalPacket = True
                    Continue For
                End If
                If (LCase(chunks(i))) = "queryid" Then ' we just bumped into the packet end, let's put it in correct place of queryarr

                    sqw = chunks(i + 1).Split(".")
                    If Not IsNumeric(sqw(1)) Then ' ugly workaround for a bug in JulkinNet which in rare cases spits strings like: \queryid\60.1dfdsf\fd
                        queryid = 18 + errors
                        errors += 1
                    Else
                        queryid = sqw(1)
                        queridMajor = sqw(0)
                    End If
                    queryarr(queryid) = info.Clone()
                    receivedCount += 1
                    info.Clear()
                    If isFinalPacket Then
                        If queryid = 0 Then 'empty (but complete and valid) response
                            info("queryid") = queridMajor
                            Return info
                        End If
                        packetCount = queryid
                        isFinalPacket = False
                    End If
                Else
                    info(LCase(chunks(i))) = chunks(i + 1)
                End If
            Next
            If queryid = 0 AndAlso isFinalPacket Then 'no queryid, packet from master server?
                Return info
            End If
            info.Clear()
            If packetCount = 0 OrElse receivedCount <> packetCount Then
                If Not IsNothing(queryarr(queryid)) AndAlso queryarr(queryid).Count = 0 Then
                    info("queryid") = queridMajor
                    Return info
                ElseIf queryid <> 0 Then
                    Throw New UTQueryResponseIncompleteException()
                Else
                    If masterServer Then
                        Throw New UTQueryResponseIncompleteException()
                    Else
                        Throw New UTQueryInvalidResponseException()
                    End If
                End If
            End If
            ' put all the pieces in correct order
            For Each queryin In queryarr
                If Not IsNothing(queryin) AndAlso queryin.Count > 0 Then
                    For Each qui In queryin
                        info(Trim(qui.Key)) = Trim(qui.Value)

                    Next
                End If
            Next
        Catch ex As IndexOutOfRangeException
            Throw New UTQueryResponseIncompleteException
        Catch ex As NullReferenceException
            Debugger.Break()
        End Try
        info("queryid") = queridMajor
        Return info
    End Function

    'STUB; same as above, but for ut3... 
    Function parseQuery2(ByVal q As String) As Hashtable

        Return Nothing
    End Function

    Public Function getIp(ByVal addr As String)
        Dim tmpx() As String
        tmpx = Split(addr, ":", 2)
        getIp = tmpx(0)
    End Function

    Public Function getPort(ByVal addr As String)
        Dim tmpx() As String
        tmpx = Split(addr, ":", 2)
        getPort = tmpx(1)
    End Function

    Public Sub parseUTMasterList(ByRef ar As List(Of String), ByRef list As String)
        ' UTQueryPacket way:
        Dim packet = New UTQueryPacket(list, UTQueryPacket.UTQueryPacketFlags.UTQP_MasterServerIpList)
        For Each ipEntry As UTQueryKeyValuePair In packet
            If ipEntry.key = "ip" Then
                ar.Add(ipEntry.value)
            End If
        Next

        ' UTTSS2 way:
        'Dim chunks() As String
        ''ar.Clear()
        'chunks = Split(list, "\")
        'For i = 1 To chunks.Count Step 2
        '    If chunks(i) = "ip" Then
        '        ar.Add(chunks(i + 1))
        '    ElseIf chunks(i) = "final" Then
        '        Return
        '    End If
        'Next

        ' UTTSS1 (VB6) way:
        'Dim ptr As Integer
        'Dim tmp As String
        'Dim fetchIp As Boolean
        'fetchIp = False
        'tmp = ""
        'ptr = 0
        'For i = 1 To Len(list)
        '    If fetchIp Then
        '        If Mid(list, i, 1) = "\" Then
        '            'ar(ptr) = tmp
        '            ar.Add(tmp)
        '            ptr = ptr + 1
        '            tmp = ""
        '            fetchIp = False
        '        Else
        '            tmp = tmp & Mid(list, i, 1)
        '        End If
        '    End If
        '    If Mid(list, i, 4) = "\ip\" Then
        '        fetchIp = True
        '        i = i + 3
        '    Else

        '    End If
        'Next i
    End Sub


    Public Function serializename(ByVal nm)
        Dim news As String = ""
        Dim cx As Byte
        For i = 1 To Len(nm)
            cx = cn(nm, i)
            If (cx >= 48 And cx <= 57) Or (cx >= 65 And cx <= 90) Or (cx >= 97 And cx <= 122) Or cx = 61 Or cx = 91 Or cx = 93 Or cx = 95 Or cx = 123 Or cx = 125 Or cx = 40 Or cx = 41 Or (cx >= 43 And cx <= 46) Or cx = 32 Or cx = 33 Or cx = 36 Or cx = 38 Or cx = 39 Then
                news = news & Chr(cx)
            Else
                news = news & "%" & Hex(cx)
            End If
        Next i
        serializename = news
    End Function

    Function cn(ByVal s, ByVal n)
        cn = Asc(Mid(s, n, 1))
    End Function

    Function gint32(s)
        gint32 = (Asc(s(4)) << 24) Or (Asc(s(3)) << 16) Or (Asc(s(2)) << 8) Or (Asc(s(1)))
    End Function

    Function encodeUT2String(str As String) As Byte()
        Dim result(0 To Len(str) + 2) As Byte

        BitConverter.GetBytes(UInt16.Parse(Len(str) + 1)).CopyTo(result, 0)
        ASCII.GetBytes(str).CopyTo(result, 2)
        result(Len(str) + 3) = 0
        Return result
    End Function

    Function decodeUT2Packet(bytes() As Byte) As List(Of String)
        Dim dataSize As UInt32
        Dim pos As Integer = 4
        Dim result As New List(Of String)
        Dim strLen As UInt16
        dataSize = BitConverter.ToUInt32(bytes, 0)
        Do
            strLen = bytes(pos)
            'result.Add(BitConverter.ToString(bytes, pos + 1, strLen - 1))
            result.Add(ASCII.GetString(bytes, pos + 1, strLen - 1))
            pos += strLen + 1
        Loop While pos < bytes.Length
        Return result
    End Function

    Function decodeUT2PacketArray(bytes() As Byte) As Dictionary(Of String, String)
        Dim dataSize As UInt32
        Dim pos As Integer = 4
        Dim result As New List(Of String)
        'Dim strLen As UInt16
        dataSize = BitConverter.ToUInt32(bytes, 0)
        decodeUT2PacketArray = Nothing
    End Function

    Function encodeUT2Packet(pkt() As Byte) As Byte()
        Dim result() As Byte
        Dim dataSize As UInt32 = pkt.Length

        ReDim result(dataSize + 3)

        BitConverter.GetBytes(dataSize).CopyTo(result, 0)
        pkt.CopyTo(result, 4)

        Return result
    End Function

    Function encodeUT2PacketStrings(strings As List(Of String)) As Byte()
        Dim result() As Byte
        Dim dataSize As UInt32 = 0
        Dim pos As Integer = 4
        For Each s In strings
            If InStr(s, "INTINTINT") = 1 Then
                dataSize += 4
            ElseIf InStr(s, "BYTBYTBYT") = 1 Then
                dataSize += 1
            Else
                dataSize += Len(s) + 2
            End If
        Next
        ReDim result(dataSize + 3)

        BitConverter.GetBytes(dataSize).CopyTo(result, 0)

        For Each s In strings
            If InStr(s, "INTINTINT") = 1 Then
                BitConverter.GetBytes(CLng(Mid(s, 10))).CopyTo(result, pos)
                pos += 4
            ElseIf InStr(s, "BYTBYTBYT") = 1 Then
                result(pos) = CByte(Mid(s, 10))
                pos += 1
            Else
                result(pos) = Len(s) + 1
                ASCII.GetBytes(s).CopyTo(result, pos + 1)
                result(pos + Len(s) + 1) = 0
                pos += Len(s) + 2
            End If
        Next

        Return result
    End Function

    Private Function swapByteOrder(ByVal v As Int32)
        Dim byteArr As Byte()

        byteArr = BitConverter.GetBytes(v)
        Array.Reverse(byteArr)
        Return BitConverter.ToInt32(byteArr, 0)
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

End Class

Public Class UTQueryInvalidResponseException
    Inherits Exception

End Class
