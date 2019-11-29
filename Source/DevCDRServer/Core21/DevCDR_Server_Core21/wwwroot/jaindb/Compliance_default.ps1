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
    try {
        [void][Windows.Networking.Connectivity.NetworkInformation, Windows, ContentType = WindowsRuntime]
        $networkprofile = [Windows.Networking.Connectivity.NetworkInformation]::GetInternetConnectionProfile()
    
        if ($networkprofile -eq $null) {
            Write-Warning "Can't find any internet connections!"
            $res = $false
        }
    
        $cost = $networkprofile.GetConnectionCost()
    
    
        if ($cost -eq $null) {
            Write-Warning "Can't find any internet connections with a cost!"
            $res = $false
        }
    
        if ($cost.Roaming -or $cost.OverDataLimit) {
            $res = $true
        }
    
        if ($cost.NetworkCostType -eq [Windows.Networking.Connectivity.NetworkCostType]::Fixed -or
            $cost.NetworkCostType -eq [Windows.Networking.Connectivity.NetworkCostType]::Variable) {
            $res = $true
        }
    
        if ($cost.NetworkCostType -eq [Windows.Networking.Connectivity.NetworkCostType]::Unrestricted) {
            $res = $false
        }
    }
    catch { $res = $false }

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("Metered")) { $global:chk.Remove("Metered") }
    $global:chk.Add("Metered", $res)

    return $res
}

function Test-OneGetProvider($ProviderVersion = "1.7.1.2", $DownloadURL = "https://github.com/rzander/ruckzuck/releases/download/$($ProviderVersion)/RuckZuck.provider.for.OneGet_x64.msi" ) {
    <#
        .Description
        If missing, install latest RuckZuck Provider for OneGet...
    #>
    

    if (Get-PackageProvider -Name Ruckzuck -ea SilentlyContinue) { } else {
        if ($bLogging) {
            Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 1000; Description = "Installing OneGet v1.7.1.2" }) -LogType "DevCDRCore" 
        }
        &msiexec -i $DownloadURL /qn REBOOT=REALLYSUPPRESS 
    }

    if ((Get-PackageProvider -Name Ruckzuck).Version -lt [version]($ProviderVersion)) {
        if ($bLogging) {
            Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 1000; Description = "Updating to OneGet v1.7.1.2" }) -LogType "DevCDRCore" 
        }
        &msiexec -i $DownloadURL /qn REBOOT=REALLYSUPPRESS 
    }

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("OneGetProvider")) { $global:chk.Remove("OneGetProvider") }
    $global:chk.Add("OneGetProvider", (Get-PackageProvider -Name Ruckzuck).Version.ToString())
}

