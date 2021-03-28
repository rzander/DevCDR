if (-not (get-process ROMAWOAgent -ea SilentlyContinue)) { 
    &msiexec -i https://cdnromawo.azureedge.net/rest/v2/GetFile/ROMAWOAgent/ROMAWOAgent.msi /qn REBOOT=REALLYSUPPRESS
}

#Fix Startup Type
Set-Service -Name ROMAWOAgent -StartupType Automatic -ea SilentlyContinue