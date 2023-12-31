﻿' Licensed to the .NET Foundation under one or more agreements. The .NET Foundation licenses this file to you under the MIT license. See the LICENSE.md file in the project root for more information.

Imports System.ComponentModel.Design
Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

Imports Microsoft.VisualStudio.ManagedInterfaces.ProjectDesigner
Imports Microsoft.VisualStudio.OLE.Interop

Imports Common = Microsoft.VisualStudio.Editors.AppDesCommon
Imports NativeMethods = Microsoft.VisualStudio.Editors.AppDesInterop.NativeMethods

Namespace Microsoft.VisualStudio.Editors.PropertyPages

#Region "Internal interface definitions"

    ' ******************
    '
    ' IMPORTANT NOTE: 
    '
    ' These interfaces are internal to our property page implementation.  They are marked public for those assemblies that have 
    '   classes that inherit from PropPageUserControlBase, but they are not useful for assemblies implementing pages for the project
    '   designer to host without using PropPageUserControlBase.  These interfaces should never be construed to have communication
    '   between the property pages and the property page designer view or the project designer, because that would make it impossible
    '   for third parties to write property pages with the same functionality (because they are not in a non-versioned common
    '   assembly).  
    '
    'The only interface necessary for third parties to implement for property pages that the project designer can host is
    '   IPropertyPage.  The interfaces in Microsoft.VisualStudio.ManagedInterfaces.dll are optional for third parties to implement
    '   in order to hook in to our undo functionality and control group validation.
    '
    ' *******************

    ''' <summary>
    ''' This is an interface interface used to communicate between the PropPageBase class and any property pages (those that inherit
    '''   from PropPageUserControlBase).
    ''' It is our internal equivalent of IPropertyPage2.
    ''' </summary>
    <ComVisible(False)>
    Public Interface IPropertyPageInternal
        Sub Apply()
        Sub Help(HelpDir As String)
        Function IsPageDirty() As Boolean
        Sub SetObjects(objects() As Object)
        Sub SetPageSite(base As IPropertyPageSiteInternal)
        Sub EditProperty(dispid As Integer)
        Function GetHelpContextF1Keyword() As String ' Gets the F1 keyword to push into the user context for this property page

    End Interface

    ''' <summary>
    ''' This is an interface interface that subclasses of PropPageUserControlBase use to communicate with their site (PropPageBaseClass)
    '''   (i.e., it is internal to our property page implementation - public for those assemblies that have classes that inherit from
    '''   PropPageUserControlBase, but not useful for those implementing pages without using PropPageUserControlBase).
    ''' It is our internal equivalent of IPropertyPageSite.
    ''' </summary>
    <ComVisible(False)>
    Public Interface IPropertyPageSiteInternal
        Sub OnStatusChange(flags As PROPPAGESTATUS)
        Function GetLocaleID() As Integer
        Function TranslateAccelerator(msg As Message) As Integer
        Function GetService(ServiceType As Type) As Object
        ReadOnly Property IsImmediateApply As Boolean
    End Interface

    <Flags, ComVisible(False)>
    Public Enum PROPPAGESTATUS
        Dirty = 1
        Validate = 2
        Clean = 4
    End Enum

#If False Then 'Not currently needed by any pages, consider exposing if needed in the future
    <ComVisible(False)> _
    Public Interface IVsDocDataContainer
        Function GetDocDataCookies() As UInteger()
    End Interface
#End If

#End Region

#Region "PropPageBase"

    Public MustInherit Class PropPageBase
        Implements IPropertyPage2
        Implements IPropertyPageSiteInternal
        Implements IVsProjectDesignerPage
#If False Then
        Implements IVsDocDataContainer
#End If

        Private _propPage As Control
        Private _pageSite As IPropertyPageSite
        Private Const SW_HIDE As Integer = 0
        Private _size As Drawing.Size
        Private _defaultSize As Drawing.Size
        Private _docString As String
        Private _helpFile As String
        Private _helpContext As UInteger
        Private _objects As Object()
        Private _prevParent As IntPtr
        Private _dispidFocus As Integer
        Private _hostedInNative As Boolean
        Private _wasSetParentCalled As Boolean

        Protected Sub New()
        End Sub

        Protected MustOverride Function CreateControl() As Control 'CONSIDER: this appears to be used only for the default implementation of GetDefaultSize - better way of doing this?  Is this a performance hit?

#Region "IPropertyPageSiteInternal"

        Protected Sub OnStatusChange(flags As PROPPAGESTATUS) Implements IPropertyPageSiteInternal.OnStatusChange
            If _pageSite IsNot Nothing Then
                _pageSite.OnStatusChange(CType(flags, UInteger))
            End If
        End Sub

        Protected Function GetLocaleID() As Integer Implements IPropertyPageSiteInternal.GetLocaleID
            Dim localeID As UInteger
            If _pageSite IsNot Nothing Then
                _pageSite.GetLocaleID(localeID)
            Else
                Debug.Fail("PropertyPage site not set")
            End If
            Return CType(localeID, Integer)
        End Function

        ''' <summary>
        ''' Instructs the page site to process a keystroke if it desires.
        ''' </summary>
        ''' <param name="msg"></param>
        ''' <remarks>
        ''' This function can be called by a property page to give the site a chance to a process a message
        '''   before the page does.  Return S_OK to indicate we have handled it, S_FALSE to indicate we did not
        '''   process it, and E_NOTIMPL to indicate that the site does not support keyboard processing.
        ''' </remarks>
        Protected Function TranslateAccelerator(msg As Message) As Integer Implements IPropertyPageSiteInternal.TranslateAccelerator
            'Delegate to the actual site.
            If _pageSite IsNot Nothing Then
                Dim _msg As MSG
                _msg.hwnd = msg.HWnd
                _msg.message = CType(msg.Msg, UInteger)
                _msg.wParam = msg.WParam
                _msg.lParam = msg.LParam

                Return _pageSite.TranslateAccelerator(New MSG(0) {_msg})
            Else
                'Returning S_FALSE indicates we have no handled the message
                Return NativeMethods.S_FALSE
            End If

        End Function

        ''' <summary>
        ''' Calls GetService on site first, then on pagesite if service wasn't found
        ''' </summary>
        ''' <param name="ServiceType"></param>
        Protected Function GetService(ServiceType As Type) As Object Implements IPropertyPageSiteInternal.GetService
            'Proffer the actual IPropertyPageSite as a service
            If ServiceType.Equals(GetType(IPropertyPageSite)) Then
                Return _pageSite
            End If

            Dim sp As System.IServiceProvider = TryCast(_pageSite, System.IServiceProvider)
            If sp IsNot Nothing Then
                Return sp.GetService(ServiceType)
            End If

            Return Nothing
        End Function

        ''' <summary>
        ''' Returns whether or not the property page hosted in this site should be with 
        '''   immediate-apply mode or not
        ''' </summary>
        Private ReadOnly Property IsImmediateApply As Boolean Implements IPropertyPageSiteInternal.IsImmediateApply
            Get
                'Current implementation is always immediate-apply for
                '  modeless property pages
                Return True
            End Get
        End Property

#End Region

        Protected Overridable Property DocString As String
            Get
                Return _docString
            End Get
            Set
                _docString = Value
            End Set
        End Property

        Protected MustOverride ReadOnly Property ControlType As Type

        Protected Overridable ReadOnly Property ControlTypeForResources As Type
            Get
                Return ControlType
            End Get
        End Property

        Protected MustOverride ReadOnly Property Title As String

        Protected Overridable Property HelpFile As String
            Get
                Return _helpFile
            End Get
            Set
                _helpFile = Value
            End Set
        End Property

        Protected Overridable Property HelpContext As UInteger
            Get
                Return _helpContext
            End Get
            Set
                _helpContext = Value
            End Set
        End Property

        Protected Overridable Property DefaultSize As Drawing.Size
            Get
                If _defaultSize.Width = 0 Then 'CONSIDER: Need a better mechanism than assuming the resources are available.  Perf hit?
                    Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(ControlTypeForResources)
                    Dim mySize As Drawing.Size = CType(resources.GetObject("$this.Size"), Drawing.Size)
                    If mySize.IsEmpty Then
                        'Check for ClientSize if Size is not found
                        mySize = CType(resources.GetObject("$this.ClientSize"), Drawing.Size)
                    End If
                    _defaultSize = mySize
                End If
                Return _defaultSize
            End Get
            Set
                _defaultSize = Value
            End Set
        End Property

        Protected Overridable ReadOnly Property Objects As Object()
            Get
                Return _objects
            End Get
        End Property

        Public Overridable ReadOnly Property SupportsTheming As Boolean
            Get
                Return False
            End Get
        End Property

        Private Sub IPropertyPage2_Activate(hWndParent As IntPtr, pRect() As RECT, bModal As Integer) Implements IPropertyPage2.Activate, IPropertyPage.Activate

            If _propPage IsNot Nothing Then
                Debug.Assert(NativeMethods.GetParent(_propPage.Handle).Equals(hWndParent), "Property page already Activated with different parent")
                Return
            End If

            _size.Width = pRect(0).right - pRect(0).left
            _size.Height = pRect(0).bottom - pRect(0).top

            Create(hWndParent)

            'PERF: Delay making the property page visible until we have moved it to its correct location
            If _propPage IsNot Nothing Then
                _propPage.SuspendLayout()
            End If
            Move(pRect)
            If _propPage IsNot Nothing Then
                _propPage.ResumeLayout(True)
            End If

            _propPage.Visible = True

            'Make sure we set focus to the active control.  This would normally
            '  happen on window activate automatically, but the property page
            '  isn't hosted until after that.
            _propPage.Focus()

            'If the first-focused control on the page is a TextBox, select all its text.  This is normally done
            '  automatically by Windows Forms, but the handling of double-click by the shell before the mouse up
            '  confuses Windows Forms in thinking the textbox was activated by being clicked, so it doesn't happen
            '  in this case.
            If TypeOf _propPage Is ContainerControl AndAlso TypeOf DirectCast(_propPage, ContainerControl).ActiveControl Is TextBox Then
                DirectCast(DirectCast(_propPage, ContainerControl).ActiveControl, TextBox).SelectAll()
            End If

            If _dispidFocus <> -1 Then
                CType(_propPage, IPropertyPageInternal).EditProperty(_dispidFocus)
            End If

        End Sub

        Private Function IPropertyPage_Apply() As Integer Implements IPropertyPage.Apply
            Apply()
            Return NativeMethods.S_OK
        End Function

        Protected Overridable Sub Apply() Implements IPropertyPage2.Apply
            If _propPage IsNot Nothing Then
                Dim page As IPropertyPageInternal = CType(_propPage, IPropertyPageInternal)
                page.Apply()
            End If
        End Sub

        Protected Overridable Sub Deactivate() Implements IPropertyPage2.Deactivate, IPropertyPage.Deactivate

            If _propPage IsNot Nothing Then

                _propPage.SuspendLayout() 'No need for more layouts...
                If _propPage.Parent IsNot Nothing AndAlso Not _hostedInNative Then
                    'We sited ourselves by setting the Windows Forms Parent property
                    _propPage.Parent = Nothing
                ElseIf _wasSetParentCalled Then
                    'We sited ourselves via a native SetParent call
                    NativeMethods.SetParent(_propPage.Handle, _prevParent)
                End If

                _propPage.Dispose()
                _propPage = Nothing

            End If

        End Sub

        Private Sub GetPageInfo(pPageInfo() As PROPPAGEINFO) Implements IPropertyPage2.GetPageInfo, IPropertyPage.GetPageInfo
            Requires.NotNull(pPageInfo)

            pPageInfo(0).cb = 4 + 4 + 8 + 4 + 4 + 4
            pPageInfo(0).dwHelpContext = HelpContext
            pPageInfo(0).pszDocString = DocString
            pPageInfo(0).pszHelpFile = HelpFile
            pPageInfo(0).pszTitle = Title
            pPageInfo(0).SIZE.cx = DefaultSize.Width
            pPageInfo(0).SIZE.cy = DefaultSize.Height

        End Sub

        Protected Overridable Sub Help(strHelpDir As String) Implements IPropertyPage2.Help, IPropertyPage.Help

            If _propPage Is Nothing Then
                Return
            End If

            Dim page As IPropertyPageInternal = CType(_propPage, IPropertyPageInternal)
            page.Help(strHelpDir)

        End Sub

        Protected Overridable Function IsPageDirty() As Integer Implements IPropertyPage2.IsPageDirty, IPropertyPage.IsPageDirty

            If _propPage Is Nothing Then
                Return NativeMethods.S_FALSE
            End If

            Try
                If CType(_propPage, IPropertyPageInternal).IsPageDirty() Then
                    Return NativeMethods.S_OK
                End If
            Catch ex As Exception When Common.ReportWithoutCrash(ex, NameOf(IsPageDirty), NameOf(PropPageBase))
                Return NativeMethods.E_FAIL
            End Try

            Return NativeMethods.S_FALSE

        End Function

        Private Sub Move(pRect() As RECT) Implements IPropertyPage2.Move, IPropertyPage.Move
            ' we need to adjust the size of the page if it's autosize or if we're native (in which
            ' case we're going to adjust the size of our secret usercontrol instead) See the Create
            ' for more info about the panel
            If _propPage IsNot Nothing AndAlso pRect IsNot Nothing AndAlso pRect.Length <> 0 AndAlso (_propPage.AutoSize OrElse _hostedInNative) Then
                Dim minSize As Drawing.Size = _propPage.MinimumSize

                ' we have to preserve these to set the size of our scrolling panel
                Dim height As Integer = pRect(0).bottom - pRect(0).top
                Dim width As Integer = pRect(0).right - pRect(0).left

                ' we'll use these to set the page size since they'll respect the minimums
                Dim minRespectingHeight As Integer = pRect(0).bottom - pRect(0).top
                Dim minRespectingWidth As Integer = pRect(0).right - pRect(0).left

                If height < minSize.Height Then
                    minRespectingHeight = minSize.Height
                End If
                If width < minSize.Width Then
                    minRespectingWidth = minSize.Width
                End If

                _propPage.Bounds = New Rectangle(pRect(0).left, pRect(0).top, minRespectingWidth, minRespectingHeight)
                ' if we're in native, set our scrolling panel to be the exact size that we were
                ' passed so if we need scroll bars, they show up properly
                If _hostedInNative Then
                    _propPage.Parent.Size = New Drawing.Size(width, height)
                End If

            End If

        End Sub

        Protected Overridable Sub SetObjects(cObjects As UInteger, objects() As Object) Implements IPropertyPage2.SetObjects, IPropertyPage.SetObjects

            'Debug.Assert seems to have problems during shutdown - don't do the check
            'Debug.Assert((objects Is Nothing AndAlso cObjects = 0) OrElse (objects IsNot Nothing AndAlso objects.Length = cObjects), "Unexpected arguments")

            _objects = objects
            Debug.Assert(objects Is Nothing OrElse objects.Length = 0 OrElse objects(0) IsNot Nothing)

            If _propPage IsNot Nothing Then

                CType(_propPage, IPropertyPageInternal).SetObjects(_objects)

            End If

        End Sub

        Private Sub SetPageSite(PageSite As IPropertyPageSite) Implements IPropertyPage2.SetPageSite, IPropertyPage.SetPageSite
            If PageSite IsNot Nothing AndAlso _pageSite IsNot Nothing Then
                Throw New COMException("PageSite", NativeMethods.E_UNEXPECTED)
            End If

            _pageSite = PageSite
        End Sub

        Private Sub Show(nCmdShow As UInteger) Implements IPropertyPage2.Show, IPropertyPage.Show

            If _propPage Is Nothing Then
                Throw New InvalidOperationException("Form not created")
            End If

            ' if we're in native, show/hide our secret scrolling panel too
            ' See Create(hWnd) for more info on where that comes from
            If nCmdShow <> SW_HIDE Then
                If _hostedInNative Then
                    _propPage.Parent.Show()
                End If
                _propPage.Show()
                SetHelpContext()
            Else
                If _hostedInNative Then
                    _propPage.Parent.Hide()
                End If
                _propPage.Hide()
            End If

        End Sub

        ''' <summary>
        ''' Sets the help context into the help service for this property page.
        ''' </summary>
        Private Sub SetHelpContext()
            If _pageSite IsNot Nothing Then
                Dim sp As System.IServiceProvider = TryCast(_pageSite, System.IServiceProvider)
                'Note: we have to get the right help service - GetService through the property page's
                '  accomplishes this (it goes through the PropPageDesignerRootDesigner).  There is a
                '  separate help service associated with each window frame.
                Dim HelpService As IHelpService = TryCast(sp.GetService(GetType(IHelpService)), IHelpService)
                If HelpService IsNot Nothing Then
                    Dim HelpKeyword As String = Nothing
                    Dim PropertyPageContext As IPropertyPageInternal = TryCast(_propPage, IPropertyPageInternal)
                    If PropertyPageContext IsNot Nothing Then
                        HelpKeyword = PropertyPageContext.GetHelpContextF1Keyword()
                    End If
                    If HelpKeyword Is Nothing Then
                        HelpKeyword = String.Empty
                    End If
                    HelpService.AddContextAttribute("Keyword", HelpKeyword, HelpKeywordType.F1Keyword)
                Else
                    Debug.Fail("Page site doesn't proffer IHelpService")
                End If
            Else
                Debug.Fail("Page site not a service provider - can't set help context for page")
            End If
        End Sub

        ''' <summary>
        ''' Instructs the property page to process the keystroke described in pMsg.
        ''' </summary>
        ''' <param name="pMsg"></param>
        ''' <returns>
        ''' S_OK if the property page handled the accelerator, S_FALSE if the property page handles accelerators, but this one was not useful to it,
        '''   S_NOTIMPL if the property page does not handle accelerators, or E_POINTER if the address in pMsg is not valid. For example, it may be NULL.
        ''' </returns>
        Protected Overridable Function TranslateAccelerator(pMsg() As MSG) As Integer Implements IPropertyPage2.TranslateAccelerator, IPropertyPage.TranslateAccelerator
            If pMsg Is Nothing Then
                Return NativeMethods.E_POINTER
            End If

            If _propPage IsNot Nothing Then
                Dim m As Message = Message.Create(pMsg(0).hwnd, CType(pMsg(0).message, Integer), pMsg(0).wParam, pMsg(0).lParam)
                Dim used As Boolean = False

                'Preprocessing should be passed to the control whose handle the message refers to.
                Dim target As Control = Control.FromChildHandle(m.HWnd)
                If target IsNot Nothing Then
                    used = target.PreProcessMessage(m)
                End If

                If used Then
                    pMsg(0).message = CType(m.Msg, UInteger)
                    pMsg(0).wParam = m.WParam
                    pMsg(0).lParam = m.LParam
                    'Returning S_OK indicates we handled the message ourselves
                    Return NativeMethods.S_OK
                End If
            End If

            'Returning S_FALSE indicates we have not handled the message
            Return NativeMethods.S_FALSE
        End Function

        Private Sub EditProperty(dispid As Integer) Implements IPropertyPage2.EditProperty

            _dispidFocus = dispid

            If _propPage IsNot Nothing Then
                Dim page As IPropertyPageInternal = CType(_propPage, IPropertyPageInternal)
                page.EditProperty(dispid)
            End If

        End Sub

        Private Function Create(hWndParent As IntPtr) As IntPtr

            _propPage = CreateControl()
            Debug.Assert(TypeOf _propPage Is IPropertyPageInternal)
            _propPage.Visible = False 'PERF: Delay making the property page visible until we have moved it to its correct location
            _propPage.SuspendLayout()
            Try

                'PERF: Set the page site before setting up the parent, so that the page has the opportunity
                '  to properly set its Font before being shown
                CType(_propPage, IPropertyPageInternal).SetPageSite(CType(Me, IPropertyPageSiteInternal))

                If Not (TypeOf _propPage Is IPropertyPageInternal) Then
                    Throw New InvalidOperationException("Control must implement IPropertyPageInternal")
                End If
                _prevParent = NativeMethods.GetParent(_propPage.Handle)

                Common.Switches.TracePDPerf("PropPage.Create: Setting the property page's parent")

                Dim ParentControl As Control = Control.FromHandle(hWndParent)
                Dim AlwaysUseSetParent As Boolean = False
#If DEBUG Then
                AlwaysUseSetParent = Common.Switches.PDAlwaysUseSetParent.Enabled
#End If
                If ParentControl IsNot Nothing AndAlso Not AlwaysUseSetParent Then
                    _propPage.Parent = ParentControl
                    Debug.Assert(_propPage.Parent IsNot Nothing, "Huh?  Deactivate() logic depends on this.")
                Else
                    'Not a managed window, use the win32 api method
                    _hostedInNative = True
                    ' in order to have scroll bars properly appear in large fonts, wrap
                    ' the page in a usercontrol (since it supports AutoScroll) that won't
                    ' scale with fonts. Move(rect) will set the proper size.
                    Dim sizingParent As New UserControl With {
                        .AutoScaleMode = AutoScaleMode.None,
                        .AutoScroll = True,
                        .Text = "SizingParent" 'For debugging purposes (Spy++)
                        }
                    _propPage.Parent = sizingParent
                    NativeMethods.SetParent(sizingParent.Handle, hWndParent)
                    _wasSetParentCalled = True
                    Debug.Assert(_propPage.Parent Is Nothing OrElse AlwaysUseSetParent, "Huh?  Deactivate() logic depends on this.")
                End If

                'Site the undo manager if we have one and the page supports it
                If (_propPageUndoSite IsNot Nothing) AndAlso (TypeOf _propPage Is IVsProjectDesignerPage) Then
                    CType(_propPage, IVsProjectDesignerPage).SetSite(_propPageUndoSite)
                End If

                'If the SetObjects call was cached, we need to do the SetObjects now
                If (_objects IsNot Nothing) AndAlso (_objects.Length > 0) Then
                    Dim page As IPropertyPageInternal = CType(_propPage, IPropertyPageInternal)
                    page.SetObjects(_objects)
                End If

                Return _propPage.Handle

            Finally
                'We don't want to lay out until we've set our size correctly (in IPropertyPage2_Activate)
                _propPage.ResumeLayout(False)
            End Try
        End Function

#Region "IVsProjectDesignerPage"
        Private _propPageUndoSite As IVsProjectDesignerPageSite

        ''' <summary>
        ''' Gets the current value for the given property.  This value will be serialized using the binary serializer, and saved for
        '''   use later by Undo and Redo operations.
        ''' </summary>
        ''' <param name="PropertyName"></param>
        Protected Overridable Function GetProperty(PropertyName As String) As Object Implements IVsProjectDesignerPage.GetProperty
            Dim Page As IVsProjectDesignerPage = TryCast(_propPage, IVsProjectDesignerPage)
            If Page IsNot Nothing Then
                Return Page.GetProperty(PropertyName)
            End If

            Return Nothing
        End Function

        ''' <summary>
        ''' Tells the property page to set the given value for the given property.  This is called during Undo and Redo operations.  The
        '''   page should also update its UI for the given property.
        ''' </summary>
        ''' <param name="PropertyName"></param>
        ''' <param name="Value"></param>
        Protected Overridable Sub SetProperty(PropertyName As String, Value As Object) Implements IVsProjectDesignerPage.SetProperty
            Dim Page As IVsProjectDesignerPage = TryCast(_propPage, IVsProjectDesignerPage)
            If Page IsNot Nothing Then
                Page.SetProperty(PropertyName, Value)
            End If
        End Sub

        ''' <summary>
        ''' Notifies the property page of the IVsProjectDesignerPageSite
        ''' </summary>
        ''' <param name="site"></param>
        Protected Overridable Sub SetSite(site As IVsProjectDesignerPageSite) Implements IVsProjectDesignerPage.SetSite
            Dim Page As IVsProjectDesignerPage = TryCast(_propPage, IVsProjectDesignerPage)
            If Page IsNot Nothing Then
                Page.SetSite(site)
            End If
            _propPageUndoSite = site
        End Sub

        ''' <summary>
        ''' Returns true if the given property supports returning and setting multiple values at the same time in order to support
        '''   Undo and Redo operations when multiple configurations are selected by the user.  This function should always return the
        '''   same value for a given property (i.e., it does not depend on whether multiple configurations have currently been passed in
        '''   to SetObjects, but simply whether this property supports multiple-value undo/redo).
        ''' </summary>
        ''' <param name="PropertyName"></param>
        Protected Overridable Function SupportsMultipleValueUndo(PropertyName As String) As Boolean Implements IVsProjectDesignerPage.SupportsMultipleValueUndo
            Dim Page As IVsProjectDesignerPage = TryCast(_propPage, IVsProjectDesignerPage)
            If Page IsNot Nothing Then
                Return Page.SupportsMultipleValueUndo(PropertyName)
            Else
                Return False
            End If
        End Function

        ''' <summary>
        ''' Gets the current values for the given property, one for each of the objects (configurations) that may be affected by a property
        '''   change and need to be remembered for Undo purposes.  The set of objects passed back normally should be the same objects that
        '''   were given to the page via SetObjects (but this is not required).
        '''   This function is called for a property if SupportsMultipleValueUndo returns true for that property.  If 
        ''' SupportsMultipleValueUndo returns false, or this function returns False, then GetProperty is called instead.
        ''' </summary>
        ''' <param name="PropertyName">The property to read values from</param>
        ''' <param name="Objects">[out] The set of objects (configurations) whose properties should be remembered by Undo</param>
        ''' <param name="Values">[out] The current values of the property for each configuration (corresponding to Objects)</param>
        ''' <returns>True if the property has multiple values to be read.</returns>
        Protected Overridable Function GetPropertyMultipleValues(PropertyName As String, ByRef Objects As Object(), ByRef Values As Object()) As Boolean Implements IVsProjectDesignerPage.GetPropertyMultipleValues
            Dim Page As IVsProjectDesignerPage = TryCast(_propPage, IVsProjectDesignerPage)
            If Page IsNot Nothing Then
                Return Page.GetPropertyMultipleValues(PropertyName, Objects, Values)
            Else
                Objects = Nothing
                Values = Nothing
                Return False
            End If
        End Function

        ''' <summary>
        ''' Tells the property page to set the given values for the given properties, one for each of the objects (configurations) passed
        '''   in.  This property is called if the corresponding previous call to GetPropertyMultipleValues succeeded, otherwise
        '''   SetProperty is called instead.
        ''' Note that the Objects values are not required to be a subset of the objects most recently passed in through SetObjects.
        ''' </summary>
        ''' <param name="PropertyName"></param>
        ''' <param name="Objects"></param>
        ''' <param name="Values"></param>
        Protected Overridable Sub SetPropertyMultipleValues(PropertyName As String, Objects() As Object, Values() As Object) Implements IVsProjectDesignerPage.SetPropertyMultipleValues
            Dim Page As IVsProjectDesignerPage = TryCast(_propPage, IVsProjectDesignerPage)
            If Page IsNot Nothing Then
                Page.SetPropertyMultipleValues(PropertyName, Objects, Values)
            End If
        End Sub

        ''' <summary>
        ''' Finish all pending validations
        ''' </summary>
        ''' <returns>Return false if validation failed, and the customer wants to fix it (not ignore it)</returns>
        Public Function FinishPendingValidations() As Boolean Implements IVsProjectDesignerPage.FinishPendingValidations
            Dim Page As IVsProjectDesignerPage = TryCast(_propPage, IVsProjectDesignerPage)
            If Page IsNot Nothing Then
                Return Page.FinishPendingValidations()
            End If
            Return True
        End Function

        ''' <summary>
        ''' Called when the page is activated or deactivated
        ''' </summary>
        ''' <param name="activated"></param>
        Public Sub OnWindowActivated(activated As Boolean) Implements IVsProjectDesignerPage.OnActivated
            Dim Page As IVsProjectDesignerPage = TryCast(_propPage, IVsProjectDesignerPage)
            If Page IsNot Nothing Then
                Page.OnActivated(activated)
            End If
        End Sub

#End Region

#If False Then
#Region "IVsDocDataContainer"
        ''' <summary>
        ''' Provides a mechanism for property pages to expose docdatas that need to be saved in the Project Designer
        ''' </summary>
        Protected Overridable Function GetDocDataCookies() As UInteger() Implements IVsDocDataContainer.GetDocDataCookies
            If TypeOf m_PropPage Is IVsDocDataContainer Then
                Return DirectCast(m_PropPage, IVsDocDataContainer).GetDocDataCookies()
            End If

            Return New UInteger() {}
        End Function
#End Region
#End If

    End Class

#End Region

#Region "VBPropPageBase"

    Public MustInherit Class VBPropPageBase
        Inherits PropPageBase

        Protected Sub New()
            'The follow entry is a dummy value - without something stuffed in here the
            '  property page will NOT show the help button. The F1 keyword is the real 
            '  help context
            MyBase.New()
            HelpContext = 1
            HelpFile = "VBREF.CHM"
        End Sub

    End Class

#End Region

End Namespace