function Test-DevCDRAgent($AgentVersion = "1.0.0.33") {
    <#
        .Description
        Install or Update DevCDRAgentCore if required
    #>
    $fix = "1.0.0.5"
    if (-NOT (Get-Process DevCDRAgent -ea SilentlyContinue)) {
        if ([version](get-item "$($env:ProgramFiles)\DevCDRAgentCore\DevCDRAgentCore.exe").VersionInfo.FileVersion -lt [version]($AgentVersion)) {
            [xml]$a = Get-Content "$($env:ProgramFiles)\DevCDRAgentCore\DevCDRAgentCore.exe.config"
            $customerId = ($a.configuration.applicationSettings."DevCDRAgent.Properties.Settings".setting | Where-Object { $_.name -eq 'CustomerID' }).value
            $ep = ($a.configuration.userSettings."DevCDRAgent.Properties.Settings".setting | Where-Object { $_.name -eq 'Endpoint' }).value

            if ($customerId) { 
                $customerId > $env:temp\customer.log
                if ($bLogging) {
                    Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 1001; Description = "Updating DevCDRAgent to v$($AgentVersion)" }) -LogType "DevCDRCore" 
                }
                &msiexec -i https://devcdrcore.azurewebsites.net/DevCDRAgentCoreNew.msi CUSTOMER="$($customerId)" /qn REBOOT=REALLYSUPPRESS  
            }
            else {
                if ($ep) {
                    $ep > $env:temp\ep.log
                    if ($bLogging) {
                        Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 1001; Description = "Updating DevCDRAgent to v$($AgentVersion)" }) -LogType "DevCDRCore" 
                    }

                    &msiexec -i https://devcdrcore.azurewebsites.net/DevCDRAgentCoreNew.msi ENDPOINT="$($ep)" /qn REBOOT=REALLYSUPPRESS  
                }
            }
        }
    }
    else {
        Get-ScheduledTask DevCDR | Unregister-ScheduledTask -Confirm:$False
    }

    #Add Scheduled-Task to repair Agent 
    if ((Get-ScheduledTask DevCDR -ea SilentlyContinue).Description -ne "DeviceCommander fix $($fix)") {
        if ($bLogging) {
            Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 1004; Description = "Registering Scheduled-Task for DevCDR fix $($fix)" }) -LogType "DevCDRCore" 
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
        if ($customerId ) {
            $arg = "if(-not (get-process DevCDRAgentCore -ea SilentlyContinue)){ `"&msiexec -i https://devcdrcore.azurewebsites.net/DevCDRAgentCoreNew.msi CUSTOMER=$($customerId) /qn REBOOT=REALLYSUPPRESS`" }"
            $action = New-ScheduledTaskAction -Execute 'Powershell.exe' -Argument $arg
            $trigger = New-ScheduledTaskTrigger -Daily -At 11:25am -RandomDelay 00:25:00
            $Stset = New-ScheduledTaskSettingsSet -RunOnlyIfIdle -IdleDuration 00:02:00 -IdleWaitTimeout 02:30:00 -WakeToRun
            Register-ScheduledTask -Action $action -Settings $Stset -Trigger $trigger -TaskName "DevCDR" -Description "DeviceCommander fix $($fix)" -User "System" -TaskPath "\DevCDR" -Force
        }
        else {
            if ($ep) {
                $arg = "if(-not (get-process DevCDRAgentCore -ea SilentlyContinue)){ `"&msiexec -i https://devcdrcore.azurewebsites.net/DevCDRAgentCoreNew.msi ENDPOINT=$($ep) /qn REBOOT=REALLYSUPPRESS`" }"
                $action = New-ScheduledTaskAction -Execute 'Powershell.exe' -Argument $arg
                $trigger = New-ScheduledTaskTrigger -Daily -At 11:25am -RandomDelay 00:25:00
                $Stset = New-ScheduledTaskSettingsSet -RunOnlyIfIdle -IdleDuration 00:02:00 -IdleWaitTimeout 02:30:00 -WakeToRun
                Register-ScheduledTask -Action $action -Settings $Stset -Trigger $trigger -TaskName "DevCDR" -Description "DeviceCommander fix $($fix)" -User "System" -TaskPath "\DevCDR" -Force
            }
        }
    }

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("DevCDRAgent")) { $global:chk.Remove("DevCDRAgent") }
    $global:chk.Add("DevCDRAgent", (get-item "$($env:ProgramFiles)\DevCDRAgentCore\DevCDRAgentCore.exe").VersionInfo.FileVersion )
}

function Test-Administrators {
    <#
        .Description
        Fix local Admins on CloudJoined Devices, PowerShell Isseue if unknown cloud users/groups are member of a local group
    #>
    
    $bRes = $false;
    #Skip fix if running on a DC
    if ( (Get-WmiObject Win32_OperatingSystem).ProductType -ne 2) {
        if (Get-LocalGroupMember -SID S-1-5-32-544 -ea SilentlyContinue) { } else {
            $localgroup = (Get-LocalGroup -SID "S-1-5-32-544").Name
            $Group = [ADSI]"WinNT://localhost/$LocalGroup,group"
            $members = $Group.psbase.Invoke("Members")
            $members | ForEach-Object { $_.GetType().InvokeMember("Name", 'GetProperty', $null, $_, $null) } | Where-Object { $_ -like "S-1-12-1-*" } | ForEach-Object { Remove-LocalGroupMember -Name $localgroup $_ } 
            $bRes = $true;
        }
    }
    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("FixAdministrators")) { $global:chk.Remove("FixAdministrators") }
    $global:chk.Add("FixAdministrators", $bRes)
}

