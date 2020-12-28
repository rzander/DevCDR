function Test-Logging {
    <#
        .Description
        test if logging is enabled
    #>

    if (Get-Module -ListAvailable -Name WriteAnalyticsLog) { return WriteAnalyticsLog\Test-Logging  -TenantID "ROMAWO" } else { return $false }
}

function Test-Nuget {
    <#
        .Description
        Check if Nuget PackageProvider is installed
    #>

    try {
        if ([version]((Get-PackageProvider nuget | Sort-Object version)[-1]).Version -lt "2.8.5.208") { Install-PackageProvider -Name "Nuget" -Force }
    }
    catch { Install-PackageProvider -Name "Nuget" -Force }

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("Nuget")) { $global:chk.Remove("Nuget") }
    $global:chk.Add("Nuget", ((Get-PackageProvider nuget | Sort-Object version)[-1]).Version.ToString())
}

function Test-NetMetered {
    <#
        .Description
        Check if Device is using a metered network connection.
    #>

    # Source: https://www.powershellgallery.com/packages/NetMetered/1.0/Content/NetMetered.psm1
    # Created by:     Wil Taylor (wilfridtaylor@gmail.com) 
    $res = $false;
    try {
        [void][Windows.Networking.Connectivity.NetworkInformation, Windows, ContentType = WindowsRuntime]
        $networkprofile = [Windows.Networking.Connectivity.NetworkInformation]::GetInternetConnectionProfile()

        if ($networkprofile -eq $null) {
            Write-Warning "Can't find any internet connections!"
        }
        else {
            $cost = $networkprofile.GetConnectionCost()
    
            if ($cost -eq $null) {
                Write-Warning "Can't find any internet connections with a cost!"
            }
    
            if ($cost.Roaming -or $cost.OverDataLimit) {
                $res = $true
            }
            
            if ($cost.NetworkCostType -eq [Windows.Networking.Connectivity.NetworkCostType]::Unrestricted) {
                $res = $false
            }

            if ($cost.NetworkCostType -eq [Windows.Networking.Connectivity.NetworkCostType]::Fixed -or
                $cost.NetworkCostType -eq [Windows.Networking.Connectivity.NetworkCostType]::Variable) {
                $res = $true
            }
        }
    }
    catch { $res = $false }

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("Metered")) { $global:chk.Remove("Metered") }
    $global:chk.Add("Metered", $res)

    return $res
}

function Test-OneGetProvider($ProviderVersion = "1.7.2.0", $DownloadURL = "https://github.com/rzander/ruckzuck/releases/download/$($ProviderVersion)/RuckZuck.provider.for.OneGet_x64.msi" ) {
    <#
        .Description
        If missing, install latest RuckZuck Provider for OneGet...
    #>
    

    if (Get-PackageProvider -Name Ruckzuck -ea SilentlyContinue) { } else {
        if (Test-Logging) {
            Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 1000; Description = "Installing OneGet v1.7.1.3"; CustomerID = $( Get-DevcdrID ); DeviceID = $( GetMyID ) }) -LogType "DevCDRCore" 
        }
        &msiexec -i $DownloadURL /qn REBOOT=REALLYSUPPRESS 
    }

    if ((Get-PackageProvider -Name Ruckzuck).Version -lt [version]($ProviderVersion)) {
        if (Test-Logging) {
            Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 1000; Description = "Updating to OneGet v1.7.1.3"; CustomerID = $( Get-DevcdrID ); DeviceID = $( GetMyID ) }) -LogType "DevCDRCore" 
        }
        &msiexec -i $DownloadURL /qn REBOOT=REALLYSUPPRESS 
    }

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("OneGetProvider")) { $global:chk.Remove("OneGetProvider") }
    $global:chk.Add("OneGetProvider", (Get-PackageProvider -Name Ruckzuck).Version.ToString())
}

function Test-DevCDRAgent($AgentVersion = "2.0.1.51") {
    <#
        .Description
        Install or Update DevCDRAgentCore if required
    #>
    $fix = "1.0.0.7"
    if (-NOT (Get-Process ROMAWOAgent -ea SilentlyContinue)) {

        if ([version](get-item "$($env:ProgramFiles)\DevCDRAgentCore\DevCDRAgentCore.exe").VersionInfo.FileVersion -lt [version]($AgentVersion)) {
            [xml]$a = Get-Content "$($env:ProgramFiles)\DevCDRAgentCore\DevCDRAgentCore.exe.config"
            $customerId = ($a.configuration.applicationSettings."DevCDRAgent.Properties.Settings".setting | Where-Object { $_.name -eq 'CustomerID' }).value
            $ep = ($a.configuration.userSettings."DevCDRAgent.Properties.Settings".setting | Where-Object { $_.name -eq 'Endpoint' }).value

            if ($customerId) { 
                $customerId > $env:temp\customer.log
                if (Test-Logging) {
                    Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 1002; Description = "Updating DevCDRAgent to v$($AgentVersion)"; DeviceID = $( GetMyID ); CustomerID = $( Get-DevcdrID ) }) -LogType "DevCDRCore" 
                }
                &msiexec -i https://devcdrcore.azurewebsites.net/DevCDRAgentCoreNew.msi CUSTOMER="$($customerId)" /qn REBOOT=REALLYSUPPRESS  
            }
            else {
                &msiexec -i https://devcdrcore.azurewebsites.net/DevCDRAgentCoreNew.msi ENDPOINT="$($ep)" /qn REBOOT=REALLYSUPPRESS  
            }
        }

    }
    else {
        try {
            Get-ScheduledTask DevCDR | Unregister-ScheduledTask -Confirm:$False
            $scheduleObject = New-Object -ComObject Schedule.Service
            $scheduleObject.connect()
            $rootFolder = $scheduleObject.GetFolder("\")
            $rootFolder.DeleteFolder("DevCDR", $null)
        }
        catch {}
    }

    #Add Scheduled-Task to repair Agent 
    if (Get-Process DevCDRAgentCore -ea SilentlyContinue) {
        if ((Get-ScheduledTask DevCDR -ea SilentlyContinue).Description -ne "DeviceCommander fix $($fix)") {
            if (Test-Logging) {
                Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 1004; Description = "Registering Scheduled-Task for DevCDR fix $($fix)"; DeviceID = $( GetMyID ); CustomerID = $( Get-DevcdrID ) }) -LogType "DevCDRCore" 
            }
            try {
                $scheduleObject = New-Object -ComObject schedule.service
                $scheduleObject.connect()
                $rootFolder = $scheduleObject.GetFolder("\")
                $rootFolder.CreateFolder("DevCDR")
            }
            catch { }

            [xml]$a = Get-Content "$($env:ProgramFiles)\DevCDRAgentCore\DevCDRAgentCore.exe.config"
            $customerId = ($a.configuration.applicationSettings."DevCDRAgent.Properties.Settings".setting | Where-Object { $_.name -eq 'CustomerID' }).value
            $ep = ($a.configuration.userSettings."DevCDRAgent.Properties.Settings".setting | Where-Object { $_.name -eq 'Endpoint' }).value
            if ($customerId) {
                if ($ep) {
                    $arg = "if(-not (get-process DevCDRAgentCore -ea SilentlyContinue)) { `"&msiexec -i https://devcdrcore.azurewebsites.net/DevCDRAgentCoreNew.msi CUSTOMER=$($customerId) ENDPOINT=$($ep) /qn REBOOT=REALLYSUPPRESS`" }"
                    $action = New-ScheduledTaskAction -Execute 'Powershell.exe' -Argument $arg
                    $trigger = New-ScheduledTaskTrigger -Daily -At 11:25am -RandomDelay 00:25:00
                    $Stset = New-ScheduledTaskSettingsSet -RunOnlyIfIdle -IdleDuration 00:02:00 -IdleWaitTimeout 02:30:00 -WakeToRun
                }
                else {
                    $arg = "if(-not (get-process DevCDRAgentCore -ea SilentlyContinue)) { `"&msiexec -i https://devcdrcore.azurewebsites.net/DevCDRAgentCoreNew.msi CUSTOMER=$($customerId) ENDPOINT=https://devcdrcore.azurewebsites.net/chat /qn REBOOT=REALLYSUPPRESS`" }"
                    $action = New-ScheduledTaskAction -Execute 'Powershell.exe' -Argument $arg
                    $trigger = New-ScheduledTaskTrigger -Daily -At 11:25am -RandomDelay 00:25:00
                    $Stset = New-ScheduledTaskSettingsSet -RunOnlyIfIdle -IdleDuration 00:02:00 -IdleWaitTimeout 02:30:00 -WakeToRun
                }
                Register-ScheduledTask -Action $action -Settings $Stset -Trigger $trigger -TaskName "DevCDR" -Description "DeviceCommander fix $($fix)" -User "System" -TaskPath "\DevCDR" -Force
            }
            else {
                if ($ep) {
                    $arg = "if(-not (get-process DevCDRAgentCore -ea SilentlyContinue)) { `"&msiexec -i https://devcdrcore.azurewebsites.net/DevCDRAgentCoreNew.msi ENDPOINT=$($ep) /qn REBOOT=REALLYSUPPRESS`" }"
                    $action = New-ScheduledTaskAction -Execute 'Powershell.exe' -Argument $arg
                    $trigger = New-ScheduledTaskTrigger -Daily -At 11:25am -RandomDelay 00:25:00
                    $Stset = New-ScheduledTaskSettingsSet -RunOnlyIfIdle -IdleDuration 00:02:00 -IdleWaitTimeout 02:30:00 -WakeToRun
                }
                else {
                    $arg = "if(-not (get-process DevCDRAgentCore -ea SilentlyContinue)) { `"&msiexec -i https://devcdrcore.azurewebsites.net/DevCDRAgentCoreNew.msi ENDPOINT=https://devcdrcore.azurewebsites.net/chat /qn REBOOT=REALLYSUPPRESS`" }"
                    $action = New-ScheduledTaskAction -Execute 'Powershell.exe' -Argument $arg
                    $trigger = New-ScheduledTaskTrigger -Daily -At 11:25am -RandomDelay 00:25:00
                    $Stset = New-ScheduledTaskSettingsSet -RunOnlyIfIdle -IdleDuration 00:02:00 -IdleWaitTimeout 02:30:00 -WakeToRun
                }
                Register-ScheduledTask -Action $action -Settings $Stset -Trigger $trigger -TaskName "DevCDR" -Description "DeviceCommander fix $($fix)" -User "System" -TaskPath "\DevCDR" -Force
            }
        }
    }

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("DevCDRAgent")) { $global:chk.Remove("DevCDRAgent") }
    if ($global:chk.ContainsKey("ROMAWOAgent")) { $global:chk.Remove("ROMAWOAgent") }
    if (Get-Process DevCDRAgentCore -ea SilentlyContinue) {
        $global:chk.Add("DevCDRAgent", (get-item "$($env:ProgramFiles)\DevCDRAgentCore\DevCDRAgentCore.exe").VersionInfo.FileVersion )
        $global:chk.Add("ROMAWOAgent", (get-item "$($env:ProgramFiles)\DevCDRAgentCore\DevCDRAgentCore.exe").VersionInfo.FileVersion )
    }
    if (Get-Process ROMAWOAgent -ea SilentlyContinue) {
        $global:chk.Add("DevCDRAgent", (get-item "$($env:ProgramFiles)\ROMAWO Agent\ROMAWOAgent.exe").VersionInfo.FileVersion )
        $global:chk.Add("ROMAWOAgent", (get-item "$($env:ProgramFiles)\ROMAWO Agent\ROMAWOAgent.exe").VersionInfo.FileVersion )
    }
}

