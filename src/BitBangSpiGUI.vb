﻿'File:          BitBangSpiGUI.vb
'Author:        Alex Nolan (alex.nolan@analog.com)
'Date:          9/23/2019
'Description:   Bit bang SPI traffic to a DUT. Allows for robustness testing of the SPI interface.

Imports FX3Api

Public Class BitBangSpiGUI
    Inherits FormBase

    Private m_AppGUI As AppBrowseGUI

    Private Sub BitBangSpiGUI_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        m_TopGUI.FX3.BitBangSpiConfig = New BitBangSpiConfig(True)
        csLag.Text = m_TopGUI.FX3.BitBangSpiConfig.CSLagTicks.ToString()
        csLead.Text = m_TopGUI.FX3.BitBangSpiConfig.CSLeadTicks.ToString()
        stallTicks.Text = 10.0
    End Sub

    Friend Sub SetAppGUI(AppGUI As AppBrowseGUI)
        m_AppGUI = AppGUI
    End Sub

    Private Sub Shutdown() Handles Me.Closing
        'restore hardware SPI
        m_TopGUI.FX3.RestoreHardwareSpi()
        m_AppGUI.btn_BitBangSpi.Enabled = True
    End Sub

    Private Sub btn_Transfer_Click(sender As Object, e As EventArgs) Handles btn_Transfer.Click

        Dim transfers, bptransfer As UInteger
        Dim MOSI As New List(Of Byte)
        Dim MISO As Byte()
        Dim MISOStr As String
        Try
            transfers = Convert.ToUInt32(numTransfers.Text)
            bptransfer = Convert.ToUInt32(bitsPerTransfer.Text)
            m_TopGUI.FX3.SetBitBangSpiFreq(Convert.ToDouble(sclk_freq.Text))
            m_TopGUI.FX3.SetBitBangStallTime(Convert.ToDouble(stallTicks.Text))
            m_TopGUI.FX3.BitBangSpiConfig.CSLagTicks = Convert.ToUInt16(csLag.Text)
            m_TopGUI.FX3.BitBangSpiConfig.CSLeadTicks = Convert.ToUInt16(csLead.Text)
            Dim byteStr As String
            For i As Integer = 0 To MOSIData.Text.Length() - 1 Step 2
                byteStr = MOSIData.Text.Substring(i, 2)
                MOSI.Add(Convert.ToByte(byteStr, 16))
            Next
            MISO = m_TopGUI.FX3.BitBangSpi(bptransfer, transfers, MOSI.ToArray(), 5000)
            MISOStr = ""
            For Each value In MISO
                MISOStr = MISOStr + value.ToString("X2")
            Next
            MISOData.Text = MISOStr
        Catch ex As Exception
            MsgBox("ERROR: Invalid settings. " + ex.Message())
        End Try

    End Sub

    Private Sub btn_restoreSpi_Click(sender As Object, e As EventArgs) Handles btn_restoreSpi.Click
        m_TopGUI.FX3.RestoreHardwareSpi()
    End Sub

    Private Sub RestoreSPI() Handles Me.LostFocus
        m_TopGUI.FX3.RestoreHardwareSpi()
    End Sub
End Class