function Test-LocalAdmin {
    <#
        .Description
         disable local Admin account if PW is older than 4 hours
    #>
    $Admins = ""

    #Skip fix if running on a DC
    if ( (Get-WmiObject Win32_OperatingSystem).ProductType -ne 2) {
        if ((Get-LocalUser | Where-Object { $_.SID -like "S-1-5-21-*-500" }).Enabled) {
            #Fix if local Admin PW is older than 4 Hours
            if (((get-date) - (Get-LocalUser | Where-Object { $_.SID -like "S-1-5-21-*-500" }).PasswordLastSet).TotalHours -gt 4) {
                #Disable local Admin
                (Get-LocalUser | Where-Object { $_.SID -like "S-1-5-21-*-500" }) | Disable-LocalUser

                #generate new random PW
                $pw = get-random -count 12 -input (35..37 + 45..46 + 48..57 + 65..90 + 97..122) | ForEach-Object -begin { $aa = $null } -process { $aa += [char]$_ } -end { $aa }; (Get-LocalUser | Where-Object { $_.SID -like "S-1-5-21-*-500" }) | Set-LocalUser -Password (ConvertTo-SecureString -String $pw -AsPlainText -Force)
                $pw = "";

                $Admins = (Get-LocalUser | Where-Object { $_.SID -like "S-1-5-21-*-500" }).Name

                if ($bLogging) {
                    Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 1005; Description = "local Admin account disabled and new random passwort generated" }) -LogType "DevCDRCore" 
                }
            }
        }
    }

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("LocalAdmin")) { $global:chk.Remove("LocalAdmin") }
    $global:chk.Add("LocalAdmin", $Admins)
}

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

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("WOL")) { $global:chk.Remove("WOL") }
    $global:chk.Add("WOL", $bRes)
}

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