function Test-ROMAWOAgent($AgentVersion = "2.1.0.0") {
    <#
        .Description
        Install or Update ROMAWO if required
    #>
    $fix = "1.0.0.0"

    if ([version](get-item "$($env:ProgramFiles)\ROMAWO Agent\ROMAWOAgent.exe").VersionInfo.FileVersion -lt [version]($AgentVersion)) {
        [xml]$a = Get-Content "$($env:ProgramFiles)\ROMAWO Agent\ROMAWOAgent.exe.config"
        $customerId = ($a.configuration.applicationSettings."DevCDRAgent.Properties.Settings".setting | Where-Object { $_.name -eq 'CustomerID' }).value
        $ep = ($a.configuration.userSettings."DevCDRAgent.Properties.Settings".setting | Where-Object { $_.name -eq 'Endpoint' }).value

        if ($customerId) { 
            $customerId > $env:temp\customer.log
            if (Test-Logging) {
                Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 1002; Description = "Updating ROMAWO Agent to v$($AgentVersion)"; DeviceID = $( GetMyID ); CustomerID = $( Get-DevcdrID ) }) -LogType "ROMAWO"  -TenantID "ROMAWO"
            }
            &msiexec -i https://cdnromawo.azureedge.net/rest/v2/GetFile/ROMAWOAgent/ROMAWOAgent.msi CUSTOMER="$($customerId)" /qn REBOOT=REALLYSUPPRESS  
        }
        else {
            $customerId = Get-DevcdrID
            if ($customerId) {
                &msiexec -i https://cdnromawo.azureedge.net/rest/v2/GetFile/ROMAWOAgent/ROMAWOAgent.msi ENDPOINT="$(Get-DevCDREP)/chat" CUSTOMER="$($customerId)" /qn REBOOT=REALLYSUPPRESS  
            }
            else {
                &msiexec -i https://cdnromawo.azureedge.net/rest/v2/GetFile/ROMAWOAgent/ROMAWOAgent.msi /qn REBOOT=REALLYSUPPRESS  
            }
        }
    }


    #Add Scheduled-Task to repair Agent 
    if ((Get-ScheduledTask ROMAWO -ea SilentlyContinue).Description -ne "ROMAWO Agent fix $($fix)") {
        if (Test-Logging) {
            Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 1004; Description = "Registering Scheduled-Task for ROMAWO fix $($fix)"; DeviceID = $( GetMyID ); CustomerID = $( Get-DevcdrID ) }) -LogType "ROMAWO" -TenantID "ROMAWO" 
        }
        try {
            $scheduleObject = New-Object -ComObject schedule.service
            $scheduleObject.connect()
            $rootFolder = $scheduleObject.GetFolder("\")
            $rootFolder.CreateFolder("ROMAWO")
        }
        catch { }

        [xml]$a = Get-Content "$($env:ProgramFiles)\ROMAWO Agent\ROMAWOAgent.exe.config"
        $customerId = ($a.configuration.applicationSettings."DevCDRAgent.Properties.Settings".setting | Where-Object { $_.name -eq 'CustomerID' }).value
        $ep = ($a.configuration.userSettings."DevCDRAgent.Properties.Settings".setting | Where-Object { $_.name -eq 'Endpoint' }).value
        if ($customerId) {
            if ($ep) {
                $arg = "if(-not (get-process ROMAWOAgent -ea SilentlyContinue)) { `"&msiexec -i https://cdnromawo.azureedge.net/rest/v2/GetFile/ROMAWOAgent/ROMAWOAgent.msi CUSTOMER=$($customerId) ENDPOINT=$($ep) /qn REBOOT=REALLYSUPPRESS`" }"
                $action = New-ScheduledTaskAction -Execute 'Powershell.exe' -Argument $arg
                $trigger = New-ScheduledTaskTrigger -Daily -At 11:25am -RandomDelay 00:25:00
                $Stset = New-ScheduledTaskSettingsSet -RunOnlyIfIdle -IdleDuration 00:02:00 -IdleWaitTimeout 02:30:00 -WakeToRun
            }
            else {
                $arg = "if(-not (get-process ROMAWOAgent -ea SilentlyContinue)) { `"&msiexec -i https://cdnromawo.azureedge.net/rest/v2/GetFile/ROMAWOAgent/ROMAWOAgent.msi CUSTOMER=$($customerId) /qn REBOOT=REALLYSUPPRESS`" }"
                $action = New-ScheduledTaskAction -Execute 'Powershell.exe' -Argument $arg
                $trigger = New-ScheduledTaskTrigger -Daily -At 11:25am -RandomDelay 00:25:00
                $Stset = New-ScheduledTaskSettingsSet -RunOnlyIfIdle -IdleDuration 00:02:00 -IdleWaitTimeout 02:30:00 -WakeToRun
            }
            Register-ScheduledTask -Action $action -Settings $Stset -Trigger $trigger -TaskName "ROMAWO" -Description "ROMAWO Agent fix $($fix)" -User "System" -TaskPath "\ROMAWO" -Force
            Get-ScheduledTask DevCDR | Unregister-ScheduledTask -Confirm:$False
        }
        else {
            if ($ep) {
                $arg = "if(-not (get-process ROMAWOAgent -ea SilentlyContinue)) { `"&msiexec -i https://cdnromawo.azureedge.net/rest/v2/GetFile/ROMAWOAgent/ROMAWOAgent.msi ENDPOINT=$($ep) /qn REBOOT=REALLYSUPPRESS`" }"
                $action = New-ScheduledTaskAction -Execute 'Powershell.exe' -Argument $arg
                $trigger = New-ScheduledTaskTrigger -Daily -At 11:25am -RandomDelay 00:25:00
                $Stset = New-ScheduledTaskSettingsSet -RunOnlyIfIdle -IdleDuration 00:02:00 -IdleWaitTimeout 02:30:00 -WakeToRun
            }
            else {
                $arg = "if(-not (get-process ROMAWOAgent -ea SilentlyContinue)) { `"&msiexec -i https://cdnromawo.azureedge.net/rest/v2/GetFile/ROMAWOAgent/ROMAWOAgent.msi /qn REBOOT=REALLYSUPPRESS`" }"
                $action = New-ScheduledTaskAction -Execute 'Powershell.exe' -Argument $arg
                $trigger = New-ScheduledTaskTrigger -Daily -At 11:25am -RandomDelay 00:25:00
                $Stset = New-ScheduledTaskSettingsSet -RunOnlyIfIdle -IdleDuration 00:02:00 -IdleWaitTimeout 02:30:00 -WakeToRun
            }
            #Do not add fix Version as the Task has to be recreated with customerID and EP
            Register-ScheduledTask -Action $action -Settings $Stset -Trigger $trigger -TaskName "ROMAWO" -Description "ROMAWO Agent fix" -User "System" -TaskPath "\ROMAWO" -Force
            Get-ScheduledTask DevCDR | Unregister-ScheduledTask -Confirm:$False
        }
    }

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("DevCDRAgent")) { $global:chk.Remove("DevCDRAgent") }
    if ($global:chk.ContainsKey("ROMAWOAgent")) { $global:chk.Remove("ROMAWOAgent") }
    $global:chk.Add("DevCDRAgent", (get-item "$($env:ProgramFiles)\ROMAWO Agent\ROMAWOAgent.exe").VersionInfo.FileVersion )
    $global:chk.Add("ROMAWOAgent", (get-item "$($env:ProgramFiles)\ROMAWO Agent\ROMAWOAgent.exe").VersionInfo.FileVersion )
}

#to be replaced with Remove-Administrators...
function Set-Administrators {
    <#
        .Description
        remove all local admins except the built in Azure Global-Admins and Azure Device-Admins. ExcludeSID can contain wildcards to exclude some SID's from removal. 
    #>
    
    [CmdletBinding()]Param(
        [parameter(Mandatory = $false)]
        [array]
        $ExcludeSID = @(),
        [parameter(Mandatory = $false)] [switch] $Remove
    )


    if ($ExcludeSID.count -eq 0) {
        if ( (Get-WmiObject Win32_OperatingSystem).ProductType -ne 2) { 
            $localgroup = (Get-LocalGroup -SID "S-1-5-32-544").Name
            $Group = [ADSI]"WinNT://localhost/$LocalGroup, group"
            $members = $Group.psbase.Invoke("Members")
            $members | ForEach-Object { $_.GetType().InvokeMember("ADSPath", "GetProperty", $null, $_, $null) } | Where-Object { $_ -notlike "WinNT://S-1-12-1-*" } | ForEach-Object { try { $Group.Remove($_) } catch {} } 
        }
    }
    else {
        if ( (Get-WmiObject Win32_OperatingSystem).ProductType -ne 2) { 
            $localgroup = (Get-LocalGroup -SID "S-1-5-32-544").Name
            $Group = [ADSI]"WinNT://localhost/$LocalGroup, group"
            $members = $Group.psbase.Invoke("Members")
            $members | ForEach-Object { 
                $script:rem = $true
                $script:member = $_; 
                $binarySID = ($_.GetType().InvokeMember("objectSID", "GetProperty", $null, $_, $null));
                $stringSID = (New-Object System.Security.Principal.SecurityIdentifier($binarySID, 0)).Value; 
           
                $ExcludeSID | ForEach-Object { 
                    if ($stringSID -like $_) { 
                        $script:rem = $false
                    }
                } 
            
                if ($script:rem) {
                    if ($Remove.ToBool()) {
                        try { 
                            $grp = ($script:member.GetType().InvokeMember("ADSPath", "GetProperty", $null, $_, $null))
                            $Group.Remove($grp) 
                            if (Test-Logging) {
                                Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 1101; Description = "removed from local Admins: $($grp)"; CustomerID = $( Get-DevcdrID ); DeviceID = $( GetMyID ) }) -LogType "ROMAWO" -TenantID "ROMAWO"
                            }
                        }
                        catch {} 
                    }
                    else {
                        Write-Output $stringSID
                    }
                }
            }
        } 
    } 
}

function Remove-Administrators {
    <#
        .Description
        remove all local admins except the built in Azure Global-Admins and Azure Device-Admins. ExcludeSID can contain wildcards to exclude some SID's from removal. 
    #>
    
    [CmdletBinding()]Param(
        [parameter(Mandatory = $false)]
        [array]
        $ExcludeSID = @(),
        [parameter(Mandatory = $false)] [switch] $Remove
    )


    if ($ExcludeSID.count -eq 0) {
        if ( (Get-WmiObject Win32_OperatingSystem).ProductType -ne 2) { 
            $localgroup = (Get-LocalGroup -SID "S-1-5-32-544").Name
            $Group = [ADSI]"WinNT://localhost/$LocalGroup, group"
            $members = $Group.psbase.Invoke("Members")
            $members | ForEach-Object { $_.GetType().InvokeMember("ADSPath", "GetProperty", $null, $_, $null) } | Where-Object { $_ -notlike "WinNT://S-1-12-1-*" } | ForEach-Object { try { $Group.Remove($_) } catch {} } 
        }
    }
    else {
        if ( (Get-WmiObject Win32_OperatingSystem).ProductType -ne 2) { 
            $localgroup = (Get-LocalGroup -SID "S-1-5-32-544").Name
            $Group = [ADSI]"WinNT://localhost/$LocalGroup, group"
            $members = $Group.psbase.Invoke("Members")
            $members | ForEach-Object { 
                $script:rem = $true
                $script:member = $_; 
                $binarySID = ($_.GetType().InvokeMember("objectSID", "GetProperty", $null, $_, $null));
                $stringSID = (New-Object System.Security.Principal.SecurityIdentifier($binarySID, 0)).Value; 
           
                $ExcludeSID | ForEach-Object { 
                    if ($stringSID -like $_) { 
                        $script:rem = $false
                    }
                } 
            
                if ($script:rem) {
                    if ($Remove.ToBool()) {
                        try { 
                            $grp = ($script:member.GetType().InvokeMember("ADSPath", "GetProperty", $null, $_, $null))
                            $Group.Remove($grp) 
                            if (Test-Logging) {
                                Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 1101; Description = "removed from local Admins: $($grp)"; CustomerID = $( Get-DevcdrID ); DeviceID = $( GetMyID ) }) -LogType "ROMAWO" -TenantID "ROMAWO"
                            }
                        }
                        catch {} 
                    }
                    else {
                        Write-Output $stringSID
                    }
                }
            }
        } 
    } 
}

function Set-LocalAdmin($disableAdmin = $true, $randomizeAdmin = $true) {
    <#
        .Description
         disable local Admin account or randomize PW if older than 4 hours
    #>

    #Skip fix if running on a DC
    if ( (Get-WmiObject Win32_OperatingSystem).ProductType -ne 2) {
        $pwlastset = (Get-LocalUser | Where-Object { $_.SID -like "S-1-5-21-*-500" }).PasswordLastSet
        if (!$pwlastset) { $pwlastset = Get-Date -Date "1970-01-01 00:00:00Z" }
        if (((get-date) - $pwlastset).TotalHours -gt 4) {
            if ($disableAdmin) {
                (Get-LocalUser | Where-Object { $_.SID -like "S-1-5-21-*-500" }) | Disable-LocalUser
            }
            else {
                (Get-LocalUser | Where-Object { $_.SID -like "S-1-5-21-*-500" }) | Enable-LocalUser 
            }

            if ($randomizeAdmin) {
                if (((get-date) - $pwlastset).TotalHours -gt 12) {
                    $pw = get-random -count 12 -input (35..37 + 45..46 + 48..57 + 65..90 + 97..122) | ForEach-Object -begin { $aa = $null } -process { $aa += [char]$_ } -end { $aa }; 
                    (Get-LocalUser | Where-Object { $_.SID -like "S-1-5-21-*-500" }) | Set-LocalUser -Password (ConvertTo-SecureString -String $pw -AsPlainText -Force)

                    if (Test-Logging) {
                        Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 1001; Description = "AdminPW:" + $pw; CustomerID = $( Get-DevcdrID ); DeviceID = $( GetMyID ) }) -LogType "DevCDR" -TenantID "DevCDR"
                    }
                }
            }
        }
    }
}

function Test-LocalAdmin {
    <#
        .Description
         count local Admins
    #>

    $locAdmin = @()  
    #Skip fix if running on a DC
    if ( (Get-WmiObject Win32_OperatingSystem).ProductType -ne 2) {
        
        $admingroup = (Get-WmiObject -Class Win32_Group -Filter "LocalAccount='True' AND SID='S-1-5-32-544'").Name
       
        $groupconnection = [ADSI]("WinNT://localhost/$admingroup,group")
        $members = $groupconnection.Members()
        ForEach ($member in $members) {
            $name = $member.GetType().InvokeMember("Name", "GetProperty", $NULL, $member, $NULL)
            $class = $member.GetType().InvokeMember("Class", "GetProperty", $NULL, $member, $NULL)
            $bytes = $member.GetType().InvokeMember("objectsid", "GetProperty", $NULL, $member, $NULL)
            $sid = New-Object Security.Principal.SecurityIdentifier ($bytes, 0)
            $result = New-Object -TypeName psobject
            $result | Add-Member -MemberType NoteProperty -Name Name -Value $name
            $result | Add-Member -MemberType NoteProperty -Name ObjectClass -Value $class
            $result | Add-Member -MemberType NoteProperty -Name id -Value $sid.Value.ToString()
            #Exclude locaAdmin and DomainAdmins
            if (($result.id -notlike "S-1-5-21-*-500" ) -and ($result.id -notlike "S-1-5-21-*-512" )) {
                $locAdmin = $locAdmin + $result;
            }
        }
    }

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("Admins")) { $global:chk.Remove("Admins") }
    $global:chk.Add("Admins", $locAdmin.Count)
}

#depreciated by Set-WOL
function Test-WOL {
    <#
        .Description
        Enable WOL on NetworkAdapters
    #>
    $bRes = $false
    $niclist = Get-NetAdapter | Where-Object { ($_.MediaConnectionState -eq "Connected") -and (($_.name -match "Ethernet") -or ($_.name -match "local area connection")) }
    $niclist | ForEach-Object { 
        $nic = $_
        $nicPowerWake = Get-WmiObject MSPower_DeviceWakeEnable -Namespace root\wmi | Where-Object { $_.instancename -match [regex]::escape($nic.PNPDeviceID) }
        If ($nicPowerWake.Enable -eq $true) { }
        Else {
            try {
                $nicPowerWake.Enable = $True
                $nicPowerWake.psbase.Put() 
                $bRes = $true;
            }
            catch { }
        }
        $nicMagicPacket = Get-WmiObject MSNdis_DeviceWakeOnMagicPacketOnly -Namespace root\wmi | Where-Object { $_.instancename -match [regex]::escape($nic.PNPDeviceID) }
        If ($nicMagicPacket.EnableWakeOnMagicPacketOnly -eq $true) { }
        Else {
            try {
                $nicMagicPacket.EnableWakeOnMagicPacketOnly = $True
                $nicMagicPacket.psbase.Put()
                $bRes = $true;
            }
            catch { }
        }
    }

    
    #Enable WOL broadcasts
    if ((Get-NetFirewallRule -DisplayName "WOL" -ea SilentlyContinue).count -gt 1) {
        #Cleanup WOl Rules
        Remove-NetFirewallRule -DisplayName "WOL" -ea SilentlyContinue
    }
    if ((Get-NetFirewallRule -DisplayName "WOL" -ea SilentlyContinue).count -eq 0) {
        #Add WOL Rule
        New-NetFirewallRule -DisplayName "WOL" -Direction Outbound -RemotePort 9 -Protocol UDP -Action Allow
    }

    #if ($null -eq $global:chk) { $global:chk = @{ } }
    #if ($global:chk.ContainsKey("WOL")) { $global:chk.Remove("WOL") }
    #$global:chk.Add("WOL", $bRes)
}

