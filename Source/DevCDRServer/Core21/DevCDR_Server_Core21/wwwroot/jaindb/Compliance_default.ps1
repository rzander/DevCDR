[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# Source: https://www.powershellgallery.com/packages/NetMetered/1.0/Content/NetMetered.psm1
# Created by:     Wil Taylor (wilfridtaylor@gmail.com) 
function Test-NetMetered {
    try {
        [void][Windows.Networking.Connectivity.NetworkInformation, Windows, ContentType = WindowsRuntime]
        $networkprofile = [Windows.Networking.Connectivity.NetworkInformation]::GetInternetConnectionProfile()
    
        if ($networkprofile -eq $null) {
            Write-Warning "Can't find any internet connections!"
            return $false
        }
    
        $cost = $networkprofile.GetConnectionCost()
    
    
        if ($cost -eq $null) {
            Write-Warning "Can't find any internet connections with a cost!"
            return $false
        }
    
        if ($cost.Roaming -or $cost.OverDataLimit) {
            return $true
        }
    
        if ($cost.NetworkCostType -eq [Windows.Networking.Connectivity.NetworkCostType]::Fixed -or
            $cost.NetworkCostType -eq [Windows.Networking.Connectivity.NetworkCostType]::Variable) {
            return $true
        }
    
        if ($cost.NetworkCostType -eq [Windows.Networking.Connectivity.NetworkCostType]::Unrestricted) {
            return $false
        }
    }
    catch { return $false }
}

#Install RuckZuck Provider for OneGet if missing...
if (Get-PackageProvider -Name Ruckzuck -ea SilentlyContinue) { } else {
    &msiexec -i https://github.com/rzander/ruckzuck/releases/download/1.7.0.2/RuckZuck.provider.for.OneGet_x64.msi /qn REBOOT=REALLYSUPPRESS 
}

if ((Get-PackageProvider -Name Ruckzuck).Version -lt [version]("1.7.0.2")) {
    &msiexec -i https://github.com/rzander/ruckzuck/releases/download/1.7.0.2/RuckZuck.provider.for.OneGet_x64.msi /qn REBOOT=REALLYSUPPRESS 
}

#Update DevCDRAgentCore
if ([version](get-item "C:\Program Files\DevCDRAgentCore\DevCDRAgentCore.exe").VersionInfo.FileVersion -lt [version]"1.0.0.23") {
    [xml]$a = gc "C:\Program Files\DevCDRAgentCore\DevCDRAgentCore.exe.config"
    $EP = ($a.configuration.applicationSettings."DevCDRAgent.Properties.Settings".setting | Where-Object { $_.name -eq 'Endpoint' }).value
    $EP > $env:temp\ep.log
    &msiexec -i https://devcdrcore.azurewebsites.net/DevCDRAgentCore.msi ENDPOINT="$($EP)" /qn REBOOT=REALLYSUPPRESS  
}

#Add Scheduled-Task to repair Agent 
if ((Get-ScheduledTask DevCDR -ea SilentlyContinue).Description -ne 'DeviceCommander fix 1.0.0.0') {
    try {
        $scheduleObject = New-Object -ComObject schedule.service
        $scheduleObject.connect()
        $rootFolder = $scheduleObject.GetFolder("\")
        $rootFolder.CreateFolder("DevCDR")
    }
    catch { }

    [xml]$a = gc "C:\Program Files\DevCDRAgentCore\DevCDRAgentCore.exe.config"
    $EP = ($a.configuration.applicationSettings."DevCDRAgent.Properties.Settings".setting | Where-Object { $_.name -eq 'Endpoint' }).value
    $arg = "if(-not (get-process DevCDRAgentCore -ea SilentlyContinue)){ `"&msiexec -i https://devcdrcore.azurewebsites.net/DevCDRAgentCore.msi ENDPOINT=$($EP) /qn REBOOT=REALLYSUPPRESS`" }"
    $action = New-ScheduledTaskAction -Execute 'Powershell.exe' -Argument $arg
    $trigger = New-ScheduledTaskTrigger -Daily -At 11:45am -RandomDelay 00:15:00
    $Stset = New-ScheduledTaskSettingsSet -RunOnlyIfIdle -IdleDuration 00:02:00 -IdleWaitTimeout 02:30:00 -WakeToRun
    Register-ScheduledTask -Action $action -Settings $Stset -Trigger $trigger -TaskName "DevCDR" -Description "DeviceCommander fix 1.0.0.0" -User "System" -TaskPath "\DevCDR" -Force
}


#Only Update SW if LockScreen (LogonUI) is present
if (get-process logonui -ea SilentlyContinue) {
	
    #Disable FastBoot
    New-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name 'HiberbootEnabled' -Value 0 -PropertyType DWord -Force -ea SilentlyContinue;

    #region Select a method to restrict Peer Selection on DeliveryOptimization
    #Create the key if missing 
    If ((Test-Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization') -eq $false ) { New-Item -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization' -force -ea SilentlyContinue } 

    #Enable Setting and Restrict to local Subnet only
    Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization' -Name 'DORestrictPeerSelectionBy' -Value 1 -ea SilentlyContinue 
    #endregion

    #Enable WOL broadcasts
    if ((Get-NetFirewallRule -DisplayName "WOL" -ea SilentlyContinue).count -gt 1) {
        #Cleanup WOl Rules
        Remove-NetFirewallRule -DisplayName "WOL" -ea SilentlyContinue
    }
    if ((Get-NetFirewallRule -DisplayName "WOL" -ea SilentlyContinue).count -eq 0) {
        #Add WOL Rule
        New-NetFirewallRule -DisplayName "WOL" -Direction Outbound -RemotePort 9 -Protocol UDP -Action Allow
    }

    #Update Software if network is NOT metered
    if (-NOT (Test-NetMetered )) {
        #List of managed Software.
        $ManagedSW = @("7-Zip", "7-Zip(MSI)", "FileZilla", "Google Chrome", "Firefox" , "Notepad++", "Notepad++(x64)", "Code", "AdobeReader DC MUI", "VSTO2010", "GIMP",
            "AdobeReader DC", "Microsoft Power BI Desktop", "Putty", "WinSCP", "AdobeAir", "ShockwavePlayer",
            "VCRedist2017x64" , "VCRedist2017x86", "VCRedist2015x64", "VCRedist2015x86", "VCRedist2013x64", "VCRedist2013x86", 
            "VCRedist2012x64", "VCRedist2012x86", "VCRedist2010x64" , "VCRedist2010x86", 
            "VLC", "JavaRuntime8", "JavaRuntime8x64", "FlashPlayerPlugin", "FlashPlayerPPAPI", "Microsoft Azure Information Protection" )

        #Find Software Updates
        $updates = Find-Package -ProviderName RuckZuck -Updates | Select-Object PackageFilename

        #Update only managed Software
        $ManagedSW | ForEach-Object { 
            if ($updates.PackageFilename -contains $_) { 
                "Updating: " + $_ ;
                Install-Package -ProviderName RuckZuck "$($_)"
            }
            else { "$($_)" }
        }
    }

    #Cleanup Temp
    if ((Get-ChildItem "$($env:windir)\Temp\*" -Recurse).Count -gt 100) {
        Remove-Item "$($env:windir)\Temp\*" -Force -Recurse -Exclude devcdrcore.log -ea SilentlyContinue
    }
}
# SIG # Begin signature block
# MIIOEgYJKoZIhvcNAQcCoIIOAzCCDf8CAQExCzAJBgUrDgMCGgUAMGkGCisGAQQB
# gjcCAQSgWzBZMDQGCisGAQQBgjcCAR4wJgIDAQAABBAfzDtgWUsITrck0sYpfvNR
# AgEAAgEAAgEAAgEAAgEAMCEwCQYFKw4DAhoFAAQUi1VddKNRZxHvcuqhmcuipJRF
# LTCgggtIMIIFYDCCBEigAwIBAgIRANsn6eS1hYK93tsNS/iNfzcwDQYJKoZIhvcN
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
# MBwGCisGAQQBgjcCAQsxDjAMBgorBgEEAYI3AgEVMCMGCSqGSIb3DQEJBDEWBBRR
# bWFrh3RBBYc/+lAZYDDh2CYR8DANBgkqhkiG9w0BAQEFAASCAQBB//piDSPZu6bY
# ZMxcGXTfmoxJYHq8ROwA3kaHcId1ZTWrcpghKomO+W/UoMKGhjWOldcdpMT3x+Tz
# QMohO65lDYhShfi2hNjq629Yv8DMZSZLakPW7wQ6AGdwVFaG9pgdF2Zm7QRL67lB
# Rr8g/d7yKLA1NXqxUu5feROhjsybz0nowH5AClGE6ugtXZq1T6AR7D+hrjjhiGi/
# sEE5VGXh3aCew5cAKV3/COvGvBAJJ2thnaDiXQbA7Q4K7aZcq/LSLFwY926upaLD
# hvGSKUYGc1lXI0ZZO3xWJXNxLjU52Kk1MKX26+N4hTX5L7gKXJ3/7HMnT3EUfuPa
# EFbtkm4o
# SIG # End signature block
