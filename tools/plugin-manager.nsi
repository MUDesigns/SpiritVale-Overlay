; SpiritVale Plugin Manager — current-user NSIS installer
; Build after: dotnet publish ... -o dist\win-x64
;   makensis /DVERSION=0.3.0 /DPUBLISHDIR=..\dist\win-x64 tools\plugin-manager.nsi

!ifndef VERSION
  !define VERSION "0.3.0"
!endif
!ifndef PUBLISHDIR
  !define PUBLISHDIR "..\dist\win-x64"
!endif

Name "SpiritVale Plugin Manager"
OutFile "..\dist\SpiritVale Plugin Manager_${VERSION}_x64-setup.exe"
InstallDir "$LOCALAPPDATA\SpiritValePluginManager"
RequestExecutionLevel user
Unicode true
SetCompressor /SOLID lzma
ShowInstDetails show

!include "MUI2.nsh"

!define MUI_ABORTWARNING
!define MUI_ICON "${PUBLISHDIR}\Assets\shop-icon.ico"
!define MUI_UNICON "${PUBLISHDIR}\Assets\shop-icon.ico"

!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "Install"
  SetOutPath "$INSTDIR"
  ; Wipe previous files so upgrades replace cleanly (plugins live in AppData, not here).
  RMDir /r "$INSTDIR"
  SetOutPath "$INSTDIR"
  File /r "${PUBLISHDIR}\*.*"

  ; Do not ship Plugins\ from a dirty publish folder.
  RMDir /r "$INSTDIR\Plugins"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\SpiritVale Plugin Manager"
  CreateShortCut "$SMPROGRAMS\SpiritVale Plugin Manager\SpiritVale Plugin Manager.lnk" \
    "$INSTDIR\SpiritVale.Overlay.Host.exe"
  CreateShortCut "$DESKTOP\SpiritVale Plugin Manager.lnk" \
    "$INSTDIR\SpiritVale.Overlay.Host.exe"

  ; spiritvale://install/{id} deep links
  WriteRegStr HKCU "Software\Classes\spiritvale" "" "URL:SpiritVale Plugin Manager"
  WriteRegStr HKCU "Software\Classes\spiritvale" "URL Protocol" ""
  WriteRegStr HKCU "Software\Classes\spiritvale\DefaultIcon" "" '"$INSTDIR\SpiritVale.Overlay.Host.exe",0'
  WriteRegStr HKCU "Software\Classes\spiritvale\shell\open\command" "" \
    '"$INSTDIR\SpiritVale.Overlay.Host.exe" "%1"'

  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SpiritValePluginManager" \
    "DisplayName" "SpiritVale Plugin Manager"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SpiritValePluginManager" \
    "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SpiritValePluginManager" \
    "Publisher" "MUDesigns"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SpiritValePluginManager" \
    "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SpiritValePluginManager" \
    "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SpiritValePluginManager" \
    "DisplayIcon" "$INSTDIR\SpiritVale.Overlay.Host.exe"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SpiritValePluginManager" \
    "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SpiritValePluginManager" \
    "NoRepair" 1
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\SpiritVale Plugin Manager.lnk"
  RMDir /r "$SMPROGRAMS\SpiritVale Plugin Manager"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "Software\Classes\spiritvale"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SpiritValePluginManager"
SectionEnd