#depreciated by Set-FastBoot
function Test-FastBoot($Value = 0) {
    <#
        .Description
        Disable FastBoot
    #>

    New-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name 'HiberbootEnabled' -Value $Value -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("FastBoot")) { $global:chk.Remove("FastBoot") }
    $global:chk.Add("FastBoot", $Value)
}

#depreciated by Set-DeliveryOptimization
function Test-DeliveryOptimization {
    <#
        .Description
        restrict Peer Selection on DeliveryOptimization
    #>

    #Create the key if missing 
    If ((Test-Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization') -eq $false ) { New-Item -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization' -force -ea SilentlyContinue } 

    #Enable Setting and Restrict to local Subnet only
    Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization' -Name 'DORestrictPeerSelectionBy' -Value 1 -ea SilentlyContinue 

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("DO")) { $global:chk.Remove("DO") }
    $global:chk.Add("DO", 1)
}

function Test-locked {
    <#
        .Description
        check if device is locked
    #>
    $bRes = $false
    If (get-process logonui -ea SilentlyContinue) { $bRes = $true } else { $bRes = $false }
    
    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("locked")) { $global:chk.Remove("locked") }
    $global:chk.Add("locked", $bRes)

    return $bRes
}

function Test-Software {
    <#
        .Description
        Check for missing SW Updates
    #>

    #Find Software Updates
    $updates = Find-Package -ProviderName RuckZuck -Updates | Select-Object PackageFilename

    if ($updates) {
        if ($null -eq $global:chk) { $global:chk = @{ } }
        if ($global:chk.ContainsKey("RZUpdates")) { $global:chk.Remove("RZUpdates") }
        if ($updates) { $global:chk.Add("RZUpdates", $updates.PackageFilename -join ';') } else { $global:chk.Add("RZUpdates", "") }
    }
    else {
        if ($null -eq $global:chk) { $global:chk = @{ } }
        if ($global:chk.ContainsKey("RZUpdates")) { $global:chk.Remove("RZUpdates") }
        if ($updates) { $global:chk.Add("RZUpdates", "") } else { $global:chk.Add("RZUpdates", "") }   
    }
}

function Update-Software {
    <#
        .Description
        Update a specific list of Softwares
    #>
    param( 
        [parameter(Mandatory = $true)] [string[]] $SWList, 
        [parameter(Mandatory = $true)] [boolean] $CheckMeteredNetwork
    )

    if ($CheckMeteredNetwork) {
        if (Test-NetMetered) { return }
    }

    #Find Software Updates
    $updates = Find-Package -ProviderName RuckZuck -Updates | Select-Object PackageFilename | Sort-Object { Get-Random }
    $i = 0
    #Update only managed Software
    $SWList | ForEach-Object { 
        if ($updates.PackageFilename -contains $_) { 
            if (Test-Logging) {
                Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 2000; Description = "RuckZuck updating: $($_)"; CustomerID = $( Get-DevcdrID ); DeviceID = $( GetMyID ) }) -LogType "DevCDR" -TenantID "DevCDR"
            }
            "Updating: " + $_ ;
            Install-Package -ProviderName RuckZuck "$($_)" -ea SilentlyContinue
        }
        else { $i++ }
    }

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("SoftwareUpdates")) { $global:chk.Remove("SoftwareUpdates") }
    if ($updates) { $global:chk.Add("SoftwareUpdates", $updates.PackageFilename.count) } else { $global:chk.Add("SoftwareUpdates", 0) }
}

function Test-Temp {
    <#
        .Description
        Cleanup %WINDIR%\Temp if more than 100 Files are detected.
    #>

    if ((Get-ChildItem "$($env:windir)\Temp\*" -Recurse).Count -gt 100) {
        Remove-Item "$($env:windir)\Temp\*" -Force -Recurse -Exclude devcdrcore.log -ea SilentlyContinue
    }

    #if ($null -eq $global:chk) { $global:chk = @{ } }
    #if ($global:chk.ContainsKey("Temp")) { $global:chk.Remove("Temp") }
    #$global:chk.Add("Temp ", $true)
}

Function Test-Defender($Age = 7) {
    <#
        .Description
        Run Defender Quickscan if last scan is older than $Age days
    #>
    if ((Get-WmiObject -Namespace root\SecurityCenter2 -Query "SELECT * FROM AntiVirusProduct" -ea SilentlyContinue).displayName.count -eq 1) {
        $ScanAge = (Get-MpComputerStatus).QuickScanAge
        if ($ScanAge -ge $Age) { start-process "$($env:ProgramFiles)\Windows Defender\MpCmdRun.exe" -ArgumentList '-Scan -ScanType 1' }

        if ($null -eq $global:chk) { $global:chk = @{ } }
        if ($global:chk.ContainsKey("DefenderScanAge")) { $global:chk.Remove("DefenderScangAge") }
        $global:chk.Add("DefenderScanAge", $ScanAge)
    }
    else {
        if ($null -eq $global:chk) { $global:chk = @{ } }
        if ($global:chk.ContainsKey("DefenderScanAge")) { $global:chk.Remove("DefenderScanAge") }
        $global:chk.Add("DefenderScanAge", 999)
    }
}

Function Test-Bitlocker {
    <#
        .Description
        Check if BitLocker is enabled
    #>
    $bRes = "Off"
    try {
        if ((Get-BitLockerVolume C:).ProtectionStatus -eq "On") { $bRes = (Get-BitLockerVolume C:).EncryptionMethod.ToString() }
    }
    catch { }

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("Bitlocker")) { $global:chk.Remove("Bitlocker") }
    $global:chk.Add("Bitlocker", $bRes)
}

Function Test-DiskSpace {
    <#
        .Description
        Check free Disk-Space
    #>

    #Get FreeSpace in %
    $c = get-psdrive C
    $free = [math]::Round((10 / (($c).Used + ($c).Free) * ($c).Free)) * 10

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("FreeSpace")) { $global:chk.Remove("FreeSpace") }
    $global:chk.Add("FreeSpace", $free)
}

Function Test-TPM {
    <#
        .Description
        Check TPM Status
    #>

    $res = "No"
    #Get FreeSpace in %
    $tpm = Get-Tpm -ea SilentlyContinue
    if ($tpm) {
        if ($tpm.TpmReady) { $res = "Ready" }
        if ($tpm.LockedOut) { $res = "LockedOut" }
    }

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("TPM")) { $global:chk.Remove("TPM") }
    $global:chk.Add("TPM", $res)
}

Function Test-SecureBoot {
    <#
        .Description
        Check TPM Status
    #>

    $res = Confirm-SecureBootUEFI

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("SecureBoot")) { $global:chk.Remove("SecureBoot") }
    $global:chk.Add("SecureBoot", $res)
}

Function Test-Office {
    <#
        .Description
        Check Office Status
    #>

    $O365 = (Get-ItemProperty HKLM:SOFTWARE\Microsoft\Office\ClickToRun\Configuration -ea SilentlyContinue).VersionToReport

    if (-NOT $O365) {
        $O365 = (Get-ItemProperty HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\* | Where-Object { $_.DisplayName -like "Microsoft Office Professional Plus *" }).DisplayVersion | Select-Object -First 1
    }

    if (-NOT $O365) {
        $O365 = (Get-ItemProperty HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\* | Where-Object { $_.DisplayName -like "Microsoft Office Standard *" }).DisplayVersion | Select-Object -First 1
    }

    if (-NOT $O365) {
        $O365 = (Get-ItemProperty HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\* | Where-Object { $_.DisplayName -like "Microsoft Office Home *" }).DisplayVersion | Select-Object -First 1
    }

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("Office")) { $global:chk.Remove("Office") }
    $global:chk.Add("Office", $O365 )
}

Function Test-DefenderThreats {
    <#
        .Description
        Check Virus Threat Status
    #>

    if ((Get-WmiObject -Namespace root\SecurityCenter2 -Query "SELECT * FROM AntiVirusProduct" -ea SilentlyContinue).displayName.count -eq 1) {
        $Threats = Get-MpThreat -ea SilentlyContinue
        if ($Threats) {
            if ($null -eq $global:chk) { $global:chk = @{ } }
            if ($global:chk.ContainsKey("AVThreats")) { $global:chk.Remove("AVThreats") }
            if ($Threats.count) {
                $global:chk.Add("AVThreats", $Threats.count)
            }
            else {
                $global:chk.Add("AVThreats", 1) 
            }
        }
        else {
            if ($null -eq $global:chk) { $global:chk = @{ } }
            if ($global:chk.ContainsKey("AVThreats")) { $global:chk.Remove("AVThreats") }
            $global:chk.Add("AVThreats", 0)     
        }
    }
    else {
        $Threats = $null
        if ($null -eq $global:chk) { $global:chk = @{ } }
        if ($global:chk.ContainsKey("AVThreats")) { $global:chk.Remove("AVThreats") }
        $global:chk.Add("AVThreats", -1) 
    }
}

Function Test-OSVersion {
    <#
        .Description
        Check OS Version
    #>

    $UBR = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -Name UBR).UBR
    $Version = (Get-WMIObject win32_operatingsystem).Version
    $Caption = (Get-WMIObject win32_operatingsystem).caption

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("OSVersion")) { $global:chk.Remove("OSVersion") }
    $global:chk.Add("OSVersion", $Version + "." + $UBR )

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("OS")) { $global:chk.Remove("OS") }
    $global:chk.Add("OS", $Caption )
}

Function Test-ASR {
    <#
        .Description
        Check Attack Surface Reduction
    #>

    $i = ((Get-MpPreference).AttackSurfaceReductionRules_Actions | Where-Object { $_ -eq 1 } ).count
    if ($i -gt 0) {
        if ($null -eq $global:chk) { $global:chk = @{ } }
        if ($global:chk.ContainsKey("ASR")) { $global:chk.Remove("ASR") }
        $global:chk.Add("ASR", $i )
    }
    else {
        if ($null -eq $global:chk) { $global:chk = @{ } }
        if ($global:chk.ContainsKey("ASR")) { $global:chk.Remove("ASR") }
        $global:chk.Add("ASR", 0)
    }
}

Function Test-Firewall {
    <#
        .Description
        Check Windows Firewall
    #>

    $i = ((Get-NetFirewallProfile).enabled | Where-Object { $_ -eq $true } ).count
    if ($i -gt 0) {
        if ($null -eq $global:chk) { $global:chk = @{ } }
        if ($global:chk.ContainsKey("FW")) { $global:chk.Remove("FW") }
        $global:chk.Add("FW", $i )
    }
}

Function Test-WU {
    <#
        .Description
        Check missing Windows Updates
    #>

    try {
        if (Get-InstalledModule -Name PSWindowsUpdate -MinimumVersion "2.2.0.2" -ea SilentlyContinue) { } else {
            set-executionpolicy bypass -Force
            Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.208 -Force
            Set-PSRepository -Name PSGallery -InstallationPolicy Trusted 
            Install-Module PSWindowsUpdate -Force
        }

        $upd = Get-WUList -MicrosoftUpdate

        if ($upd) {
            if ($null -eq $global:chk) { $global:chk = @{ } }
            if ($global:chk.ContainsKey("WU")) { $global:chk.Remove("WU") }
            $global:chk.Add("WU", $upd.count)
        }
        else {
            if ($null -eq $global:chk) { $global:chk = @{ } }
            if ($global:chk.ContainsKey("WU")) { $global:chk.Remove("WU") }
            $global:chk.Add("WU", 0) 
        }
        
    }
    catch {
        if ($null -eq $global:chk) { $global:chk = @{ } }
        if ($global:chk.ContainsKey("WU")) { $global:chk.Remove("WU") }
        $global:chk.Add("WU", -1) 
    }
}

Function Test-AppLocker {
    <#
        .Description
        Check if AppLocker is configured
    #>

    try {
        $AL = (Get-AppLockerPolicy -Effective).RuleCollections.count
        if ($null -eq $global:chk) { $global:chk = @{ } }
        if ($global:chk.ContainsKey("AppLocker")) { $global:chk.Remove("AppLocker") }
        $global:chk.Add("AppLocker", $AL) 
    }
    catch {
        if ($null -eq $global:chk) { $global:chk = @{ } }
        if ($global:chk.ContainsKey("AppLocker")) { $global:chk.Remove("AppLocker") }
        $global:chk.Add("AppLocker", -1) 
    }
}

