Imports System

Namespace VbNetSample
    Public Class TestService
        Implements ITestInterface

        Private _value As Integer

        Public Sub New(value As Integer)
            _value = value
        End Sub

        Public Sub DoWork() Implements ITestInterface.DoWork
            Console.WriteLine(_value)
        End Sub

        Public Function GetValue() As Integer Implements ITestInterface.GetValue
            Return _value
        End Function

        Private Function Helper(x As Integer) As Integer
            If x > 0 Then
                Return x * 2
            Else
                Return 0
            End If
        End Function
    End Class
End Namespace
