﻿' Licensed to the .NET Foundation under one or more agreements. The .NET Foundation licenses this file to you under the MIT license. See the LICENSE.md file in the project root for more information.

Imports Microsoft.VisualStudio.Shell.Interop

Namespace Microsoft.VisualStudio.Editors.AppDesDesignerFramework

    ''' <summary>
    ''' Provide a convenient way to check out/query edit a set of files. 
    ''' </summary>
    Public NotInheritable Class SourceCodeControlManager

#Region "Private fields"
        ' Service provider used to get services
        Private ReadOnly _serviceProvider As IServiceProvider

        ' A map of file names to manage
        Private ReadOnly _managedFiles As New Dictionary(Of String, Boolean)(3, StringComparer.OrdinalIgnoreCase)

#End Region

#Region "Constructor(s)"
        ''' <summary>
        ''' Create a new instance of the SourceCodeControlManager
        ''' 
        ''' The service provider will be queried for 
        '''   SVsQueryEditQuerySave (falling back to IVsTextManager)
        '''   
        ''' </summary>
        ''' <param name="sp"></param>
        Public Sub New(sp As IServiceProvider, Hierarchy As IVsHierarchy)
            Requires.NotNull(sp)

            _serviceProvider = sp
        End Sub
#End Region

#Region "Manage the set of files that we are responsible for..."

        ''' <summary>
        ''' Add a file to manage SCC status for. 
        ''' </summary>
        ''' <param name="mkDocument"></param>
        Public Sub ManageFile(mkDocument As String)
            _managedFiles(mkDocument) = True
        End Sub

        ''' <summary>
        ''' Remove the specified file from the set of files we are monitoring...
        ''' </summary>
        ''' <param name="mkDocument"></param>
        ''' <remarks>
        ''' It is OK to pass in the name of a file that isn't currently managed...
        ''' </remarks>
        Public Sub StopManagingFile(mkDocument As String)
            Debug.WriteLineIf(AppDesCommon.Switches.MSVBE_SCC.TraceInfo, String.Format("Stop managing {0}'s SCC status", mkDocument))
            _managedFiles.Remove(mkDocument)
        End Sub

        ''' <summary>
        ''' Get a list of the files currently managed by this service...
        ''' </summary>
        Public Property ManagedFiles As List(Of String)
            Get
                Return New List(Of String)(_managedFiles.Keys)
            End Get
            Set
                _managedFiles.Clear()
                For Each file As String In value
                    _managedFiles(file) = True
                Next
            End Set
        End Property

#End Region

#Region "Methods to query/set the editable state for the managed files"
        ''' <summary>
        ''' Make sure that all files managed by this instance is editable (usually by 
        ''' checking them out if under SCC)
        ''' </summary>
        ''' <remarks>
        ''' Will throw CheckoutExceptions is checkout fails...
        ''' </remarks>
        Public Sub EnsureFilesEditable()
            QueryEditableFilesInternal(False, True)
        End Sub

        ''' <summary>
        ''' Query if all the files are editable. Will not prompt the user - will only report
        ''' if it is OK to edit the file. 
        ''' </summary>
        Public Function AreFilesEditable() As Boolean
            Return QueryEditableFilesInternal(True, False)
        End Function
#End Region