Function Set-EdgeChromium {
    <#
        .Description
        Configure Edge Chromium...
    #>
    [CmdletBinding()]Param(
        [parameter(Mandatory = $false)]
        [String]
        $HomePageURL = "",
        [parameter(Mandatory = $false)]
        [String]
        $RestrictUserDomain = "",
        [parameter(Mandatory = $false)]
        [bool]
        $Google = $true,
        [parameter(Mandatory = $false)]
        [int]
        $PolicyRevision = 0,
        [parameter(Mandatory = $false)]
        [switch]$Force,
        [parameter(Mandatory = $false)]
        [switch]$Remove
    )


    #Fake MDM
    #https://hitco.at/blog/apply-edge-policies-for-non-domain-joined-devices/
    if ($HomePageURL -and (Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Provisioning\OMADM\Accounts' -ea SilentlyContinue).count -eq 0) {
        
        #  # Fake MDM-Enrollment - Key 1 of 2 - let a Win10 v1809, v1903, v1909 Machine "feel" MDM-Managed
        if ((Test-Path -LiteralPath "HKLM:\SOFTWARE\Microsoft\Enrollments\FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF") -ne $true) { New-Item "HKLM:\SOFTWARE\Microsoft\Enrollments\FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF" -force -ea SilentlyContinue | Out-Null };

        #  # Fake MDM-Enrollment - Key 2 of 2 - let a Win10 v1809, v1903, v1909 Machine "feel" MDM-Managed
        if ((Test-Path -LiteralPath "HKLM:\SOFTWARE\Microsoft\Provisioning\OMADM\Accounts\FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF") -ne $true) { New-Item "HKLM:\SOFTWARE\Microsoft\Provisioning\OMADM\Accounts\FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF" -force -ea SilentlyContinue | Out-Null };
        New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Enrollments\FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF' -Name 'EnrollmentState' -Value 0x00000001  -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;
        New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Enrollments\FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF' -Name 'EnrollmentType' -Value 0x00000000  -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;
        New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Enrollments\FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF' -Name 'IsFederated' -Value 0 -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;
        New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Provisioning\OMADM\Accounts\FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF' -Name 'Flags' -Value 14089087 -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;
        New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Provisioning\OMADM\Accounts\FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF' -Name 'AcctUId' -Value '0x000000000000000000000000000000000000000000000000000000000000000000000000' -PropertyType String -Force -ea SilentlyContinue | Out-Null;
        New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Provisioning\OMADM\Accounts\FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF' -Name 'RoamingCount' -Value 0 -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;
        New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Provisioning\OMADM\Accounts\FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF' -Name 'SslClientCertReference' -Value 'MY;User;0000000000000000000000000000000000000000' -PropertyType String -Force -ea SilentlyContinue | Out-Null;
        New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Provisioning\OMADM\Accounts\FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF' -Name 'ProtoVer' -Value '1.2' -PropertyType String -Force -ea SilentlyContinue | Out-Null;
    }

    if (((Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -ea SilentlyContinue).EdgePolicy -ge $PolicyRevision) -and -NOT $Force.ToBool()) {  } else {
        #Create the key if missing 
        If ((Test-Path 'HKLM:\Software\Policies\Microsoft\Edge') -eq $false ) { New-Item -Path 'HKLM:\Software\Policies\Microsoft\Edge' -force -ea SilentlyContinue | Out-Null } 
        If ((Test-Path 'HKLM:\Software\Policies\Microsoft\Edge\Recommended') -eq $false ) { New-Item -Path 'HKLM:\Software\Policies\Microsoft\Edge\Recommended' -force -ea SilentlyContinue | Out-Null } 
        If ((Test-Path 'HKLM:\Software\Policies\Microsoft\Edge\Recommended\RestoreOnStartupURLs') -eq $false ) { New-Item -Path 'HKLM:\Software\Policies\Microsoft\Edge\Recommended\RestoreOnStartupURLs' -force -ea SilentlyContinue | Out-Null } 
        If ((Test-Path 'HKLM:\Software\Policies\Microsoft\EdgeUpdate') -eq $false ) { New-Item -Path 'HKLM:\Software\Policies\Microsoft\EdgeUpdate' -force -ea SilentlyContinue | Out-Null } 
    
        #Action to take on startup
        Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Edge\Recommended' -Name 'RestoreOnStartup' -Value 4 -ea SilentlyContinue 
    
        #Configure the home page URL
        Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Edge\Recommended' -Name 'HomepageLocation' -Value "$($HomePageURL)" -ea SilentlyContinue 
        #Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Edge' -Name 'HomepageLocation' -Value "$($HomePageURL)" -ea SilentlyContinue 
    
        #Set the new tab page as the home page
        Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Edge\Recommended' -Name 'HomepageIsNewTabPage' -Value 0 -ea SilentlyContinue 
    
        #Show Home button on toolbar
        Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Edge\Recommended' -Name 'ShowHomeButton' -Value 1 -ea SilentlyContinue 
    
        #Sites to open when the browser starts
        Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Edge\Recommended\RestoreOnStartupURLs' -Name '1' -Value "$($HomePageURL)" -ea SilentlyContinue 
    
        #Hide the First-run experience and splash screen
        Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Edge' -Name 'HideFirstRunExperience' -Value 1 -ea SilentlyContinue 
    
        if ($RestrictUserDomain) {
            #Restrict which accounts can be used as Microsoft Edge primary accounts
            Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Edge' -Name 'RestrictSigninToPattern' -Value ".*@$($RestrictUserDomain)" -ea SilentlyContinue 
        }

        #Prevent Desktop Shortcut creation upon install default
        Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\EdgeUpdate' -Name 'CreateDesktopShortcutDefault' -Value 0 -ea SilentlyContinue 

        if ($Google) {
            #Set Google as Search Provider
            Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Edge\Recommended' -Name 'DefaultSearchProviderEnabled' -Value 1 -ea SilentlyContinue 
            Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Edge\Recommended' -Name 'DefaultSearchProviderName' -Value 'Google' -ea SilentlyContinue 
            Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Edge\Recommended' -Name 'DefaultSearchProviderSearchURL' -Value '{google:baseURL}search?q={searchTerms}&{google:RLZ}{google:originalQueryForSuggestion}{google:assistedQueryStats}{google:searchFieldtrialParameter}{google:searchClient}{google:sourceId}ie={inputEncoding}' -ea SilentlyContinue 
            Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Edge\Recommended' -Name 'DefaultSearchProviderSuggestURL' -Value '{google:baseURL}complete/search?output=chrome&q={searchTerms}' -ea SilentlyContinue 
        }
    }

    #remove Policy
    if ([string]::IsNullOrEmpty($HomePageURL) -or $Remove.ToBool()) {
        Remove-Item -Path 'HKLM:\Software\Policies\Microsoft\Edge' -Recurse -force -ea SilentlyContinue  | Out-Null;
        Remove-Item  -Path 'HKLM:\Software\Policies\Microsoft\Edge\Recommended' -force -ea SilentlyContinue  | Out-Null;
        Remove-Item  -Path 'HKLM:\Software\Policies\Microsoft\Edge\Recommended\RestoreOnStartupURLs' -force -ea SilentlyContinue  | Out-Null;
        Remove-Item  -Path 'HKLM:\Software\Policies\Microsoft\EdgeUpdate' -force -ea SilentlyContinue  | Out-Null;
        Remove-Item  -Path 'HKLM:\SOFTWARE\Microsoft\Enrollments\FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF' -force -ea SilentlyContinue  | Out-Null;
        Remove-Item  -Path 'HKLM:\SOFTWARE\Microsoft\Provisioning\OMADM\Accounts\FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF' -force -ea SilentlyContinue  | Out-Null;
        Remove-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'EdgePolicy' -Force -ea SilentlyContinue | Out-Null;
    }
    else {
        if ((Test-Path -LiteralPath "HKLM:\SOFTWARE\ROMAWO") -ne $true) { New-Item "HKLM:\SOFTWARE\ROMAWO" -force -ea SilentlyContinue | Out-Null };
        New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'EdgePolicy' -Value $PolicyRevision -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;        
    }
}

Function Set-OneDrive {
    <#
        .Description
        Configure OneDrive...
    #>
    [CmdletBinding()]Param(
        [parameter(Mandatory = $false)]
        [bool]
        $KFM = $true,
        [parameter(Mandatory = $false)]
        [bool]
        $FilesOnDemand = $true,
        [parameter(Mandatory = $false)]
        [int]
        $PolicyRevision = 0,
        [parameter(Mandatory = $false)]
        [switch]$Force,
        [parameter(Mandatory = $false)]
        [switch]$Remove
    )

    if (((Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -ea SilentlyContinue).OneDrivePolicy -ge $PolicyRevision) -and -NOT $Force.ToBool()) {  } else {
        #Create the key if missing 
        If ((Test-Path 'HKLM:\SOFTWARE\Policies\Microsoft\OneDrive') -eq $false ) { New-Item -Path 'HKLM:\SOFTWARE\Policies\Microsoft\OneDrive' -force -ea SilentlyContinue } 
                        
        if ($KFM) {
            #Device must be AAD Joined
            if ((Get-ItemProperty "HKLM:SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo\*\" -ea SilentlyContinue).TenantId) {
                $tenantid = (Get-ItemProperty "HKLM:SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo\*\" -ea SilentlyContinue).TenantId

                Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\OneDrive' -Name 'KFMSilentOptIn' -Value "$($tenantid)" -ea SilentlyContinue 
                Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\OneDrive' -Name 'KFMSilentOptInWithNotification' -Value 0 -ea SilentlyContinue 
            }
        }
        
        if ($FilesOnDemand) {
            Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\OneDrive' -Name 'FilesOnDemandEnabled' -Value 1 -ea SilentlyContinue 
        }
        else {
            #Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\OneDrive' -Name 'FilesOnDemandEnabled' -Value 0 -ea SilentlyContinue 
            Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\OneDrive' -Name 'FilesOnDemandEnabled' -ea SilentlyContinue 
        }
    }

    if ($Remove.ToBool()) {
        Remove-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'OneDrivePolicy' -Force -ea SilentlyContinue | Out-Null;
        Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\OneDrive' -Name 'SilentAccountConfig' -ea SilentlyContinue 
        Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\OneDrive' -Name 'KFMSilentOptIn' -ea SilentlyContinue 
        Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\OneDrive' -Name 'KFMSilentOptInWithNotification' -ea SilentlyContinue 
        Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\OneDrive' -Name 'FilesOnDemandEnabled' -ea SilentlyContinue 
    }
    else {
        if ((Test-Path -LiteralPath "HKLM:\SOFTWARE\ROMAWO") -ne $true) { New-Item "HKLM:\SOFTWARE\ROMAWO" -force -ea SilentlyContinue | Out-Null };
        

        if ($tenantid) {
            New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'OneDrivePolicy' -Value $PolicyRevision -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;    
        }
        else {
            if (-NOT $KFM) {
                New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'OneDrivePolicy' -Value $PolicyRevision -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;    
            }
        }
    }
}

Function Set-BitLocker {
    [CmdletBinding()]Param(
        [parameter(Mandatory = $false)]
        [String]
        $Drive = "C:",
        [parameter(Mandatory = $false)]
        [String]
        $EncryptionMethod = "XtsAes128",
        [parameter(Mandatory = $false)]
        [switch]$EnforceNewKey,
        [parameter(Mandatory = $false)]
        [int]
        $PolicyRevision = 0,
        [parameter(Mandatory = $false)]
        [switch]$Force
    )

    #only enable BitLocker if TPM is present
    if (Get-Tpm -ea SilentlyContinue) {
        if (((Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -ea SilentlyContinue).BitLockerPolicy -ge $PolicyRevision) -and -NOT $Force.ToBool()) {  } else {

            Remove-Item "HKLM:\Software\Policies\Microsoft\FVE" -recurse -force -ea SilentlyContinue
            Enable-BitLocker -MountPoint "$($Drive)" -EncryptionMethod $EncryptionMethod -UsedSpaceOnly -SkipHardwareTest -RecoveryPasswordProtector -ea SilentlyContinue > out-nul

            if ($EnforceNewKey.ToBool()) {
                $BLV = Get-BitLockerVolume -MountPoint "$($Drive)"
                $BLV.KeyProtector | Where-Object { $_.KeyProtectorType -eq "RecoveryPassword" } | ForEach-Object {
                    Remove-BitLockerKeyProtector -MountPoint "$($Drive)" -KeyProtectorId $_.KeyProtectorId -ea SilentlyContinue > out-nul
                }
                Add-BitLockerKeyProtector -MountPoint "$($Drive)" -RecoveryPasswordProtector -ea SilentlyContinue > out-nul
            }

            Add-BitLockerKeyProtector -MountPoint "$($Drive)" -TpmProtector -ea SilentlyContinue

            $KeyPresent = $false
            (Get-BitLockerVolume c:).KeyProtector | Where-Object { $_.KeyProtectorType -eq "RecoveryPassword" }  | ForEach-Object { 
                $PW = $_.RecoveryPassword 
                $KeyPresent = $true
                if (Test-Logging) {
                    Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 1002; Description = "BitLockerKey:" + $PW; CustomerID = $( Get-DevcdrID ); DeviceID = $( GetMyID ) }) -LogType "DevCDR" -TenantID "ROMAWO"
                }
            }
        }

        if ($KeyPresent ) {
            if ((Test-Path -LiteralPath "HKLM:\SOFTWARE\ROMAWO") -ne $true) { New-Item "HKLM:\SOFTWARE\ROMAWO" -force -ea SilentlyContinue | Out-Null };
            New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'BitLockerPolicy' -Value $PolicyRevision -PropertyType DWord -Force -ea SilentlyContinue | Out-Null; 
        }
    }
    else {
        if ((Test-Path -LiteralPath "HKLM:\SOFTWARE\ROMAWO") -ne $true) { New-Item "HKLM:\SOFTWARE\ROMAWO" -force -ea SilentlyContinue | Out-Null };
        New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'BitLockerPolicy' -Value $PolicyRevision -PropertyType DWord -Force -ea SilentlyContinue | Out-Null; 
    }  
}

Function Set-AppLocker {
    [CmdletBinding()]Param(
        [parameter(Mandatory = $false)]
        [String]
        $XMLFile = "_",
        [parameter(Mandatory = $false)]
        [int]
        $PolicyRevision = 0,
        [parameter(Mandatory = $false)]
        [switch]$Force,
        [parameter(Mandatory = $false)]
        [switch]$ScriptRules,
        [parameter(Mandatory = $false)]
        [switch]$Remove
    )

    if ($Remove.ToBool()) {
        $policy = @"
<AppLockerPolicy Version="1">
  <RuleCollection Type="Exe" EnforcementMode="NotConfigured" />
  <RuleCollection Type="Msi" EnforcementMode="NotConfigured" />
  <RuleCollection Type="Script" EnforcementMode="NotConfigured" />
  <RuleCollection Type="Dll" EnforcementMode="NotConfigured" />
  <RuleCollection Type="Appx" EnforcementMode="NotConfigured" />
</AppLockerPolicy>
"@
        $policy > $env:TEMP\Policy.xml
        Set-AppLockerPolicy -XMLPolicy $env:TEMP\Policy.xml
        Remove-Item $env:TEMP\Policy.xml -Force
        Remove-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'AppLockerPolicy' -Force -ea SilentlyContinue | Out-Null;
    }
    else {
        if (((Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -ea SilentlyContinue).AppLockerPolicy -ge $PolicyRevision) -and -NOT $Force.ToBool()) {  } else {
            if (Test-Path $XMLFile) {
                Set-AppLockerPolicy -XMLPolicy $XMLFile
            }
            else {
                $policy = @"
    <AppLockerPolicy Version="1">
    <RuleCollection Type="Appx" EnforcementMode="Enabled">
      <FilePublisherRule Id="a9e18c21-ff8f-43cf-b9fc-db40eed693ba" Name="(Standardregel) Alle signierten App-Pakete" Description="Ermöglicht Mitgliedern der Gruppe &quot;Jeder&quot; das Ausführen von signierten App-Paketen." UserOrGroupSid="S-1-1-0" Action="Allow">
        <Conditions>
          <FilePublisherCondition PublisherName="*" ProductName="*" BinaryName="*">
            <BinaryVersionRange LowSection="0.0.0.0" HighSection="*" />
          </FilePublisherCondition>
        </Conditions>
      </FilePublisherRule>
    </RuleCollection>
    <RuleCollection Type="Dll" EnforcementMode="Enabled" />
    <RuleCollection Type="Exe" EnforcementMode="Enabled">
      <FilePublisherRule Id="27afe9b3-7225-4ae9-a1b6-2a4efc5519ae" Name="Signiert von *" Description="" UserOrGroupSid="S-1-1-0" Action="Allow">
        <Conditions>
          <FilePublisherCondition PublisherName="*" ProductName="*" BinaryName="*">
            <BinaryVersionRange LowSection="*" HighSection="*" />
          </FilePublisherCondition>
        </Conditions>
        <Exceptions>
          <FilePublisherCondition PublisherName="O=GOOGLE INC, L=MOUNTAIN VIEW, S=CALIFORNIA, C=US" ProductName="*" BinaryName="*">
            <BinaryVersionRange LowSection="*" HighSection="*" />
          </FilePublisherCondition>
          <FilePublisherCondition PublisherName="O=TEAMVIEWER GMBH, L=GOEPPINGEN, S=BADEN-WUERTTEMBERG, C=DE" ProductName="*" BinaryName="*">
            <BinaryVersionRange LowSection="*" HighSection="*" />
          </FilePublisherCondition>
          <FilePathCondition Path="\\*" />
        </Exceptions>
      </FilePublisherRule>
      <FilePathRule Id="921cc481-6e17-4653-8f75-050b80acca20" Name="(Standardregel) Alle Dateien im Ordner &quot;Programme&quot;" Description="Ermöglicht Mitgliedern der Gruppe &quot;Jeder&quot; das Ausführen von Anwendungen, die sich im Ordner &quot;Programme&quot; befinden" UserOrGroupSid="S-1-1-0" Action="Allow">
        <Conditions>
          <FilePathCondition Path="%PROGRAMFILES%\*" />
        </Conditions>
      </FilePathRule>
      <FilePathRule Id="a61c8b2c-a319-4cd0-9690-d2177cad7b51" Name="(Standardregel) Alle Dateien im Ordner &quot;Windows&quot;" Description="Ermöglicht Mitgliedern der Gruppe &quot;Jeder&quot; das Ausführen von Anwendungen, die sich im Ordner &quot;Windows&quot; befinden" UserOrGroupSid="S-1-1-0" Action="Allow">
        <Conditions>
          <FilePathCondition Path="%WINDIR%\*" />
        </Conditions>
      </FilePathRule>
      <FilePathRule Id="eab7d1ff-c8f6-4c50-a891-b411ae84be13" Name="%WINDIR%\Tasks\*" Description="" UserOrGroupSid="S-1-1-0" Action="Deny">
        <Conditions>
          <FilePathCondition Path="%WINDIR%\Tasks\*" />
        </Conditions>
      </FilePathRule>
      <FilePathRule Id="fd686d83-a829-4351-8ff4-27c7de5755d2" Name="(Standardregel) Alle Dateien" Description="Ermöglicht Mitgliedern der lokalen Administratorgruppe das Ausführen aller Anwendungen" UserOrGroupSid="S-1-5-32-544" Action="Allow">
        <Conditions>
          <FilePathCondition Path="*" />
        </Conditions>
      </FilePathRule>
      <FilePathRule Id="53391d4e-21eb-4f28-874b-a40248b3e1d1" Name="%WINDIR%\System32\FxsTmp\*" Description="" UserOrGroupSid="S-1-1-0" Action="Deny">
        <Conditions>
          <FilePathCondition Path="%WINDIR%\System32\FxsTmp\*" />
        </Conditions>
      </FilePathRule>
      <FilePathRule Id="9fe862ac-614f-496a-8d34-11606c0ff069" Name="%WINDIR%\System32\spool\drivers\color\*" Description="" UserOrGroupSid="S-1-1-0" Action="Deny">
        <Conditions>
          <FilePathCondition Path="%WINDIR%\System32\spool\drivers\color\*" />
        </Conditions>
      </FilePathRule>
      <FilePathRule Id="80456a54-4115-487c-a869-028f96c442b2" Name="%WINDIR%\Registration\CRMLog\*" Description="" UserOrGroupSid="S-1-1-0" Action="Deny">
        <Conditions>
          <FilePathCondition Path="%WINDIR%\Registration\CRMLog\*" />
        </Conditions>
      </FilePathRule>
      <FilePathRule Id="4dc5c5e2-48f6-4d4b-9d1d-a41ace5f9a08" Name="%WINDIR%\tracing\*" Description="" UserOrGroupSid="S-1-1-0" Action="Deny">
        <Conditions>
          <FilePathCondition Path="%WINDIR%\tracing\*" />
        </Conditions>
      </FilePathRule>
      <FilePathRule Id="8e37698c-e67d-47df-a1af-94e9ca12207c" Name="%WINDIR%\System32\Microsoft\Crypto\RSA\MachineKeys\*" Description="" UserOrGroupSid="S-1-1-0" Action="Deny">
        <Conditions>
          <FilePathCondition Path="%WINDIR%\System32\Microsoft\Crypto\RSA\MachineKeys\*" />
        </Conditions>
      </FilePathRule>
      <FilePathRule Id="d5cdbf2c-3eeb-4621-8b7d-4f2e85b82228" Name="%WINDIR%\SysWOW64\FxsTmp\*" Description="" UserOrGroupSid="S-1-1-0" Action="Deny">
        <Conditions>
          <FilePathCondition Path="%WINDIR%\SysWOW64\FxsTmp\*" />
        </Conditions>
      </FilePathRule>
      <FilePathRule Id="159da158-3314-4c53-9355-1c3d5a500de4" Name="%WINDIR%\SysWOW64\Tasks\Microsoft\Windows\PLA\System\*" Description="" UserOrGroupSid="S-1-1-0" Action="Deny">
        <Conditions>
          <FilePathCondition Path="%WINDIR%\SysWOW64\Tasks\Microsoft\Windows\PLA\System\*" />
        </Conditions>
      </FilePathRule>
      <FilePathRule Id="3472aff6-4985-447e-b1cb-651ed9d1be3c" Name="%WINDIR%\CCM\Logs\*" Description="" UserOrGroupSid="S-1-1-0" Action="Deny">
        <Conditions>
          <FilePathCondition Path="%WINDIR%\CCM\Logs\*" />
        </Conditions>
      </FilePathRule>
      <FilePathRule Id="bb01fb31-81bb-44ce-9de2-4bcddb64a500" Name="%WINDIR%\CCM\Inventory\noidmifs\*" Description="" UserOrGroupSid="S-1-1-0" Action="Deny">
        <Conditions>
          <FilePathCondition Path="%WINDIR%\CCM\Inventory\noidmifs\*" />
        </Conditions>
      </FilePathRule>
      <FilePathRule Id="39b889d9-9c06-4c6f-85a6-524cb25678be" Name="%WINDIR%\CCM\SystemTemp\AppVTempData\AppVCommandOutput\*" Description="" UserOrGroupSid="S-1-1-0" Action="Deny">
        <Conditions>
          <FilePathCondition Path="%WINDIR%\CCM\SystemTemp\AppVTempData\AppVCommandOutput\*" />
        </Conditions>
      </FilePathRule>
    </RuleCollection>
    <RuleCollection Type="Msi" EnforcementMode="Enabled">
      <FilePublisherRule Id="b7af7102-efde-4369-8a89-7a6a392d1473" Name="(Standardregel) Alle digital signierten Windows Installer-Dateien" Description="Ermöglicht Mitgliedern der Gruppe &quot;Jeder&quot; das Ausführen von digital signierten Windows Installer-Dateien" UserOrGroupSid="S-1-1-0" Action="Allow">
        <Conditions>
          <FilePublisherCondition PublisherName="*" ProductName="*" BinaryName="*">
            <BinaryVersionRange LowSection="0.0.0.0" HighSection="*" />
          </FilePublisherCondition>
        </Conditions>
      </FilePublisherRule>
      <FilePathRule Id="5b290184-345a-4453-b184-45305f6d9a54" Name="(Standardregel) Alle Windows Installer-Dateien unter &quot;%systemdrive%\Windows\Installer&quot;" Description="Ermöglicht Mitgliedern der Gruppe &quot;Jeder&quot; das Ausführen aller Windows Installer-Dateien, die sich unter &quot;%systemdrive%\Windows\Installer&quot; befinden." UserOrGroupSid="S-1-1-0" Action="Allow">
        <Conditions>
          <FilePathCondition Path="%WINDIR%\Installer\*" />
        </Conditions>
      </FilePathRule>
      <FilePathRule Id="64ad46ff-0d71-4fa0-a30b-3f3d30c5433d" Name="(Standardregel) Alle Windows Installer-Dateien" Description="Ermöglicht Mitgliedern der lokalen Administratorgruppe das Ausführen aller Windows Installer-Dateien" UserOrGroupSid="S-1-5-32-544" Action="Allow">
        <Conditions>
          <FilePathCondition Path="*.*" />
        </Conditions>
      </FilePathRule>
      <FilePathRule Id="a87e4136-ec32-4dc1-bdde-6a582c5a0260" Name="%WINDIR%\ccmcache\*" Description="" UserOrGroupSid="S-1-1-0" Action="Allow">
        <Conditions>
          <FilePathCondition Path="%WINDIR%\ccmcache\*" />
        </Conditions>
      </FilePathRule>
    </RuleCollection>
    <RuleCollection Type="Script" EnforcementMode="AuditOnly">
      <FilePathRule Id="06dce67b-934c-454f-a263-2515c8796a5d" Name="(Standardregel) Alle Skripts im Ordner &quot;Programme&quot;" Description="Ermöglicht Mitgliedern der Gruppe &quot;Jeder&quot; das Ausführen von Skripts, die sich im Ordner &quot;Programme&quot; befinden" UserOrGroupSid="S-1-1-0" Action="Allow">
        <Conditions>
          <FilePathCondition Path="%PROGRAMFILES%\*" />
        </Conditions>
      </FilePathRule>
      <FilePathRule Id="9428c672-5fc3-47f4-808a-a0011f36dd2c" Name="(Standardregel) Alle Skripts im Ordner &quot;Windows&quot;" Description="Ermöglicht Mitgliedern der Gruppe &quot;Jeder&quot; das Ausführen von Skripts, die sich im Ordner &quot;Windows&quot; befinden" UserOrGroupSid="S-1-1-0" Action="Allow">
        <Conditions>
          <FilePathCondition Path="%WINDIR%\*" />
        </Conditions>
      </FilePathRule>
      <FilePathRule Id="ed97d0cb-15ff-430f-b82c-8d7832957725" Name="(Standardregel) Alle Skripts" Description="Ermöglicht Mitgliedern der lokalen Administratorgruppe das Ausführen aller Skripts" UserOrGroupSid="S-1-5-32-544" Action="Allow">
        <Conditions>
          <FilePathCondition Path="*" />
        </Conditions>
      </FilePathRule>
    </RuleCollection>
  </AppLockerPolicy>
"@
                if ($ScriptRules.ToBool()) {
                    #Enable ScriptRules
                    $xPol = [XML]$policy
                    $xPol.SelectNodes("//RuleCollection[@Type='Script']").SetAttribute("EnforcementMode", "Enabled")
                    $xPol.Save("$($env:TEMP)\Policy.xml")
                }
                else {
                    $policy > $env:TEMP\Policy.xml
                }
                Set-AppLockerPolicy -XMLPolicy $env:TEMP\Policy.xml
                Remove-Item $env:TEMP\Policy.xml -Force
            }

            Set-Service "AppIDSvc" -StartupType Automatic -ea SilentlyContinue
            Start-Service "AppIDSVC"
        }

        if ((Test-Path -LiteralPath "HKLM:\SOFTWARE\ROMAWO") -ne $true) { New-Item "HKLM:\SOFTWARE\ROMAWO" -force -ea SilentlyContinue | Out-Null };
        New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'AppLockerPolicy' -Value $PolicyRevision -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;  
    }
}

Function Set-WindowsUpdate {
    <#
        .Description
        Configure Windows Updates...
    #>
    [CmdletBinding()]Param(
        [parameter(Mandatory = $false)]
        [int]
        $DeferFeatureUpdateDays = 60,
        [parameter(Mandatory = $false)]
        [int]
        $DeferQualityUpdateDays = 7,
        [parameter(Mandatory = $false)]
        [bool]$RecommendedUpdates = $false,
        [parameter(Mandatory = $false)]
        [int]
        $PolicyRevision = 0,
        [parameter(Mandatory = $false)]
        [switch]$Force,
        [parameter(Mandatory = $false)]
        [switch]$Remove
    )

    if (((Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -ea SilentlyContinue).WUAPolicy -ge $PolicyRevision) -and -NOT $Force.ToBool()) {  } else {

        if (-NOT $Remove.ToBool()) {
            Remove-Item -Path 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX' -recurse -force -ea SilentlyContinue  | Out-Null
            Remove-Item -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -recurse -force -ea SilentlyContinue  | Out-Null

            If ((Test-Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate') -eq $false ) { New-Item -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -force -ea SilentlyContinue | Out-Null } 
            If ((Test-Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate\AU') -eq $false ) { New-Item -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate\AU' -force -ea SilentlyContinue | Out-Null } 

            #Feature Updates
            Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -Name 'DeferFeatureUpdates' -Value 1 -ea SilentlyContinue 
            Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -Name 'BranchReadinessLevel' -Value 16 -ea SilentlyContinue 
            Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -Name 'DeferFeatureUpdatesPeriodInDays' -Value $DeferFeatureUpdateDays -ea SilentlyContinue 

            #Quality Updates
            Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -Name 'DeferQualityUpdates' -Value 1 -ea SilentlyContinue 
            Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -Name 'DeferQualityUpdatesPeriodInDays' -Value $DeferQualityUpdateDays -ea SilentlyContinue 

            #Enable Recommended (Preview) Updates
            if ($RecommendedUpdates) {
                Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate\AU' -Name 'IncludeRecommendedUpdates' -Value 1 -ea SilentlyContinue 
            }
            else {
                Remove-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate\AU' -Name 'IncludeRecommendedUpdates' -ea SilentlyContinue  | Out-Null;
            }

            if ((Test-Path -LiteralPath "HKLM:\SOFTWARE\ROMAWO") -ne $true) { New-Item "HKLM:\SOFTWARE\ROMAWO" -force -ea SilentlyContinue | Out-Null };
            New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'WUAPolicy' -Value $PolicyRevision -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;   
        }
    }

    if ($Remove.ToBool()) {
        Remove-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'WUAPolicy' -Force -ea SilentlyContinue | Out-Null;
        Remove-Item -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -recurse -force -ea SilentlyContinue  | Out-Null;
        Remove-Item -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate\AU' -recurse -force -ea SilentlyContinue  | Out-Null;
    }
}

Function Set-Defender {
    <#
        .Description
        Configure Windows Defender...
    #>
    [CmdletBinding()]Param(
        [parameter(Mandatory = $false)]
        [bool]$ASR = $true,
        [parameter(Mandatory = $false)]
        [bool]$NetworkProtection = $true,
        [parameter(Mandatory = $false)]
        [bool]$PUA = $true,
        [parameter(Mandatory = $false)]
        [int]$PolicyRevision = 0,
        [parameter(Mandatory = $false)]
        [switch]$Force,
        [parameter(Mandatory = $false)]
        [switch]$Remove
    )

    if (((Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -ea SilentlyContinue).DefenderPolicy -ge $PolicyRevision) -and -NOT $Force.ToBool()) {  } else {

        if ($ASR) {
            Set-MpPreference -AttackSurfaceReductionOnlyExclusions "C:\Windows", "C:\Program Files", "C:\Program Files (x86)", "C:\ProgramData\Microsoft\Windows Defender"
            Set-MpPreference -AttackSurfaceReductionRules_Ids BE9BA2D9-53EA-4CDC-84E5-9B1EEEE46550, D4F940AB-401B-4EFC-AADC-AD5F3C50688A, 3B576869-A4EC-4529-8536-B80A7769E899, 75668C1F-73B5-4CF0-BB93-3ECF5CB7CC84, D3E037E1-3EB8-44C8-A917-57927947596D, 5BEB7EFE-FD9A-4556-801D-275E5FFC04CC, 92E97FA1-2EDF-4476-BDD6-9DD0B4DDDC7B, 01443614-cd74-433a-b99e-2ecdc07bfc25, c1db55ab-c21a-4637-bb3f-a12568109d35, d1e49aac-8f56-4280-b9ba-993a6d77406c, 2b3f03d-6a65-4f7b-a9c7-1c7ef74a9ba4, 26190899-1602-49e8-8b27-eb1d0a1ce869, 7674ba52-37eb-4a4f-a9a1-f0f9a1619a2c, e6db77e5-3df2-4cf1-b95a-636979351e5b, 9e6c4e1f-7d60-472f-ba1a-a39ef669e4b2, b2b3f03d-6a65-4f7b-a9c7-1c7ef74a9ba4  -AttackSurfaceReductionRules_Actions Enabled, Enabled, Enabled, Enabled, Enabled, Enabled, Enabled, Enabled, Enabled, Enabled, Enabled, Enabled, Enabled, Enabled, AuditMode, Enabled
        }

        if ($NetworkProtection) {
            Set-MpPreference -EnableNetworkProtection Enabled
        }
        else {
            Set-MpPreference -EnableNetworkProtection Disabled
        }

        if ($PUA) {
            Set-MpPreference -PUAProtection Enabled
        }
        else {
            Set-MpPreference -PUAProtection Disabled
        }

        Set-MpPreference -DisableArchiveScanning $false
        Set-MpPreference -DisableAutoExclusions $false
        Set-MpPreference -DisableBehaviorMonitoring $false
        Set-MpPreference -DisableBlockAtFirstSeen $false
        Set-MpPreference -DisableCatchupFullScan $true
        Set-MpPreference -DisableCatchupQuickScan $true
        Set-MpPreference -DisableEmailScanning $true
        Set-MpPreference -DisableIOAVProtection $false
        Set-MpPreference -DisablePrivacyMode $false
        Set-MpPreference -DisableRealtimeMonitoring $false
        Set-MpPreference -DisableRemovableDriveScanning $true
        Set-MpPreference -DisableRestorePoint $true
        Set-MpPreference -DisableScanningMappedNetworkDrivesForFullScan $true
        Set-MpPreference -DisableScanningNetworkFiles $false
        Set-MpPreference -DisableScriptScanning $false

        Set-MpPreference -MAPSReporting 1
        Set-MpPreference -RandomizeScheduleTaskTimes $true
        Set-MpPreference -ScanAvgCPULoadFactor 50
        Set-MpPreference -ScanOnlyIfIdleEnabled $true


        if ((Test-Path -LiteralPath "HKLM:\SOFTWARE\ROMAWO") -ne $true) { New-Item "HKLM:\SOFTWARE\ROMAWO" -force -ea SilentlyContinue | Out-Null };
        New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'DefenderPolicy' -Value $PolicyRevision -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;   
    }

    if ($Remove.ToBool()) {
        Remove-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'DefenderPolicy' -Force -ea SilentlyContinue | Out-Null;
    }


}

function Set-WOL {
    <#
        .Description
        Enable WOL on NetworkAdapters
    #>
    [CmdletBinding()]Param(
        [parameter(Mandatory = $false)]
        [int]$PolicyRevision = 0,
        [parameter(Mandatory = $false)]
        [switch]$Force,
        [parameter(Mandatory = $false)]
        [switch]$Remove
    )

    if (((Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -ea SilentlyContinue).WOLPolicy -ge $PolicyRevision) -and -NOT $Force.ToBool()) {  } else {
        $niclist = Get-NetAdapter | Where-Object { ($_.MediaConnectionState -eq "Connected") -and (($_.name -match "Ethernet") -or ($_.name -match "local area connection")) }
        $niclist | ForEach-Object { 
            $nic = $_
            $nicPowerWake = Get-WmiObject MSPower_DeviceWakeEnable -Namespace root\wmi | Where-Object { $_.instancename -match [regex]::escape($nic.PNPDeviceID) }
            If ($nicPowerWake.Enable -eq $true) { }
            Else {
                try {
                    $nicPowerWake.Enable = $True
                    $nicPowerWake.psbase.Put() 
                }
                catch { }
            }
            $nicMagicPacket = Get-WmiObject MSNdis_DeviceWakeOnMagicPacketOnly -Namespace root\wmi | Where-Object { $_.instancename -match [regex]::escape($nic.PNPDeviceID) }
            If ($nicMagicPacket.EnableWakeOnMagicPacketOnly -eq $true) { }
            Else {
                try {
                    $nicMagicPacket.EnableWakeOnMagicPacketOnly = $True
                    $nicMagicPacket.psbase.Put()
                }
                catch { }
            }
        }

    
        #Enable WOL broadcasts
        if ((Get-NetFirewallRule -DisplayName "WOL" -ea SilentlyContinue).count -gt 1) {
            #Cleanup WOl Rules
            Remove-NetFirewallRule -DisplayName "WOL" -ea SilentlyContinue
        }
        if ((Get-NetFirewallRule -DisplayName "WOL" -ea SilentlyContinue).count -eq 0) {
            #Add WOL Rule
            New-NetFirewallRule -DisplayName "WOL" -Direction Outbound -RemotePort 9 -Protocol UDP -Action Allow
        }

        if ((Test-Path -LiteralPath "HKLM:\SOFTWARE\ROMAWO") -ne $true) { New-Item "HKLM:\SOFTWARE\ROMAWO" -force -ea SilentlyContinue | Out-Null };
        New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'WOLPolicy' -Value $PolicyRevision -PropertyType DWord -Force -ea SilentlyContinue | Out-Null; 
    }

    if ($Remove.ToBool()) {
        #Cleanup WOl Rules
        Remove-NetFirewallRule -DisplayName "WOL" -ea SilentlyContinue  
        Remove-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'WOLPolicy' -Force -ea SilentlyContinue | Out-Null;
    }
}

function Set-FastBoot {
    <#
        .Description
        Disable FastBoot
    #>
    [CmdletBinding()]Param(
        [parameter(Mandatory = $false)]
        [int]$PolicyRevision = 0,
        [parameter(Mandatory = $false)]
        [switch]$Force,
        [parameter(Mandatory = $false)]
        [switch]$Remove
    )

    if (((Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -ea SilentlyContinue).FastBootPolicy -ge $PolicyRevision) -and -NOT $Force.ToBool()) {  } else {
        New-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name 'HiberbootEnabled' -Value 0 -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;

        if ((Test-Path -LiteralPath "HKLM:\SOFTWARE\ROMAWO") -ne $true) { New-Item "HKLM:\SOFTWARE\ROMAWO" -force -ea SilentlyContinue | Out-Null };
        New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'FastBootPolicy' -Value $PolicyRevision -PropertyType DWord -Force -ea SilentlyContinue | Out-Null; 
    }

    if ($Remove.ToBool()) {
        New-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name 'HiberbootEnabled' -Value 1 -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;
        Remove-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'FastBootPolicy' -Force -ea SilentlyContinue | Out-Null;
    }

}

function Set-DeliveryOptimization {
    <#
        .Description
        restrict Peer Selection on DeliveryOptimization
    #>
    [CmdletBinding()]Param(
        [parameter(Mandatory = $false)]
        [int]$PolicyRevision = 0,
        [parameter(Mandatory = $false)]
        [switch]$Force,
        [parameter(Mandatory = $false)]
        [switch]$Remove
    )

    if (-NOT $Remove.ToBool()) {
        if (((Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -ea SilentlyContinue).DOPolicy -ge $PolicyRevision) -and -NOT $Force.ToBool()) {  } else {
            #Create the key if missing 
            If ((Test-Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization') -eq $false ) { New-Item -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization' -force -ea SilentlyContinue } 

            #Enable Setting and Restrict to local Subnet only
            Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization' -Name 'DORestrictPeerSelectionBy' -Value 1 -ea SilentlyContinue 

            if ((Test-Path -LiteralPath "HKLM:\SOFTWARE\ROMAWO") -ne $true) { New-Item "HKLM:\SOFTWARE\ROMAWO" -force -ea SilentlyContinue | Out-Null };
            New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'DOPolicy' -Value $PolicyRevision -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;   
        }

    }

    if ($Remove.ToBool()) {
        Remove-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'DOPolicy' -Force -ea SilentlyContinue | Out-Null;
        Remove-Item -Path 'HKLM:\SOFTWARE\Policies\RuckZuck' -recurse -force -ea SilentlyContinue  | Out-Null;

        Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization' -Name 'DORestrictPeerSelectionBy' -Force -ea SilentlyContinue | Out-Null;
    }
}

function Set-InactivityTimeout {
    <#
        .Description
        auto lock windows if idle for a secific time, set Timeout in seconds
    #>
    [CmdletBinding()]Param(
        [parameter(Mandatory = $false)]
        [int]$Timeout = 300,
        [parameter(Mandatory = $false)]
        [int]$PolicyRevision = 0,
        [parameter(Mandatory = $false)]
        [switch]$Force,
        [parameter(Mandatory = $false)]
        [switch]$Remove
    )

    if (-NOT $Remove.ToBool()) {
        if (((Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -ea SilentlyContinue).InactivityPolicy -ge $PolicyRevision) -and -NOT $Force.ToBool()) {  } else {

            if ((Test-Path -LiteralPath "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System") -ne $true) { New-Item "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" -force -ea SilentlyContinue };
            New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -Name 'InactivityTimeoutSecs' -Value $Timeout -PropertyType DWord -Force -ea SilentlyContinue;

            if ((Test-Path -LiteralPath "HKLM:\SOFTWARE\ROMAWO") -ne $true) { New-Item "HKLM:\SOFTWARE\ROMAWO" -force -ea SilentlyContinue | Out-Null };
            New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'InactivityPolicy' -Value $PolicyRevision -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;   
        }

    }

    if ($Remove.ToBool()) {
        Remove-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'InactivityPolicy' -Force -ea SilentlyContinue | Out-Null;
        Remove-Item -Path 'HKLM:\SOFTWARE\Policies\RuckZuck' -recurse -force -ea SilentlyContinue  | Out-Null;

        Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -Name 'InactivityTimeoutSecs' -Force -ea SilentlyContinue | Out-Null;
    }
}

function Set-OEMLicense {
    <#
        .Description
        Set OEM Windows Key to activate Windows
    #>
    [CmdletBinding()]Param(
        [parameter(Mandatory = $false)]
        [int]$PolicyRevision = 0,
        [parameter(Mandatory = $false)]
        [switch]$Force
    )


    if (((Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -ea SilentlyContinue).OEMLicensePolicy -ge $PolicyRevision) -and -NOT $Force.ToBool()) {  } else {

        $Status = (Get-CimInstance -ClassName SoftwareLicensingProduct | Where-Object { $_.PartialProductKey -and $_.ApplicationID -eq '55c92734-d682-4d71-983e-d6ec3f16059f' } | Select-Object LicenseStatus).LicenseStatus
        if ($Status -ne 1) {
            #Set OEM Key
            $key = (Get-WmiObject softwarelicensingservice).OA3xOriginalProductKey
            if ($key) { slmgr /ipk $key }
        }

        if ((Test-Path -LiteralPath "HKLM:\SOFTWARE\ROMAWO") -ne $true) { New-Item "HKLM:\SOFTWARE\ROMAWO" -force -ea SilentlyContinue | Out-Null };
        New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'OEMLicensePolicy' -Value $PolicyRevision -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;   
    }

    
}

function Test-WindowsLicense {
    <#
        .Description
        Get Windows activation status
    #>

    Get-CimInstance -ClassName SoftwareLicensingProduct | Where-Object { $_.PartialProductKey -and $_.ApplicationID -eq '55c92734-d682-4d71-983e-d6ec3f16059f' } | Select-Object Description, LicenseStatus | convertto-json
}

function Set-RuckZuck {
    <#
        .Description
        configure RuckZuck customerID; RuckZuck will configre the Repository Server based on the customerid
    #>
    [CmdletBinding()]Param(
        [parameter(Mandatory = $false)]
        [string]$Customer = "ROMAWO",
        [parameter(Mandatory = $false)]
        [int]$Broadcast = 0,
        [parameter(Mandatory = $false)]
        [int]$PolicyRevision = 0,
        [parameter(Mandatory = $false)]
        [switch]$Force,
        [parameter(Mandatory = $false)]
        [switch]$Remove
    )

    if (((Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -ea SilentlyContinue).RuckZuckPolicy -ge $PolicyRevision) -and -NOT $Force.ToBool()) {  } else {

        if (-NOT $Remove.ToBool()) {
            if ((Test-Path -LiteralPath "HKLM:\SOFTWARE\Policies\RuckZuck") -ne $true) { New-Item "HKLM:\SOFTWARE\Policies\RuckZuck" -force -ea SilentlyContinue | Out-Null };
            New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Policies\RuckZuck' -Name 'Broadcast' -Value $Broadcast -PropertyType DWord -Force -ea SilentlyContinue | Out-Null
            New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Policies\RuckZuck' -Name 'CustomerID' -Value $Customer -PropertyType String -Force -ea SilentlyContinue | Out-Null

            if ((Test-Path -LiteralPath "HKLM:\SOFTWARE\ROMAWO") -ne $true) { New-Item "HKLM:\SOFTWARE\ROMAWO" -force -ea SilentlyContinue | Out-Null };
            New-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'RuckZuckPolicy' -Value $PolicyRevision -PropertyType DWord -Force -ea SilentlyContinue | Out-Null;   
        }
    }

    if ($Remove.ToBool()) {
        Remove-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\ROMAWO' -Name 'RuckZuckPolicy' -Force -ea SilentlyContinue | Out-Null;
        Remove-Item -Path 'HKLM:\SOFTWARE\Policies\RuckZuck' -recurse -force -ea SilentlyContinue  | Out-Null;
    }
}

function Disable-WindowsUpdate {
    <#
        .Description
        Disable Windows-Update 
    #>
    
    #Create the key if missing 
    If ((Test-Path 'HKLM:\Software\Policies\Microsoft\WindowsStore') -eq $false ) { New-Item -Path 'HKLM:\Software\Policies\Microsoft\WindowsStore' -force -ea SilentlyContinue } 
    If ((Test-Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate') -eq $false ) { New-Item -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -force -ea SilentlyContinue } 

    #Turn off access to all Windows Update features
    Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -Name 'DisableWindowsUpdateAccess' -Value 1 -ea SilentlyContinue 

    #Remove access to use all Windows Update features
    Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -Name 'SetDisableUXWUAccess' -Value 1 -ea SilentlyContinue 

    #Select when Quality Updates are received
    Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -Name 'DeferQualityUpdates' -Value 1 -ea SilentlyContinue 
    Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -Name 'DeferQualityUpdatesPeriodInDays' -Value 35 -ea SilentlyContinue 
    Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -Name 'PauseQualityUpdatesStartTime' -Value '1' -ea SilentlyContinue 

    #Turn off the offer to update to the latest version of Windows
    Set-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\WindowsStore' -Name 'DisableOSUpgrade' -Value 1 -ea SilentlyContinue 

    Set-Service UsoSvc -StartMode Disabled -ea SilentlyContinue 
    Stop-service UsoSvc -ea SilentlyContinue 
    gpupdate /force

}

function Enable-Windowsupdate {
    <#
        .Description
        Enable Windows-Update
    #>
    Remove-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -Name 'DisableWindowsUpdateAccess' -ea SilentlyContinue 
    Remove-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -Name 'SetDisableUXWUAccess' -ea SilentlyContinue
    Remove-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -Name 'DeferQualityUpdates' -ea SilentlyContinue 
    Remove-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -Name 'DeferQualityUpdatesPeriodInDays' -ea SilentlyContinue 
    Remove-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\Windows\WindowsUpdate' -Name 'PauseQualityUpdatesStartTime' -ea SilentlyContinue  
    Remove-ItemProperty -Path 'HKLM:\Software\Policies\Microsoft\WindowsStore' -Name 'DisableOSUpgrade' -ea SilentlyContinue 
    Set-ItemProperty -Path 'HKLM:\Software\Microsoft\Windows\WindowsUpdate\UpdatePolicy\PolicyState' -Name 'DeferQualityUpdates' -Value 0 -ea SilentlyContinue 
    Remove-ItemProperty -Path 'HKLM:\Software\Microsoft\WindowsUpdate\UpdatePolicy\Settings' -Name 'PausedQualityDate'-ea SilentlyContinue 
    Set-ItemProperty -Path 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\WAU' -Name 'DeferQualityUpdates' -Value 0 -ea SilentlyContinue 
    Set-Service UsoSvc -StartMode Automatic -ea SilentlyContinue 
    Start-service UsoSvc -ea SilentlyContinue 
    gpupdate /force
}

function Update-Windows {
    try {
        if (Get-InstalledModule -Name PSWindowsUpdate -MinimumVersion "2.2.0.2" -ea SilentlyContinue) {} else {
            Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.208 -Force;
            Set-PSRepository -Name PSGallery -InstallationPolicy Trusted; 
            Install-Module PSWindowsUpdate -Force;
        }
        #Add-WUServiceManager -ServiceID 7971f918-a847-4430-9279-4a52d1efe18d -confirm:$false;
        Install-WindowsUpdate -MicrosoftUpdate -IgnoreReboot -AcceptAll -Install;
        "Updates installed..."
    }
    catch { "Error, unable to detect updates: " + $_.Exception.Message }
}

function Get-ConfigItems {
    <#
        .Description
        Get Configuration from Azure TableStorage
    #>
    [CmdletBinding()]Param(
        [parameter(Mandatory = $false)]
        [String]
        $CustomerID,
        [parameter(Mandatory = $false)]
        [string]
        $Hostname = $env:COMPUTERNAME,
        [parameter(Mandatory = $false)]
        [string]
        $SettingName = "",
        [parameter(Mandatory = $false)]
        [string]
        $SettingType = "",
        [parameter(Mandatory = $false)]
        [string]
        $storageAccount,
        [parameter(Mandatory = $false)]
        [string]
        $sasToken
    )

    if ($sasToken) {
        $version = "2017-04-17";
        $dnow = (Get-Date).ToUniversalTime().toString('yyyy-MM-dd');
        if ($SettingName -eq "") { $NameQuery = "" } else { $NameQuery = "%20and%20SettingName%20eq%20'$SettingName'" }
        if ($SettingType -eq "") { $SetQuery = "" } else { $SetQuery = "%20and%20SettingType%20eq%20'$SettingType'" }

        #Remove questionmark prefix on sasToken
        if ($sasToken.StartsWith('?')) { $sasToken = $sasToken.TrimStart('?') }

        $resource = "ConfigItems()?`$filter=PartitionKey%20eq%20'$CustomerID'%20and%20ComputerName%20eq%20'$Hostname'and%20ExpirationDate%20ge%20datetime'$dnow'$($NameQuery)$($SetQuery)&$sasToken";
        $table_url = "https://$storageAccount.table.core.windows.net/$resource"
        $GMTTime = (Get-Date).ToUniversalTime().toString('R')
        $headers = @{
            'x-ms-date'    = $GMTTime
            "x-ms-version" = $version
            Accept         = "application/json;odata=fullmetadata"
        }
        return (Invoke-RestMethod -Uri $table_url -Headers $headers -ContentType application/json).value
    }
    else {
        $uri = (Get-DevcdrEP) + "/devcdr/GetConfigItems?signature=" + (Get-DevcdrSIG) + "&settingname=LocalAdmins&settingtype=SID&hostname=" + $Hostname
        return (Invoke-RestMethod -Uri $uri -ContentType application/json)
    }
}

function Sync-DeviceHardware () {

    #Get SerialNumber
    $ser = (Get-WmiObject win32_bios).SerialNumber

    #Get Hardware Hash
    $hwid = ((Get-WMIObject -Namespace root/cimv2/mdm/dmmap -Class MDM_DevDetail_Ext01 -Filter "InstanceID='Ext' AND ParentID='./DevDetail'").DeviceHardwareData)

    #Get Vendor
    $Vendor = (Get-CimInstance  -ClassName Win32_ComputerSystem).Manufacturer.Trim()

    #Get Model
    $Model = (Get-CimInstance  -ClassName Win32_ComputerSystem).Model.Trim()

    #Get UUID
    $UUID = (Get-CimInstance  -ClassName Win32_ComputerSystemProduct).UUID.Trim()

    $orderIdentifier = "ROMAWO"

    #Create object with the required parameters
    $devdata = @{ Vendor = $Vendor; Model = $Model; SerialNumber = $ser; HardwareHash = $hwid; UUID = $UUID; OrderIdentifier = $orderIdentifier; Hostname = $env:computername }
    $body = ConvertTo-Json -InputObject $devdata 

    $uri = (Get-DevcdrEP) + "/devcdr/RegisterDevice?signature=" + (Get-DevcdrSIG)

    $status = Invoke-RestMethod -uri $uri -Body $body -Method Post

    return $status
}

#region DevCDR

Function Get-DevcdrEP {
    <#
        .Description
        Get DeviceCommander Endpoint URL from NamedPipe
    #>
    try {
        $pipe = new-object System.IO.Pipes.NamedPipeClientStream '.', 'devcdrep', 'In'
        $pipe.Connect(5000)
        $sr = new-object System.IO.StreamReader $pipe
        while ($null -ne ($data = $sr.ReadLine())) { $sig = $data }
        $sr.Dispose()
        $pipe.Dispose()
        return $sig
    }
    catch { }

    return ""
}

Function Get-DevcdrSIG {
    <#
        .Description
        Get DeviceCommander Signature from NamedPipe
    #>
    try {
        $pipe = new-object System.IO.Pipes.NamedPipeClientStream '.', 'devcdrsig', 'In'
        $pipe.Connect(5000)
        $sr = new-object System.IO.StreamReader $pipe
        while ($null -ne ($data = $sr.ReadLine())) { $sig = $data }
        $sr.Dispose()
        $pipe.Dispose()
        return $sig
    }
    catch { }

    return ""
}

Function Get-DevcdrID {
    <#
        .Description
        Get DeviceCommander CustomerID from NamedPipe
    #>
    try {
        $pipe = new-object System.IO.Pipes.NamedPipeClientStream '.', 'devcdrid', 'In'
        $pipe.Connect(5000)
        $sr = new-object System.IO.StreamReader $pipe
        while ($null -ne ($data = $sr.ReadLine())) { $sig = $data }
        $sr.Dispose()
        $pipe.Dispose()
        return $sig
    }
    catch { }

    return ""
}

function Get-DevcdrDeviceId {
    $uuid = getinv -Name "Computer" -WMIClass "win32_ComputerSystemProduct" -Properties @("#UUID")
    $comp = getinv -Name "Computer" -WMIClass "win32_ComputerSystem" -Properties @("Domain", "#Name") -AppendProperties $uuid 
    return GetHash($comp | ConvertTo-Json -Compress)
}

Function Set-DevcdrComplianceIntervall($Minutes = 5) {
    <#
        .Description
        Set DeviceCommander HCompliance Check Intervall
    #>
    try {
        $pipe = new-object System.IO.Pipes.NamedPipeClientStream '.', 'comcheck', 'Out'
        $pipe.Connect(5000)
        $sw = new-object System.IO.StreamWriter $pipe
        $sw.WriteLine($Minutes)
        $sw.Flush()
    }
    catch { }

    return
}
#endregion

#region Inventory
function GetHash([string]$txt) {
    return GetMD5($txt)
}

function GetMD5([string]$txt) {
    $md5 = New-Object -TypeName System.Security.Cryptography.MD5CryptoServiceProvider
    $utf8 = New-Object -TypeName System.Text.ASCIIEncoding
    return Base58(@(0xd5, 0x10) + $md5.ComputeHash($utf8.GetBytes($txt))) #To store hash in Multihash format, we add a 0xD5 to make it an MD5 and an 0x10 means 10Bytes length
}

function GetSHA2_256([string]$txt) {
    $sha = New-Object -TypeName System.Security.Cryptography.SHA256CryptoServiceProvider
    $utf8 = New-Object -TypeName System.Text.ASCIIEncoding
    return Base58(@(0x12, 0x20) + $sha.ComputeHash($utf8.GetBytes($txt))) #To store hash in Multihash format, we add a 0x12 to make it an SHA256 and an 0x20 means 32Bytes length
}

function Base58([byte[]]$data) {
    $Digits = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz"
    [bigint]$intData = 0
    for ($i = 0; $i -lt $data.Length; $i++) {
        $intData = ($intData * 256) + $data[$i]; 
    }
    [string]$result = "";
    while ($intData -gt 0) {
        $remainder = ($intData % 58);
        $intData /= 58;
        $result = $Digits[$remainder] + $result;
    }

    for ($i = 0; ($i -lt $data.Length) -and ($data[$i] -eq 0); $i++) {
        $result = '1' + $result;
    }

    return $result
}

function normalize([long]$number) {
    if ($number) {
        if ($number -gt 2000000000 ) { return ([math]::Truncate($number / 1000000000) * 1000000000) }
        if ($number -gt 100000000 ) { return ([math]::Truncate($number / 1000000) * 1000000) }
        if ($number -gt 1000000 ) { return ([math]::Truncate($number / 10000) * 10000) }
    }
    return $number
}

function GetInv {
    Param(
        [parameter(Mandatory = $true)]
        [String]
        $Name,
        [String]
        $Namespace,
        [parameter(Mandatory = $true)]
        [String]
        $WMIClass,
        [String[]]
        $Properties,
        [ref]
        $AppendObject,
        $AppendProperties
    )

    if ($Namespace) { } else { $Namespace = "root\cimv2" }
    $obj = Get-CimInstance -Namespace $Namespace -ClassName $WMIClass

    if ($null -eq $Properties) { $Properties = $obj.Properties.Name | Sort-Object }
    if ($null -eq $Namespace) { $Namespace = "root\cimv2" }

    $res = $obj | Select-Object $Properties -ea SilentlyContinue

    #WMI Results can be an array of objects
    if ($obj -is [array]) {
        $Properties | ForEach-Object { $prop = $_; $i = 0; $res | ForEach-Object {
                $val = $obj[$i].($prop.TrimStart('#@'));
                try {
                    if ($val.GetType() -eq [string]) {
                        $val = $val.Trim();
                        if (($val.Length -eq 25) -and ($val.IndexOf('.') -eq 14) -and ($val.IndexOf('+') -eq 21)) {
                            $OS = Get-WmiObject -class Win32_OperatingSystem
                            $val = $OS.ConvertToDateTime($val)
                        }
                    }
                }
                catch { }
                if ($val) {
                    $_ | Add-Member -MemberType NoteProperty -Name ($prop) -Value ($val) -Force;
                }
                else {
                    $_.PSObject.Properties.Remove($prop);
                }
                $i++
            }
        } 
    }
    else {
        $Properties | ForEach-Object { 
            $prop = $_;
            $val = $obj.($prop.TrimStart('#@'));
            try {
                if ($val.GetType() -eq [string]) {
                    $val = $val.Trim();
                    if (($val.Length -eq 25) -and ($val.IndexOf('.') -eq 14) -and ($val.IndexOf('+') -eq 21)) {
                        $OS = Get-WmiObject -class Win32_OperatingSystem
                        $val = $OS.ConvertToDateTime($val)
                    }
                }
            }
            catch { }
            if ($val) {
                $res | Add-Member -MemberType NoteProperty -Name ($prop) -Value ($val) -Force;
            }
            else {
                $res.PSObject.Properties.Remove($prop);
            }
        }
            
    }
        
    
    $res.psobject.TypeNames.Insert(0, $Name) 

    if ($null -ne $AppendProperties) {
        $AppendProperties.PSObject.Properties | ForEach-Object {
            if ($_.Value) {
                $res | Add-Member -MemberType NoteProperty -Name $_.Name -Value ($_.Value)
            }
        } 
    }

    if ($null -ne $AppendObject.Value) {
        $AppendObject.Value | Add-Member -MemberType NoteProperty -Name $Name -Value ($res)
        return $null
    }

    return $res
    
}

function GetMyID {
    $uuid = getinv -Name "Computer" -WMIClass "win32_ComputerSystemProduct" -Properties @("#UUID")
    $comp = getinv -Name "Computer" -WMIClass "win32_ComputerSystem" -Properties @("Domain", "#Name") -AppendProperties $uuid 
    return GetHash($comp | ConvertTo-Json -Compress)
}

function SetID {
    Param(
        [ref]
        $AppendObject )

    if ($null -ne $AppendObject.Value) {
        $AppendObject.Value | Add-Member -MemberType NoteProperty -Name "#id" -Value (GetMyID) -ea SilentlyContinue
        $AppendObject.Value | Add-Member -MemberType NoteProperty -Name "#UUID" -Value (getinv -Name "Computer" -WMIClass "win32_ComputerSystemProduct" -Properties @("#UUID"))."#UUID" -ea SilentlyContinue
        $AppendObject.Value | Add-Member -MemberType NoteProperty -Name "#Name" -Value (getinv -Name "Computer" -WMIClass "win32_ComputerSystem" -Properties @("Name"))."Name" -ea SilentlyContinue
        $AppendObject.Value | Add-Member -MemberType NoteProperty -Name "#SerialNumber" -Value (getinv -Name "Computer" -WMIClass "win32_SystemEnclosure" -Properties @("SerialNumber"))."SerialNumber" -ea SilentlyContinue
        $AppendObject.Value | Add-Member -MemberType NoteProperty -Name "@MAC" -Value (Get-WmiObject -class "Win32_NetworkAdapterConfiguration" | Where-Object { ($_.IpEnabled -Match "True") }).MACAddress.Replace(':', '-')
        
        if (Test-Path "$($env:ProgramFiles)\DevCDRAgentCore\DevCDRAgentCore.exe.config") {
            [xml]$a = Get-Content "$($env:ProgramFiles)\DevCDRAgentCore\DevCDRAgentCore.exe.config"
            $EP = ($a.configuration.applicationSettings."DevCDRAgent.Properties.Settings".setting | Where-Object { $_.name -eq 'Endpoint' }).value
            $customerId = ($a.configuration.applicationSettings."DevCDRAgent.Properties.Settings".setting | Where-Object { $_.name -eq 'CustomerID' }).value
            $devcdrgrp = ($a.configuration.applicationSettings."DevCDRAgent.Properties.Settings".setting | Where-Object { $_.name -eq 'Groups' }).value
            $AppendObject.Value | Add-Member -MemberType NoteProperty -Name "DevCDREndpoint" -Value $EP
            $AppendObject.Value | Add-Member -MemberType NoteProperty -Name "DevCDRGroups" -Value $devcdrgrp
            $AppendObject.Value | Add-Member -MemberType NoteProperty -Name "DevCDRCustomerID" -Value $customerId
        }
        if (Test-Path "$($env:ProgramFiles)\ROMAWO Agent\ROMAWOAgent.exe.config") {
            [xml]$a = Get-Content "$($env:ProgramFiles)\ROMAWO Agent\ROMAWOAgent.exe.config"
            $customerId = ($a.configuration.applicationSettings."DevCDRAgent.Properties.Settings".setting | Where-Object { $_.name -eq 'CustomerID' }).value
            $AppendObject.Value | Add-Member -MemberType NoteProperty -Name "ROMAWOEndpoint" -Value $(get-devcdrep)
            $AppendObject.Value | Add-Member -MemberType NoteProperty -Name "ROMAWOCustomerID" -Value $customerId
        }
        
        return $null
    }   
}
#endregion Inventory

# SIG # Begin signature block
# MIIOEgYJKoZIhvcNAQcCoIIOAzCCDf8CAQExCzAJBgUrDgMCGgUAMGkGCisGAQQB
# gjcCAQSgWzBZMDQGCisGAQQBgjcCAR4wJgIDAQAABBAfzDtgWUsITrck0sYpfvNR
# AgEAAgEAAgEAAgEAAgEAMCEwCQYFKw4DAhoFAAQUNI1U++cmFRvDky2zUdqPAQs9
# XmKgggtIMIIFYDCCBEigAwIBAgIRANsn6eS1hYK93tsNS/iNfzcwDQYJKoZIhvcN
# AQELBQAwfTELMAkGA1UEBhMCR0IxGzAZBgNVBAgTEkdyZWF0ZXIgTWFuY2hlc3Rl
# cjEQMA4GA1UEBxMHU2FsZm9yZDEaMBgGA1UEChMRQ09NT0RPIENBIExpbWl0ZWQx
# IzAhBgNVBAMTGkNPTU9ETyBSU0EgQ29kZSBTaWduaW5nIENBMB4XDTE4MDUyMjAw
# MDAwMFoXDTIxMDUyMTIzNTk1OVowgawxCzAJBgNVBAYTAkNIMQ0wCwYDVQQRDAQ4
# NDgzMQswCQYDVQQIDAJaSDESMBAGA1UEBwwJS29sbGJydW5uMRkwFwYDVQQJDBBI
# YWxkZW5zdHJhc3NlIDMxMQ0wCwYDVQQSDAQ4NDgzMRUwEwYDVQQKDAxSb2dlciBa
# YW5kZXIxFTATBgNVBAsMDFphbmRlciBUb29sczEVMBMGA1UEAwwMUm9nZXIgWmFu
# ZGVyMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA1ujnILmAULVtVv3b
# /CDpM6RCdLV9Zjg+CDJFWLBzzjwAcHueV0mv4YgF4WoOhuc3o7GcIvl3P1DqxW97
# ex8cCfFcqdObZszKpP9OyeU5ft4c/rmfPC6PD2sKEWIIvLHAw/RXFS4RFoHngyGo
# 4070NFEMfFdQOSvBwHodsa128FG8hThRn8lXlWJG3327o39kLfawFAaCtfqEBVDd
# k4lYLl2aRpvuobfEATZ016qAHhxkExtuI007gGH58aokxpX+QWJI6T/Bj5eBO4Lt
# IqS6JjJdkRZPNc4Pa98OA+91nxoY5uZdrCrKReDeZ8qNZcyobgqAaCLtBS2esDFN
# 8HMByQIDAQABo4IBqTCCAaUwHwYDVR0jBBgwFoAUKZFg/4pN+uv5pmq4z/nmS71J
# zhIwHQYDVR0OBBYEFE+rkhTxw3ewJzXsZWbrdnRwy7y0MA4GA1UdDwEB/wQEAwIH
# gDAMBgNVHRMBAf8EAjAAMBMGA1UdJQQMMAoGCCsGAQUFBwMDMBEGCWCGSAGG+EIB
# AQQEAwIEEDBGBgNVHSAEPzA9MDsGDCsGAQQBsjEBAgEDAjArMCkGCCsGAQUFBwIB
# Fh1odHRwczovL3NlY3VyZS5jb21vZG8ubmV0L0NQUzBDBgNVHR8EPDA6MDigNqA0
# hjJodHRwOi8vY3JsLmNvbW9kb2NhLmNvbS9DT01PRE9SU0FDb2RlU2lnbmluZ0NB
# LmNybDB0BggrBgEFBQcBAQRoMGYwPgYIKwYBBQUHMAKGMmh0dHA6Ly9jcnQuY29t
# b2RvY2EuY29tL0NPTU9ET1JTQUNvZGVTaWduaW5nQ0EuY3J0MCQGCCsGAQUFBzAB
# hhhodHRwOi8vb2NzcC5jb21vZG9jYS5jb20wGgYDVR0RBBMwEYEPcm9nZXJAemFu
# ZGVyLmNoMA0GCSqGSIb3DQEBCwUAA4IBAQBHs/5P4BiQqAuF83Z4R0fFn7W4lvfE
# 6KJOKpXajK+Fok+I1bDl1pVC9JIqhdMt3tdOFwvSl0/qQ9Sp2cZnMovaxT8Bhc7s
# +PDbzRlklGGRlnVg6i7RHnJ90bRdxPTFUBbEMLy7UAjQ4iPPfRoxaR4rzF3BLaaz
# b7BoGc/oEPIMo/WmXWFngeHAVQ6gVlr2WXrKwHo8UlN0jmgzR7QrD3ZHbhR4yRNq
# M97TgVp8Fdw3o+PnwMRj4RIeFiIr9KGockQWqth+W9CDRlTgnxE8MhKl1PbUGUFM
# DcG3cV+dFTI8P2/sYD+aQHdBr0nDT2RWSgeEchQ1s/isFwOVBrYEqqf7MIIF4DCC
# A8igAwIBAgIQLnyHzA6TSlL+lP0ct800rzANBgkqhkiG9w0BAQwFADCBhTELMAkG
# A1UEBhMCR0IxGzAZBgNVBAgTEkdyZWF0ZXIgTWFuY2hlc3RlcjEQMA4GA1UEBxMH
# U2FsZm9yZDEaMBgGA1UEChMRQ09NT0RPIENBIExpbWl0ZWQxKzApBgNVBAMTIkNP
# TU9ETyBSU0EgQ2VydGlmaWNhdGlvbiBBdXRob3JpdHkwHhcNMTMwNTA5MDAwMDAw
# WhcNMjgwNTA4MjM1OTU5WjB9MQswCQYDVQQGEwJHQjEbMBkGA1UECBMSR3JlYXRl
# ciBNYW5jaGVzdGVyMRAwDgYDVQQHEwdTYWxmb3JkMRowGAYDVQQKExFDT01PRE8g
# Q0EgTGltaXRlZDEjMCEGA1UEAxMaQ09NT0RPIFJTQSBDb2RlIFNpZ25pbmcgQ0Ew
# ggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQCmmJBjd5E0f4rR3elnMRHr
# zB79MR2zuWJXP5O8W+OfHiQyESdrvFGRp8+eniWzX4GoGA8dHiAwDvthe4YJs+P9
# omidHCydv3Lj5HWg5TUjjsmK7hoMZMfYQqF7tVIDSzqwjiNLS2PgIpQ3e9V5kAoU
# GFEs5v7BEvAcP2FhCoyi3PbDMKrNKBh1SMF5WgjNu4xVjPfUdpA6M0ZQc5hc9IVK
# aw+A3V7Wvf2pL8Al9fl4141fEMJEVTyQPDFGy3CuB6kK46/BAW+QGiPiXzjbxghd
# R7ODQfAuADcUuRKqeZJSzYcPe9hiKaR+ML0btYxytEjy4+gh+V5MYnmLAgaff9UL
# AgMBAAGjggFRMIIBTTAfBgNVHSMEGDAWgBS7r34CPfqm8TyEjq3uOJjs2TIy1DAd
# BgNVHQ4EFgQUKZFg/4pN+uv5pmq4z/nmS71JzhIwDgYDVR0PAQH/BAQDAgGGMBIG
# A1UdEwEB/wQIMAYBAf8CAQAwEwYDVR0lBAwwCgYIKwYBBQUHAwMwEQYDVR0gBAow
# CDAGBgRVHSAAMEwGA1UdHwRFMEMwQaA/oD2GO2h0dHA6Ly9jcmwuY29tb2RvY2Eu
# Y29tL0NPTU9ET1JTQUNlcnRpZmljYXRpb25BdXRob3JpdHkuY3JsMHEGCCsGAQUF
# BwEBBGUwYzA7BggrBgEFBQcwAoYvaHR0cDovL2NydC5jb21vZG9jYS5jb20vQ09N
# T0RPUlNBQWRkVHJ1c3RDQS5jcnQwJAYIKwYBBQUHMAGGGGh0dHA6Ly9vY3NwLmNv
# bW9kb2NhLmNvbTANBgkqhkiG9w0BAQwFAAOCAgEAAj8COcPu+Mo7id4MbU2x8U6S
# T6/COCwEzMVjEasJY6+rotcCP8xvGcM91hoIlP8l2KmIpysQGuCbsQciGlEcOtTh
# 6Qm/5iR0rx57FjFuI+9UUS1SAuJ1CAVM8bdR4VEAxof2bO4QRHZXavHfWGshqknU
# fDdOvf+2dVRAGDZXZxHNTwLk/vPa/HUX2+y392UJI0kfQ1eD6n4gd2HITfK7ZU2o
# 94VFB696aSdlkClAi997OlE5jKgfcHmtbUIgos8MbAOMTM1zB5TnWo46BLqioXwf
# y2M6FafUFRunUkcyqfS/ZEfRqh9TTjIwc8Jvt3iCnVz/RrtrIh2IC/gbqjSm/Iz1
# 3X9ljIwxVzHQNuxHoc/Li6jvHBhYxQZ3ykubUa9MCEp6j+KjUuKOjswm5LLY5TjC
# qO3GgZw1a6lYYUoKl7RLQrZVnb6Z53BtWfhtKgx/GWBfDJqIbDCsUgmQFhv/K53b
# 0CDKieoofjKOGd97SDMe12X4rsn4gxSTdn1k0I7OvjV9/3IxTZ+evR5sL6iPDAZQ
# +4wns3bJ9ObXwzTijIchhmH+v1V04SF3AwpobLvkyanmz1kl63zsRQ55ZmjoIs24
# 75iFTZYRPAmK0H+8KCgT+2rKVI2SXM3CZZgGns5IW9S1N5NGQXwH3c/6Q++6Z2H/
# fUnguzB9XIDj5hY5S6cxggI0MIICMAIBATCBkjB9MQswCQYDVQQGEwJHQjEbMBkG
# A1UECBMSR3JlYXRlciBNYW5jaGVzdGVyMRAwDgYDVQQHEwdTYWxmb3JkMRowGAYD
# VQQKExFDT01PRE8gQ0EgTGltaXRlZDEjMCEGA1UEAxMaQ09NT0RPIFJTQSBDb2Rl
# IFNpZ25pbmcgQ0ECEQDbJ+nktYWCvd7bDUv4jX83MAkGBSsOAwIaBQCgeDAYBgor
# BgEEAYI3AgEMMQowCKACgAChAoAAMBkGCSqGSIb3DQEJAzEMBgorBgEEAYI3AgEE
# MBwGCisGAQQBgjcCAQsxDjAMBgorBgEEAYI3AgEVMCMGCSqGSIb3DQEJBDEWBBQ0
# yGSNCGwjCaiNMXe5erjXREgUWjANBgkqhkiG9w0BAQEFAASCAQCx4r4nlOf+JNdr
# zdhxyOeg97LQWG766OMZQpiuLO2gASkg6yHvh0DiGrJLU+pTHzb90l0y3Z9cr/1F
# pLLKS1z/2/vedCZFxG+a3s83uRc9sBYf1qR/qspZ/qpse7v+Mx/yuPgD0dW/nImV
# cJu22g3DEaHs+Gl6azZYFZYRE1UH/GYreZDa5ZP5kySBS1Zbq0uKpfFOm9wL0imP
# FQprJFOQSV+pqUGKYsGHSOOxXYmWdHTp9FxZdiX97AicYOQurq7MCEt55egg0YFE
# ihBOTH3cbc1jykmdE2t1OGyy4bUbmKFCr4qbJtfy2wWg58dlaV13QXV6QcqyrEWf
# tJLAOt5g
# SIG # End signature block
