﻿' Licensed to the .NET Foundation under one or more agreements. The .NET Foundation licenses this file to you under the MIT license. See the LICENSE.md file in the project root for more information.

Imports System.ComponentModel
Imports System.ComponentModel.Design
Imports System.Windows.Forms
Imports System.Windows.Forms.Design
Imports Microsoft.VisualStudio.Utilities

Namespace Microsoft.VisualStudio.Editors.AppDesDesignerFramework

    '**************************************************************************
    ';BaseDialog
    '
    'Remarks:
    '   A base dialog class to create and bring up dialog properly within VS Designer.
    '   This class is converted from <DD>\wizard\vsdesigner\designer\microsoft\vsdesigner\VsDesignerDialog.cs
    '
    '   Ported from VSDesigner (VSDialog.cs)
    '
    '**************************************************************************
    Public Class BaseDialog
        Inherits Form

        '= PUBLIC =============================================================

        ';Constructors
        '==========

        ''' <summary>
        ''' Constructor needed to be able to visually design classes that inherit
        '''   from this base class (the form designer has to be able to create an
        '''   instance of the inherited form's base class - this class - and it can't
        '''   do that without an "appropriate" constructor).
        ''' Do not use this constructor in your code!
        ''' </summary>
        ''' <remarks>
        ''' Do not use this constructor in your code!
        ''' </remarks>
        <EditorBrowsable(EditorBrowsableState.Never)>
        Public Sub New()
            If LicenseManager.UsageMode <> LicenseUsageMode.Designtime Then
                Debug.Fail("You should use the constructor with a ServiceProvider parameter")
            End If

        End Sub

        '**************************************************************************
        ';New
        '
        'Summary:
        '   Initialize a new dialog box.
        'Params:
        '   ServiceProvider: The IServiceProvider required to find the help provider.
        'Exceptions:
        '   ArgumentNullException: If ServiceProvider is Nothing.
        '**************************************************************************
        Public Sub New(ServiceProvider As IServiceProvider)
            Debug.Assert(ServiceProvider IsNot Nothing, "ServiceProvider is NULL.")
            Requires.NotNull(ServiceProvider)

            _serviceProvider = ServiceProvider

            ' Initialize default dialog settings
            KeyPreview = True
            MaximizeBox = False
            MinimizeBox = False
            ShowInTaskbar = False
            'Icon = null;
            StartPosition = FormStartPosition.CenterParent
            'FormBorderStyle = FormBorderStyle.FixedSingle;
            AddHandler HelpRequested, AddressOf OnHelpRequested
        End Sub 'New

        ';Methods
        '==========

        '**************************************************************************
        ';ShowDialog
        '
        'Summary:
        '   Show the dialog.
        '**************************************************************************
        Public Shadows Function ShowDialog() As DialogResult
            If Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog Then
                Icon = Nothing
            End If
            If _uiService Is Nothing Then
                _uiService = CType(GetService(GetType(IUIService)), IUIService)
            End If

            Using DpiAwareness.EnterDpiScope(DpiAwarenessContext.SystemAware)
                If _uiService IsNot Nothing Then
                    Return _uiService.ShowDialog(Me)
                Else
                    Return MyBase.ShowDialog()
                End If
            End Using
        End Function 'ShowDialog

        '= Public =============================================================

        '**************************************************************************
        ';ServiceProvider
        '
        'Summary:
        '   Gets or sets the IServiceProvider for this dialog.
        '**************************************************************************
        Public Property ServiceProvider As IServiceProvider
            Get
                Return _serviceProvider
            End Get
            Set
                Debug.Assert(Value IsNot Nothing, "Bad service provider")
                _serviceProvider = Value
            End Set
        End Property

        '= PROTECTED ==========================================================

        ';Properties
        '==========

        '**************************************************************************
        ';F1Keyword
        '
        'Summary:
        '   Gets or sets the help keyword for this dialog.
        '**************************************************************************
        Protected Overridable Property F1Keyword As String
            Get
                Return _helpKeyword
            End Get
            Set
                _helpKeyword = Value
            End Set
        End Property

        '**************************************************************************
        ';CurrentDesignerHost
        '
        'Summary:
        '   Gets the current designer host.
        '**************************************************************************
        Protected ReadOnly Property CurrentDesignerHost As IDesignerHost
            Get
                Return CType(GetService(GetType(IDesignerHost)), IDesignerHost)
            End Get
        End Property

        ';Methods
        '==========

        '**************************************************************************
        ';ShowHelp
        '
        'Summary:
        '   Shows the help topic for the dialog.
        '**************************************************************************
        Protected Sub ShowHelp()
            If _serviceProvider IsNot Nothing AndAlso F1Keyword IsNot Nothing Then
                DesignUtil.DisplayTopicFromF1Keyword(_serviceProvider, F1Keyword)
            End If
        End Sub 'ShowHelp

        '**************************************************************************
        ';SetFontStyles
        '
        'Summary:
        '   Set the font style of all controls on the dialog to its font style.
        '**************************************************************************
        Protected Sub SetFontStyles()
            DesignUtil.SetFontStyles(Me)
        End Sub 'SetFontStyles

        '**************************************************************************
        ';GetService
        '
        'Summary:
        '   Get a service from the dialog's service provider.
        'Params:
        '   ServiceType: The service's type used to get the service.
        'Returns:
        '   An Object contains the service.
        '**************************************************************************
        Protected Overrides Function GetService(ServiceType As Type) As Object

            If ServiceProvider Is Nothing Then
                Return MyBase.GetService(ServiceType)
            End If

            Return ServiceProvider.GetService(ServiceType)
        End Function 'GetService

        ' Error reporting functions
        ' -------------------------

        '**************************************************************************
        ';ReportError
        '
        'Summary:
        '   Displays an error message box with the specified error message.
        'Params:
        '   ErrorMessage: The text to display in the message box.
        '**************************************************************************
        Protected Overloads Sub ReportError(ErrorMessage As String)
            DesignUtil.ReportError(ServiceProvider, ErrorMessage)
        End Sub 'ReportError

        '**************************************************************************
        ';ReportError
        '
        'Summary:
        '   Displays an error message box with the specified error message and help link.
        'Params:
        '   ServiceProvider: The IServiceProvider, used to get devenv shell as the parent of the message box.
        '   ErrorMessage: The text to display in the message box.
        '   HelpLink: Link to the help topic for this message box.
        '**************************************************************************
        Protected Overloads Sub ReportError(errorMessage As String, helpLink As String)
            DesignUtil.ReportError(ServiceProvider, errorMessage, helpLink)
        End Sub 'ReportError

        ' MessageBox functions
        ' --------------------

        '**************************************************************************
        ';ShowMessage
        '
        'Summary:
        '   Displays a message box with specified text, caption, buttons and icon.
        'Params:
        '   Message: The text to display in the message box.
        '   Caption: The text to display in the title bar of the message box.
        '   Buttons: One of the MessageBoxButtons values that specifies which buttons to display in the message box.
        '   Icon: One of the MessageBoxIcon values that specifies which icon to display in the message box.
        'Returns:
        '   One of the DialogResult values.
        '**************************************************************************
        Protected Overloads Function ShowMessage(Message As String, Caption As String,
                Buttons As MessageBoxButtons, Icon As MessageBoxIcon) As DialogResult
            Return DesignerMessageBox.Show(ServiceProvider, Message, Caption, Buttons, Icon)
        End Function 'ShowMessage

        '**************************************************************************
        ';ShowMessage
        '
        'Summary:
        '   Displays a message box with specified text, caption, buttons, icons and default button.
        'Params:
        '   Message: The text to display in the message box.
        '   Caption: The text to display in the title bar of the message box.
        '   Buttons: One of the MessageBoxButtons values that specifies which buttons to display in the message box.
        '   Icon: One of the MessageBoxIcon values that specifies which icon to display in the message box.
        '   DefaultButton: One of the MessageBoxDefaultButton values that specifies the default button of the message box.
        'Returns:
        '   One of the DialogResult values.
        '**************************************************************************
        Protected Overloads Function ShowMessage(Message As String, Caption As String,
                Buttons As MessageBoxButtons, Icon As MessageBoxIcon,
                DefaultButton As MessageBoxDefaultButton) As DialogResult
            Return DesignerMessageBox.Show(ServiceProvider, Message, Caption, Buttons, Icon, DefaultButton)
        End Function 'ShowMessage

        '= PRIVATE ============================================================

        '**************************************************************************
        ';OnHelpRequested
        '
        'Summary:
        '   Handle the HelpRequested event.
        '**************************************************************************
        Private Overloads Sub OnHelpRequested(sender As Object, e As HelpEventArgs)
            If Not e.Handled Then
                ShowHelp()
                e.Handled = True
            End If
        End Sub 'OnHelpRequested

        Private _serviceProvider As IServiceProvider
        Private _helpKeyword As String
        Private _uiService As IUIService

    End Class

End Namespace
