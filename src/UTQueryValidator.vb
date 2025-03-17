' UT query protocol things
' might be also usable for other gamespy-based games


Imports System.Globalization
Imports System.Text.RegularExpressions

Public Class UTQueryValidator
    Protected fields As New List(Of UTQueryValidatorField)

    Public Sub New()
    End Sub

    Public Shared Function FromRuleDict(rules As Dictionary(Of String, String))
        Dim validator = New UTQueryValidator()
        For Each rule In rules
            validator.CreateRule(rule.Key, rule.Value)
        Next
        Return validator
    End Function

    Public Function Validate(packet As UTQueryPacket) As Hashtable
        Dim result As New Hashtable
        For Each field In fields
            Dim value

            If field.isArray Then
                value = ParseArray(packet, field)
            Else
                If Not packet.ContainsKey(field.key) AndAlso field.isRequired Then
                    Throw New UTQueryValidationException($"Missing required field `{field.key}`")
                ElseIf Not packet.ContainsKey(field.key) Then
                    Continue For
                End If
                value = ParseField(packet(field.key), field)
            End If

            result(field.key) = value
        Next
        Return result
    End Function

    Public Sub CreateRule(fieldName As String, rule As String)
        Dim field = New UTQueryValidatorField
        field.key = fieldName
        SetFieldAttributesFromValidationRules(field, rule)
        fields.Add(field)
    End Sub

    Protected Shared Sub SetFieldAttributesFromValidationRules(ByRef field As UTQueryValidatorField, rules As String)
        Dim rulesDelimited() As String = rules.Split("|")
        For Each rule In rulesDelimited
            Dim match = Regex.Match(rule, "([a-z_]+)(:(([^,]+,?))+)?")
            Dim ruleName As String = match.Groups(1).Value
            Dim ruleValue As String = match.Groups(3).Value

            Select Case ruleName
                Case "required"
                    field.isRequired = True
                Case "nullable"
                    field.isNullable = True
                Case "array"
                    field.isArray = True
                    field.valueType = GetValidatorTypeFromString(ruleValue)
                Case "default"
                    field.defaultValue = ruleValue
                Case "max", "lte"
                    field.valMax = ruleValue
                    field.valMaxExcluded = False
                Case "min", "gte"
                    field.valMin = ruleValue
                    field.valMinExcluded = False
                Case "lt"
                    field.valMax = ruleValue
                    field.valMaxExcluded = True
                Case "gt"
                    field.valMin = ruleValue
                    field.valMinExcluded = True
                Case Else
                    Dim fieldType = GetValidatorTypeFromString(ruleName)
                    If Not IsNothing(fieldType) Then
                        field.valueType = fieldType
                    End If
            End Select

        Next

    End Sub

    Protected Shared Function GetValidatorTypeFromString(typeName As String) As UTQueryValidatorValueType?
        Dim typeDef = New Dictionary(Of String, UTQueryValidatorValueType) From {
            {"string", UTQueryValidatorValueType.STR},
            {"integer", UTQueryValidatorValueType.INT},
            {"float", UTQueryValidatorValueType.FLOAT},
            {"boolean", UTQueryValidatorValueType.BOOL}
        }
        If Not typeDef.ContainsKey(typeName) Then
            Return Nothing
        End If

        Return typeDef(typeName)
    End Function

    Protected Shared Function ParseField(input As String, field As UTQueryValidatorField)
        Dim result = Nothing
        Dim fieldBoundsValue As Double

        If IsNothing(input) Then
            ' change null into empty string
            input = ""
        End If

        If input = "" AndAlso Not IsNothing(field.defaultValue) Then
            ' fill default value
            input = field.defaultValue
        End If

        If input = "" AndAlso field.isNullable Then
            ' if still empty, return if nullable
            Return Nothing
        End If

        If input = "" Then
            Throw New UTQueryValidationException($"Null is not allowed: {field.key}")
        End If

        Select Case field.valueType
            Case UTQueryValidatorValueType.STR
                result = input
                fieldBoundsValue = input.Length

            Case UTQueryValidatorValueType.INT
                Dim isValidValue = Integer.TryParse(s:=input, result:=result)
                If Not isValidValue Then
                    Throw New UTQueryValidationException($"Not an integer value: {field.key}")
                End If
                fieldBoundsValue = result

            Case UTQueryValidatorValueType.FLOAT
                Dim isValidValue = Double.TryParse(s:=input, result:=result, provider:=NumberFormatInfo.InvariantInfo)
                If Not isValidValue Then
                    Throw New UTQueryValidationException($"Not a float value: {field.key}")
                End If
                fieldBoundsValue = result

            Case UTQueryValidatorValueType.BOOL
                If input.ToLower() = "true" Then
                    result = True
                ElseIf input.ToLower() = "false" Then
                    result = False
                Else
                    Throw New UTQueryValidationException($"Not a boolean value: {field.key}")
                End If
        End Select

        If Not CheckFieldMinMax(fieldBoundsValue, field) Then
            Throw New UTQueryValidationException($"Value not in expected range: {field.key}")
        End If

        Return result
    End Function

    Protected Shared Function ParseArray(packet As UTQueryPacket, field As UTQueryValidatorField)
        Dim result As New UTQueryValidatorArray(field)
        For Each entry In packet
            If Not entry.key.StartsWith(field.key & "_") Then
                Continue For
            End If
            Dim idxStr = entry.key.Substring(entry.key.LastIndexOf("_") + 1)
            Dim idx As Integer
            If Not Integer.TryParse(idxStr, idx) Then
                Continue For
            End If
            Dim val = ParseField(entry.value, field)

            result.Add(idx, val)
        Next
        Return result
    End Function

    Protected Shared Function CheckFieldMinMax(value As Double?, field As UTQueryValidatorField) As Boolean
        If IsNothing(value) Then
            Return False
        End If

        If Not IsNothing(field.valMin) AndAlso (
                (value < field.valMin) OrElse
                (value = field.valMin AndAlso field.valMinExcluded)
            ) Then
            Return False
        ElseIf Not IsNothing(field.valMax) AndAlso (
                (value > field.valMax) OrElse
                (value = field.valMax AndAlso field.valMaxExcluded)
            ) Then
            Return False
        End If
        Return True
    End Function