#Region "Private helper methods"

        ''' <summary>
        ''' Check if it is ok to edit all the managed files...
        ''' </summary>
        ''' <param name="checkOnly">If true, only query if it is OK to edit all managed files without actually checking anything out</param>
        ''' <param name="throwOnFailure">If the method should throw a CheckoutException on failure</param>
        Private Function QueryEditableFilesInternal(checkOnly As Boolean, throwOnFailure As Boolean) As Boolean
            ' Do actual checkout here...
            Return QueryEditableFiles(_serviceProvider, ManagedFiles, throwOnFailure, checkOnly)
        End Function
#End Region

#Region "Public shared helper methods"

        ''' <summary>
        ''' Query/make sure that the given set of files are editable
        ''' </summary>
        ''' <param name="sp"></param>
        ''' <param name="files">The list of files to check edit status for</param>
        ''' <param name="checkOnly">Only check if it is OK to edit files (don't actually check out)</param>
        ''' <param name="throwOnFailure">If true, failure to check out will throw checkout exception</param>
        ''' <remarks>Disallows in memory edits for IVsQueryEditQuerySave2</remarks>
        Public Shared Function QueryEditableFiles(sp As IServiceProvider, files As List(Of String), throwOnFailure As Boolean, checkOnly As Boolean) As Boolean
            Dim dummy As Boolean
            Return QueryEditableFiles(sp, files, throwOnFailure, checkOnly, dummy)
        End Function

        ''' <summary>
        ''' Query/make sure that the given set of files are editable
        ''' </summary>
        ''' <param name="sp"></param>
        ''' <param name="files">The list of files to check edit status for</param>
        ''' <param name="checkOnly">Only check if it is OK to edit files (don't actually check out)</param>
        ''' <param name="throwOnFailure">If true, failure to check out will throw checkout exception</param>
        ''' <param name="fileReloaded">Out: Set to true if one or more files were reloaded...</param>
        ''' <remarks>Disallows in memory edits for IVsQueryEditQuerySave2</remarks>
        Public Shared Function QueryEditableFiles(sp As IServiceProvider, files As List(Of String), throwOnFailure As Boolean, checkOnly As Boolean, ByRef fileReloaded As Boolean, Optional allowInMemoryEdits As Boolean = True, Optional allowFileReload As Boolean = True) As Boolean
            Requires.NotNull(sp)
            Requires.NotNull(files)

            If files.Count = 0 Then
                Return True
            End If

            ' Clear out parameters
            fileReloaded = False

            Dim qEdit2 As IVsQueryEditQuerySave2
            qEdit2 = TryCast(sp.GetService(GetType(SVsQueryEditQuerySave)), IVsQueryEditQuerySave2)

            If qEdit2 IsNot Nothing Then
                Dim filesToCheckOut(files.Count - 1) As String
                files.CopyTo(filesToCheckOut, 0)

                Dim editVerdict As UInteger
                Dim result As UInteger
                Dim rgrf(files.Count - 1) As UInteger

                Dim flags As UInteger = 0

                If checkOnly Then flags = flags Or CUInt(tagVSQueryEditFlags.QEF_ReportOnly)
                If Not allowInMemoryEdits Then flags = flags Or CUInt(tagVSQueryEditFlags.QEF_DisallowInMemoryEdits)

                Dim hr As Integer = qEdit2.QueryEditFiles(flags, filesToCheckOut.Length, filesToCheckOut, rgrf, Nothing, editVerdict, result)
                VSErrorHandler.ThrowOnFailure(hr)

                Dim success As Boolean = editVerdict = CUInt(tagVSQueryEditResult.QER_EditOK)

                ' If this was reloaded, we better add it to the list of reloaded files...
                If (result And tagVSQueryEditResultFlags2.QER_Reloaded) = tagVSQueryEditResultFlags2.QER_Reloaded Then
                    fileReloaded = True
                End If

                If success AndAlso (allowFileReload OrElse Not fileReloaded) Then
                    Return True
                Else
                    If throwOnFailure Then
                        ' Failed the checkout.  We need to throw a checkout exception, but we should 
                        ' check to see if the failure happened because the user canceled.
                        '
                        If Not allowFileReload AndAlso fileReloaded Then
                            Throw New ComponentModel.Design.CheckoutException(My.Resources.Designer.DFX_OneOrMoreFilesReloaded)
                        ElseIf (result And CUInt(tagVSQueryEditResultFlags.QER_CheckoutCanceledOrFailed)) <> 0 Then
                            Throw ComponentModel.Design.CheckoutException.Canceled
                        Else
                            Throw New ComponentModel.Design.CheckoutException(My.Resources.Designer.DFX_UnableToCheckout)
                        End If
                    Else
                        Return False
                    End If
                End If
            Else
                Dim result As Integer
                Dim success As Integer
                Dim txtManager As TextManager.Interop.IVsTextManager =
                    TryCast(sp.GetService(GetType(TextManager.Interop.VsTextManagerClass)), TextManager.Interop.IVsTextManager)

                If txtManager IsNot Nothing Then
                    For Each fileName As String In files
                        If checkOnly Then
                            Dim nonEditable As Integer
                            VSErrorHandler.ThrowOnFailure(txtManager.GetBufferSccStatus2(fileName, nonEditable, result))
                            If nonEditable <> 0 Then
                                success = 1
                            Else
                                success = 0
                            End If
                        Else
                            VSErrorHandler.ThrowOnFailure(txtManager.AttemptToCheckOutBufferFromScc2(fileName, success, result))
                        End If

                        ' If this was reloaded, we better add it to the list of reloaded files...
                        If (result And tagVSQueryEditResultFlags2.QER_Reloaded) = tagVSQueryEditResultFlags2.QER_Reloaded Then
                            fileReloaded = True
                        End If

                        If success = 0 Then
                            If throwOnFailure Then
                                ' Failed the checkout.  We need to throw a checkout exception, but we should 
                                ' check to see if the failure happened because the user canceled.
                                '
                                If (result And CUInt(tagVSQueryEditResultFlags.QER_CheckoutCanceledOrFailed)) <> 0 Then
                                    Throw ComponentModel.Design.CheckoutException.Canceled
                                Else
                                    Throw New ComponentModel.Design.CheckoutException(My.Resources.Designer.DFX_UnableToCheckout)
                                End If
                            Else
                                Return False
                            End If
                        End If
                    Next
                    Return True
                Else
                    Debug.Fail("Failed to get both IVsQueryEditQuerySave2 and IVsTextManager services - can't check out file!")
                End If
            End If

            ' Assume we can edit the file...
            Return True
        End Function

        ''' <summary>
        ''' Check if it is OK to save the given files
        ''' </summary>
        ''' <param name="sp">Service provider. Will be QI:ed for IVsQueryEditQuerySave2</param>
        ''' <param name="files">The set of files to check</param>
        ''' <param name="throwOnFailure">Should we throw if the save fails?</param>
        Public Shared Function QuerySave(sp As IServiceProvider, files As List(Of String), throwOnFailure As Boolean) As Boolean
            Requires.NotNull(sp)
            Requires.NotNull(files)

            If files.Count = 0 Then
                Return True
            End If

            Dim qEdit2 As IVsQueryEditQuerySave2
            qEdit2 = TryCast(sp.GetService(GetType(SVsQueryEditQuerySave)), IVsQueryEditQuerySave2)

            If qEdit2 IsNot Nothing Then
                Dim filesToCheckOut(files.Count - 1) As String
                files.CopyTo(filesToCheckOut, 0)

                Dim result As UInteger
                Dim rgrf(files.Count - 1) As UInteger

                Dim flags As UInteger = 0

                VSErrorHandler.ThrowOnFailure(qEdit2.QuerySaveFiles(flags, filesToCheckOut.Length, filesToCheckOut, rgrf, Nothing, result))

                Dim success As Boolean = result = CInt(tagVSQuerySaveResult.QSR_SaveOK)

                If success Then
                    Return True
                Else
                    If throwOnFailure Then
                        ' Failed the checkout.  We need to throw a checkout exception, but we should 
                        ' check to see if the failure happened because the user canceled.
                        '
                        If (result And CUInt(tagVSQuerySaveResult.QSR_NoSave_UserCanceled)) <> 0 Then
                            Throw ComponentModel.Design.CheckoutException.Canceled
                        Else
                            Throw New ComponentModel.Design.CheckoutException(My.Resources.Designer.DFX_UnableToCheckout)
                        End If
                    Else
                        Return False
                    End If
                End If
            Else
                Debug.Fail("Failed to get IVsQueryEditQuerySave2 services - can't query save the file!")
            End If

            ' Assume we can save the file...
            Return True
        End Function
#End Region

    End Class
End Namespace