function Test-DeliverOptimization {
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

function Update-Software {
    <#
        .Description
        Update a specific list of Softwares
    #>
    param( 
        [parameter(Mandatory = $true)] [string[]] $SWList, 
        [parameter(Mandatory = $false)] [boolean] $CheckMeteredNetwork = $false
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
            if ($bLogging) {
                Write-Log -JSON ([pscustomobject]@{Computer = $env:COMPUTERNAME; EventID = 2000; Description = "RuckZuck updating: $($_)" }) -LogType "DevCDR" 
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

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("Temp")) { $global:chk.Remove("Temp") }
    $global:chk.Add("Temp ", $true)
}

Function Test-Defender($Age = 7) {
    <#
        .Description
        Run Defender Quickscan if last scan is older than $Age days
    #>
    if ((Get-WmiObject -Namespace root\\SecurityCenter2 -Query "SELECT * FROM AntiVirusProduct" -ea SilentlyContinue).displayName.count -eq 1) {
        $ScanAge = (Get-MpComputerStatus).QuickScanAge
        if ($ScanAge -ge $Age) { Start-MpScan -ScanType QuickScan }

        if ($null -eq $global:chk) { $global:chk = @{ } }
        if ($global:chk.ContainsKey("DefenderScangAge")) { $global:chk.Remove("DefenderScangAge") }
        $global:chk.Add("DefenderScangAge", $ScanAge)
    }
    else {
        if ($null -eq $global:chk) { $global:chk = @{ } }
        if ($global:chk.ContainsKey("DefenderScangAge")) { $global:chk.Remove("DefenderScangAge") }
        $global:chk.Add("DefenderScangAge", 999)
    }
}

Function Test-Bitlocker {
    <#
        .Description
        Check if BitLocker is enabled
    #>
    $bRes = "Off"
    try {
        if ((Get-BitLockerVolume C:).ProtectionStatus -eq "On") { $bRes = (Get-BitLockerVolume C:).EncryptionMethod }
    }
    catch { }

    if ($null -eq $global:chk) { $global:chk = @{ } }
    if ($global:chk.ContainsKey("Bitlocker")) { $global:chk.Remove("Bitlocker") }
    $global:chk.Add("Bitlocker", $bRes)
}

#Import-Module Compliance
Test-Nuget
Test-OneGetProvider("1.7.1.2")
Test-DevCDRAgent("1.0.0.33")
Test-Administrators 
Test-LocalAdmin
Test-WOL
Test-FastBoot
Test-DeliverOptimization
Test-Bitlocker

if (Test-locked) {

    $ManagedSW = @("7-Zip", "7-Zip(MSI)", "Audacity", "FileZilla", "Google Chrome", "Firefox" , "Notepad++", "Notepad++(x64)", "Code", "AdobeReader DC MUI", "VSTO2010", "GIMP",
        "AdobeReader DC", "Microsoft Power BI Desktop", "Putty", "WinSCP", "AdobeAir", "ShockwavePlayer", "VCRedist2015x64" , "VCRedist2015x86", "VCRedist2017x64" , "VCRedist2017x86",
        "VCRedist2019x64" , "VCRedist2019x86", "VCRedist2013x64", "VCRedist2013x86", "Slack", "Microsoft OneDrive", "Paint.Net",
        "VCRedist2012x64", "VCRedist2012x86", "VCRedist2010x64" , "VCRedist2010x86", "Office Timeline", "WinRAR", "Viber", "Teams Machine-Wide Installer",
        "VLC", "JavaRuntime8", "JavaRuntime8x64", "FlashPlayerPlugin", "FlashPlayerPPAPI", "Microsoft Azure Information Protection", "KeePass" )

    Update-Software -SWList $ManagedSW -CheckMeteredNetwork $true
    Test-Temp
}

if ((Get-WmiObject -Namespace root\SecurityCenter2 -Query "SELECT * FROM AntiVirusProduct" -ea SilentlyContinue).displayName.count -eq 1) {
    Test-Defender(10)
}

#$global:chk.Add("Computername", $env:computername)
#$global:chk | ConvertTo-Json

# SIG # Begin signature block
# MIIOEgYJKoZIhvcNAQcCoIIOAzCCDf8CAQExCzAJBgUrDgMCGgUAMGkGCisGAQQB
# gjcCAQSgWzBZMDQGCisGAQQBgjcCAR4wJgIDAQAABBAfzDtgWUsITrck0sYpfvNR
# AgEAAgEAAgEAAgEAAgEAMCEwCQYFKw4DAhoFAAQUhc6ogoTIhHkQLoO3vlSZC2Ly
# 2r2gggtIMIIFYDCCBEigAwIBAgIRANsn6eS1hYK93tsNS/iNfzcwDQYJKoZIhvcN
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
# MBwGCisGAQQBgjcCAQsxDjAMBgorBgEEAYI3AgEVMCMGCSqGSIb3DQEJBDEWBBTL
# 9RtLhr+/WiRhoSIWWu9+1f1FFDANBgkqhkiG9w0BAQEFAASCAQCI4s5XALQQUCKD
# IazYbzcza1sgKxFxMQPAJUcMaKS05uNjGeF0ZqDhg781NvJN0GFzXeBFN18TsZrd
# ovbULKskUejiYQuwWCzlLUf3lHRa7vS9FqozbBenZkK6hVZSohgR5scxQRZmSBjj
# 6L/fDMeUgYbIqm6Avtu/u6aCHdRWk34ADeF8+8vBxZtzWmBsRLk+sMb16bLdFMBe
# 2Hdl8HCj2X6fL7hUNGv9qZZDFlwtczls72t/HElJotHUTgZHATqfP/XBnOc0QjK0
# b22i0PtLBiXlvdBDAFgcl2uua/G68xEykVHFcXzN0VlbBK/4SHmxB/EMWniRbE2h
# 2DoLcLqg
# SIG # End signature block