End Class



Public Class UTQueryValidatorArray
    Inherits Dictionary(Of Integer, Object)

    Protected fieldDef As UTQueryValidatorField

    Public Sub New(field As UTQueryValidatorField)
        Me.fieldDef = field
    End Sub

    Default Public Overloads Property Item(key As Integer) As Object
        Get
            If Not MyBase.ContainsKey(key) AndAlso Not IsNothing(fieldDef.defaultValue) Then
                Return fieldDef.defaultValue
            End If
            If Not fieldDef.isRequired AndAlso fieldDef.isNullable Then
                Return Nothing
            End If
            Return MyBase.Item(key)
        End Get
        Set(value As Object)
            MyBase.Item(key) = value
        End Set
    End Property

End Class

Public Class UTQueryValidationException
    Inherits Exception
    Public Sub New()
    End Sub

    Public Sub New(message As String)
        MyBase.New(message)
    End Sub

    Public Sub New(message As String, inner As Exception)
        MyBase.New(message, inner)
    End Sub
End Class

Public Structure UTQueryValidatorField
    Dim key As String
    Dim valueType As UTQueryValidatorValueType
    Dim isRequired As Boolean ' field must be present, exception will be thrown otherwise
    Dim isNullable As Boolean ' field can be null
    Dim isArray As Boolean ' field appears multiple times with index suffix (eg. name_0, name_1)
    Dim defaultValue As String ' return value if validation is not satisfied; only for optional
    Dim valMin As Double? ' min value (numeric) / length (string)
    Dim valMax As Double? ' max value (numeric) / length (string)
    Dim valMinExcluded As Boolean ' turns min from >= into >
    Dim valMaxExcluded As Boolean ' turns max from <= into <
End Structure


Public Enum UTQueryValidatorValueType As Integer
    STR = 0
    INT = 1
    FLOAT = 2
    BOOL = 3
End Enum
