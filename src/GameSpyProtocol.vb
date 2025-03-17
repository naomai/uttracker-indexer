' UT query protocol things
' might be also usable for other gamespy-based games

Module GameSpyProtocol
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


    Private Function GsValFunc(ByVal reg As Byte) As Byte
        If reg < 26 Then
            Return reg + &H41 'A'
        ElseIf reg < 52 Then
            Return reg + &H47 'G'
        ElseIf reg < 62 Then
            Return reg - 4
        ElseIf reg = 62 Then
            Return &H2B '+'
        ElseIf reg = 63 Then
            Return &H2F '/'
        Else
            Return &H20 ' '
        End If
    End Function



    ''' <summary>
    ''' Generates response needed to query master servers using Gamespy protocol.
    ''' </summary>
    ''' <param name="challenge">the string containing the challenge received from the server.</param>
    ''' <param name="gamekey">the gamekey or any other text string used as algorithm's key, usually it is the gamekey but "might" be another thing in some cases. Each game has its unique Gamespy gamekey which are available here: http://aluigi.org/papers/gslist.cfg </param>
    ''' <returns>
    ''' the destination buffer that will contain the calculated
    ''' response. Its length is 4/3 of the challenge size so if the
    ''' challenge is 6 bytes long, the response will be 8 bytes long
    ''' </returns>
    ''' <remarks></remarks>
    Public Function GenerateValidateResponse(ByVal challenge As String, Optional ByVal gamekey As String = "Z5Nfb0") As String
        Dim resultBytes(0 To 7) As Byte, tmp(66) As Byte, enctmp(0 To 255) As Byte, size As Integer, chalBytes() As Byte, enkeyBytes() As Byte, a As Integer, b As Integer, x As Integer, y As Integer, z As Integer
        Dim ti As Integer

        chalBytes = System.Text.Encoding.ASCII.GetBytes(challenge)
        enkeyBytes = System.Text.Encoding.ASCII.GetBytes(gamekey)
        For i = 0 To 255
            enctmp(i) = i
        Next i
        a = 0
        For i = 0 To 255
            a = ((a + enctmp(i) + enkeyBytes(i Mod Len(gamekey))) And &HFF)
            x = enctmp(a)
            enctmp(a) = enctmp(i)
            enctmp(i) = x
        Next i
        a = 0
        b = 0

        For i = 0 To Len(challenge) - 1
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
            resultBytes(p) = GsValFunc((x And &HFC) / 4)
            resultBytes(p + 1) = GsValFunc(((x And 3) * 16) Or ((y And &HF0) / 16))
            resultBytes(p + 2) = GsValFunc(((y And 15) * 4) Or ((z And &HC0) / 64))
            resultBytes(p + 3) = GsValFunc(z And 63)
            p = p + 4
        Next i
        Return Trim(System.Text.Encoding.ASCII.GetString(resultBytes))
    End Function
End Module
