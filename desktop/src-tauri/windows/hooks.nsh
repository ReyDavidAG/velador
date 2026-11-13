!macro VELADOR_STOP_ENGINE
  nsExec::Exec 'schtasks.exe /End /TN "Velador\Engine"'
  StrCpy $R9 0
  velador_stop_retry:
    nsExec::Exec 'taskkill.exe /F /IM Velador.Engine.exe'
    nsExec::Exec 'taskkill.exe /F /IM mediamtx.exe'
    ClearErrors
    IfFileExists "$INSTDIR\engine\Velador.Engine.exe" 0 velador_stop_done
    Delete "$INSTDIR\engine\Velador.Engine.exe"
    Delete "$INSTDIR\engine\mediamtx.exe"
    IfErrors 0 velador_stop_done
    IntOp $R9 $R9 + 1
    IntCmp $R9 20 velador_stop_done
    Sleep 500
    Goto velador_stop_retry
  velador_stop_done:
!macroend

!macro NSIS_HOOK_PREINSTALL
  !insertmacro VELADOR_STOP_ENGINE
!macroend

!macro NSIS_HOOK_POSTINSTALL
  nsExec::Exec 'schtasks.exe /Run /TN "Velador\Engine"'
!macroend

!macro NSIS_HOOK_PREUNINSTALL
  nsExec::Exec '"$INSTDIR\engine\Velador.Engine.exe" uninstall'
  !insertmacro VELADOR_STOP_ENGINE
!macroend
