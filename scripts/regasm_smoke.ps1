$regasm = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
$dll = "C:\Program Files\SW2GZ\SW2GZ.dll"
$log = "$env:TEMP\regasm_sw2gz.log"
& $regasm /codebase "$dll" *>&1 | Out-File -FilePath $log -Encoding utf8
"exit: $LASTEXITCODE" | Out-File -FilePath $log -Append -Encoding utf8
