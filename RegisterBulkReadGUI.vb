﻿'File:          RegisterBulkReadGUI.vb
'Author:        Alex Nolan (alex.nolan@analog.com)
'Date:          7/25/2019
'Description:   Allows for bulk data logging from registers. Can be triggered on a data ready signal for IMUs

Imports AdisApi
Imports System.ComponentModel
Imports StreamDataLogger

Public Class RegisterBulkReadGUI
    Inherits FormBase

    Private WithEvents fileManager As Logger
    Private totalDRCaptures As Integer = 0
    Private pin As IPinObject

    Public Sub FormSetup() Handles Me.Load
        For Each reg In m_TopGUI.RegMap
            RegisterList.Items.Add(reg.Label)
        Next
        RegisterList.SelectedIndex = 0

        DRDIO.Items.Add("DIO1")
        DRDIO.Items.Add("DIO2")
        DRDIO.Items.Add("DIO3")
        DRDIO.Items.Add("DIO4")

        If m_TopGUI.FX3.ReadyPin.ToString = m_TopGUI.FX3.DIO1.ToString Then
            DRDIO.SelectedItem = "DIO1"
        ElseIf m_TopGUI.FX3.ReadyPin.ToString = m_TopGUI.FX3.DIO2.ToString Then
            DRDIO.SelectedItem = "DIO2"
        ElseIf m_TopGUI.FX3.ReadyPin.ToString = m_TopGUI.FX3.DIO3.ToString Then
            DRDIO.SelectedItem = "DIO3"
        ElseIf m_TopGUI.FX3.ReadyPin.ToString = m_TopGUI.FX3.DIO4.ToString Then
            DRDIO.SelectedItem = "DIO4"
        End If

        MeasureDR.Enabled = m_TopGUI.FX3.DrActive
        DrActiveBox.Checked = m_TopGUI.FX3.DrActive

        selectedRegview.View = View.Details
        selectedRegview.Columns.Add("Register", selectedRegview.Width - 1, HorizontalAlignment.Left)
        Label4.Text = ""
        StreamingAVARCancelButton.Enabled = False
        statusLabel.Text = "Waiting"
        statusLabel.BackColor = Color.White

        selectedRegview.Items.Clear()
        For Each item In m_TopGUI.BulkRegList
            selectedRegview.Items.Add(item)
        Next
        NumberDRToCapture.Text = m_TopGUI.numRegSamples.ToString()
        SamplesPerWrite.Text = m_TopGUI.samplesPerWrite.ToString()
        linesPerFile.Text = m_TopGUI.linesPerFile.ToString()
        UpdateRegCountLabel()
    End Sub

    Private Sub ReturnToMain(sender As Object, e As EventArgs) Handles Me.Closing

        'Save the list-view contents
        m_TopGUI.BulkRegList.Clear()
        For Each item In selectedRegview.Items
            m_TopGUI.BulkRegList.Add(item)
        Next

        'sample settings
        m_TopGUI.numRegSamples = Convert.ToInt32(NumberDRToCapture.Text)
        m_TopGUI.linesPerFile = Convert.ToInt32(linesPerFile.Text)
        m_TopGUI.samplesPerWrite = Convert.ToInt32(SamplesPerWrite.Text)

    End Sub

    Private Sub AddRegisterButton_Click(sender As Object, e As EventArgs) Handles AddRegisterButton.Click
        Dim newItem As New ListViewItem()
        newItem.SubItems(0).Text = RegisterList.SelectedItem
        selectedRegview.Items.Add(newItem)
        UpdateRegCountLabel()
    End Sub

    Private Sub RemoveRegisterButton_Click(sender As Object, e As EventArgs) Handles RemoveRegisterButton.Click
        If IsNothing(Me.selectedRegview.FocusedItem) Then
            MessageBox.Show("Please select an Item to Delete", "Remove register warning", MessageBoxButtons.OK)
        Else
            Me.selectedRegview.Items.RemoveAt(Me.selectedRegview.FocusedItem.Index)
        End If
        UpdateRegCountLabel()
    End Sub

    Private Sub ClearAllButton_Click(sender As Object, e As EventArgs) Handles ClearAllButton.Click
        Dim result As Integer
        result = MessageBox.Show("Are you sure you want to clear all registers?", "Clear all registers?", MessageBoxButtons.YesNo)
        If result = DialogResult.Yes Then
            selectedRegview.Items.Clear()
        End If
        UpdateRegCountLabel()
    End Sub

    Private Sub UpdateRegCountLabel()
        regStreamingList.Text = "Register Streaming List (" + selectedRegview.Items.Count.ToString() + ")"
    End Sub

    Private Sub MainButton_Click(sender As Object, e As EventArgs) Handles MainButton.Click

        Dim savePath As String
        Dim DrFreq As Double = Double.PositiveInfinity

        Dim timeString As String = "_" + DateTime.Now().ToString("s")
        timeString = timeString.Replace(":", "-")

        'Update the data ready pin and measurement
        UpdateDRPin()

        'Check whether user selected registers to stream
        If selectedRegview.Items.Count <= 0 Then
            MessageBox.Show("Please select registers to stream before starting", "No registers selected!", MessageBoxButtons.OK)
            Exit Sub
        End If

        'Check whether the number of samples to capture is valid
        If NumberDRToCapture.Text <= 0 Then
            MessageBox.Show("Please enter a number of samples to capture before starting", "No number of samples entered!", MessageBoxButtons.OK)
            Exit Sub
        End If

        'Check whether the measured DR is valid
        If m_TopGUI.FX3.DrActive Then
            If m_TopGUI.FX3.MeasurePinFreq(pin, 1, 5000, 2) > 10000 Or m_TopGUI.FX3.MeasurePinFreq(pin, 1, 5000, 2) < 0 Then
                MessageBox.Show("Data ready frequency invalid. Is the correct DIO selected?", "Invalid Data Ready!", MessageBoxButtons.OK)
                Exit Sub
            End If
        End If

        'Build list of registers to stream
        Dim regList As New List(Of RegMapClasses.RegClass)
        For Each item As ListViewItem In Me.selectedRegview.Items
            regList.Add(m_TopGUI.RegMap(item.Text))
        Next

        'Check the time it will take to capture each frame and ask the user if it exceeds the DR period
        If m_TopGUI.FX3.DrActive Then
            Dim drPeriod As Double = 1 / m_TopGUI.FX3.MeasurePinFreq(pin, 1, 5000, 2)
            Dim num16bitregs As Integer = 0
            For Each reg In regList
                If reg.NumBytes = 1 Or reg.NumBytes = 2 Then
                    num16bitregs += 1
                Else
                    num16bitregs += 2
                End If
            Next
            Dim calcPeriod As Double = ((m_TopGUI.FX3.StallTime / 1000000) + 17 / m_TopGUI.FX3.SclkFrequency) * num16bitregs
            'remove last stall time
            calcPeriod = calcPeriod - (m_TopGUI.FX3.StallTime / 1000000)

            If calcPeriod > drPeriod Then
                Dim result1 As DialogResult = MessageBox.Show("Register capture time exceeds data ready period. Would you like to continue?", "Data will take too long to read!", MessageBoxButtons.YesNo)
                If result1 = DialogResult.No Then
                    Exit Sub
                End If
            End If
        End If

        'Get data output save location
        savePath = setSaveLocation(m_TopGUI.lastFilePath)
        If savePath Is Nothing Then
            MessageBox.Show("Please select a folder to save the stream data.", "Invalid save path!", MessageBoxButtons.OK)
            Exit Sub
        End If

        'Set up file manager
        fileManager = New Logger(m_TopGUI.FX3, m_TopGUI.Dut)
        fileManager.RegList = regList
        fileManager.FileBaseName = "RegStream" + timeString
        fileManager.FilePath = savePath + "\"
        fileManager.Buffers = totalDRCaptures 'Number of times to read all the registers in the reg map
        fileManager.Captures = 1 'Number of times to read each register in the reg map
        Try
            fileManager.FileMaxDataRows = Convert.ToInt32(linesPerFile.Text()) 'Keep this under 1M samples to open in Excel
            fileManager.BuffersPerWrite = Convert.ToInt32(SamplesPerWrite.Text) 'Dynamic buffers per write to avoid storing too much data in RAM
        Catch ex As Exception
            fileManager.FileMaxDataRows = 1000000
            fileManager.BuffersPerWrite = 10000
            SamplesPerWrite.Text = "10000"
            linesPerFile.Text = "1000000"
            MsgBox("ERROR: Invalid settings entered, using default")
        End Try
        fileManager.BufferTimeoutSeconds = 10 'Timeout in seconds

        fileManager.RunAsync()

        statusLabel.Text = "Writing Data"
        statusLabel.BackColor = Color.White

        'Disable user inputs during capture
        StreamingAVARCancelButton.Enabled = True
        DRDIO.Enabled = False
        NumberDRToCapture.Enabled = False
        MeasureDR.Enabled = False
        AddRegisterButton.Enabled = False
        RemoveRegisterButton.Enabled = False
        ClearAllButton.Enabled = False
        RegisterList.Enabled = False
        selectedRegview.Enabled = False
        MainButton.Enabled = False
        SamplesPerWrite.Enabled = False
        linesPerFile.Enabled = False

    End Sub

    Private Sub CaptureComplete() Handles fileManager.RunAsyncCompleted
        Me.Invoke(New MethodInvoker(AddressOf DoneWork))
    End Sub

    Private Sub DoneWork()
        statusLabel.Text = "Done"
        statusLabel.BackColor = Color.Green
        StreamingAVARCancelButton.Enabled = False
        DRDIO.Enabled = True
        NumberDRToCapture.Enabled = True
        MeasureDR.Enabled = m_TopGUI.FX3.DrActive
        AddRegisterButton.Enabled = True
        RemoveRegisterButton.Enabled = True
        ClearAllButton.Enabled = True
        RegisterList.Enabled = True
        selectedRegview.Enabled = True
        MainButton.Enabled = True
        SamplesPerWrite.Enabled = True
        linesPerFile.Enabled = True
    End Sub

    Private Sub CancelButton_Click(sender As Object, e As EventArgs) Handles StreamingAVARCancelButton.Click
        MainButton.Enabled = True
        If fileManager.Busy Then
            fileManager.CancelAsync()
            statusLabel.Text = "Canceling"
            statusLabel.BackColor = Color.Red
        End If
    End Sub

    Private Sub MeasureDR_Click(sender As Object, e As EventArgs) Handles MeasureDR.Click
        UpdateDRPin()
        Label4.Text = FormatNumber(m_TopGUI.FX3.MeasurePinFreq(pin, 1, 5000, 2), 3).ToString + "  Hz"
    End Sub

    Private Sub UpdateDRPin()
        Dim dio As String
        dio = DRDIO.SelectedItem
        Select Case dio
            Case "DIO1"
                pin = m_TopGUI.FX3.DIO1
            Case "DIO2"
                pin = m_TopGUI.FX3.DIO2
            Case "DIO3"
                pin = m_TopGUI.FX3.DIO3
            Case "DIO4"
                pin = m_TopGUI.FX3.DIO4
            Case Else
                pin = m_TopGUI.FX3.DIO1
        End Select
    End Sub

    Private Sub progressUpdate(e As ProgressChangedEventArgs) Handles fileManager.ProgressChanged
        Me.Invoke(New MethodInvoker(Sub() CaptureProgressStreaming.Value = e.ProgressPercentage))
    End Sub

    Private Sub UpdateGUI()
        'Check DR capture input
        If NumberDRToCapture.Text = "" Then
            NumberDRToCapture.Text = 0
        End If
        Try
            totalDRCaptures = Convert.ToInt32(NumberDRToCapture.Text)
        Catch ex As Exception
            MsgBox("ERROR: Invalid Input")
            Exit Sub
        End Try
    End Sub

    Private Sub NumberDRToCapture_TextChanged(sender As Object, e As EventArgs) Handles NumberDRToCapture.TextChanged
        UpdateGUI()
    End Sub

    Private Sub DrActiveBox_CheckedChanged(sender As Object, e As EventArgs) Handles DrActiveBox.CheckedChanged
        m_TopGUI.FX3.DrActive = DrActiveBox.Checked
        MeasureDR.Enabled = m_TopGUI.FX3.DrActive
    End Sub

    Private Sub btn_loadregs_Click(sender As Object, e As EventArgs) Handles btn_loadregs.Click
        Dim browser As New OpenFileDialog()
        If browser.ShowDialog() = DialogResult.OK Then
            LoadRegsFromFile(browser.FileName)
        Else
            MsgBox("ERROR: No file selected")
            Exit Sub
        End If
    End Sub

    Private Sub LoadRegsFromFile(path As String)
        Dim regs() As String
        Try
            regs = System.IO.File.ReadAllLines(path)
        Catch ex As Exception
            MsgBox("ERROR: Unable to load file. " + ex.Message())
            Exit Sub
        End Try

        selectedRegview.Items.Clear()
        For Each reg In regs
            If m_TopGUI.RegMap.Contains(reg) Then
                selectedRegview.Items.Add(New ListViewItem() With {.Text = reg})
            Else
                MsgBox("ERROR: Register " + reg + " not found in register map!")
            End If
        Next

        'update register count
        UpdateRegCountLabel()
    End Sub

    Private Sub btn_saveregs_Click(sender As Object, e As EventArgs) Handles btn_saveregs.Click
        Dim regs As New List(Of String)
        For Each item As ListViewItem In selectedRegview.Items
            regs.Add(item.Text)
        Next

        If regs.Count() > 0 Then
            saveCSV("RegList", regs.ToArray())
        Else
            MsgBox("ERROR: No register to save")
        End If

    End Sub

End Class