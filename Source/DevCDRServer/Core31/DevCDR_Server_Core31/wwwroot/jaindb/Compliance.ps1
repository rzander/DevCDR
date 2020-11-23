Import-Module Compliance

Remove-Item "HKLM:\SOFTWARE\romawo\WriteAnalyticsLogs" -Recurse -Force

Test-OSVersion
Test-Nuget
Test-OneGetProvider("1.7.2.0")
Test-DevCDRAgent("2.0.1.50")
Set-LocalAdmin -disableAdmin $false -randomizeAdmin $true
Test-LocalAdmin
Test-DeliveryOptimization
Test-Bitlocker
Test-DiskSpace
Test-TPM
Test-SecureBoot 
Test-Office
Test-Firewall
Test-AppLocker

if (Test-locked) {
    if (Get-DevcdrSIG) {
        $uri = (Get-DevcdrEP) + "/devcdr/getfile?filename=RZApps.json&signature=" + (Get-DevcdrSIG)
        $ManagedSW = Invoke-RestMethod -Uri $uri
    }
    else {
        $ManagedSW = @("7-Zip", "7-Zip(MSI)", "FileZilla", "Google Chrome", "Firefox" , "Notepad++", "Notepad++(x64)", "Code", "AdobeReader DC MUI", "VSTO2010", "GIMP",
            "AdobeReader DC", "Microsoft Power BI Desktop", "Putty", "WinSCP", "AdobeAir", "ShockwavePlayer", 
            "VCRedist2019x64" , "VCRedist2019x86", "VCRedist2013x64", "VCRedist2013x86", "Slack", "Microsoft OneDrive", "Paint.Net",
            "VCRedist2012x64", "VCRedist2012x86", "VCRedist2010x64" , "VCRedist2010x86", "Office Timeline", "WinRAR", "Viber", "Teams Machine-Wide Installer",
            "VLC", "JavaRuntime8", "JavaRuntime8x64", "FlashPlayerPlugin", "FlashPlayerPPAPI", "Microsoft Azure Information Protection", "KeePass" )
    }
    
    #SentinelAgent seems do block VCRedist2019 Updates
    if (Get-Service "SentinelAgent") {
        $ManagedSW = @("7-Zip", "7-Zip(MSI)", "FileZilla", "Google Chrome", "Firefox" , "Notepad++", "Notepad++(x64)", "Code", "AdobeReader DC MUI", "VSTO2010", "GIMP",
            "AdobeReader DC", "Microsoft Power BI Desktop", "Putty", "WinSCP", "AdobeAir", "ShockwavePlayer", "Greenshot", "IrfanView", "iTunes",
            "VCRedist2013x64", "VCRedist2013x86", "Slack", "Microsoft OneDrive", "Paint.Net",
            "VCRedist2012x64", "VCRedist2012x86", "VCRedist2010x64" , "VCRedist2010x86", "Office Timeline", "WinRAR", "Viber", "Teams Machine-Wide Installer",
            "VLC", "JavaRuntime8", "JavaRuntime8x64", "FlashPlayerPlugin", "FlashPlayerPPAPI", "Microsoft Azure Information Protection", "KeePass", "PDF24" )
    }

    Update-Software -SWList $ManagedSW -CheckMeteredNetwork $true
    Test-WU
    Test-Temp

    if ((Get-WmiObject -Namespace root\SecurityCenter2 -Query "SELECT * FROM AntiVirusProduct" -ea SilentlyContinue).displayName.count -eq 1) {
        try { Test-Defender(3) } catch { }
    }
}

if ((Get-WmiObject -Namespace root\SecurityCenter2 -Query "SELECT * FROM AntiVirusProduct" -ea SilentlyContinue).displayName.count -eq 1) {
    try { Test-Defender(10) } catch { }
    Test-ASR
    Test-DefenderThreats 
}

#Detect missing Software Updates
Test-Software

#Edge Chromium Settings...
#Set-EdgeChromium -HomePageURL "https://ruckzuck.tools" [-RestrictUserDomain "zander.ch"] [-RemovePolicy $false] [-Force $true] [-PolicyRevision 99]

#OneDrive Settings
#Set-OneDrvice [-KFM $true] [-FilesOnDemand $true] [-RemovePolicy $false] [-Force $true] [-PolicyRevision 99]

#AppLocker Settings
#Set-AppLocker [-XMLFile "<Path to XML>"] [-RemovePolicy $false] [-Force $true] [-PolicyRevision 99]

#BitLocker Settings
#Set-BitLocker [-Drive = "C:"] [-EncryptionMethod = "XtsAes128"] [-EnforceNewKey = $false] [-Force $true] [-PolicyRevision 99]

#Configure WindowsUpdate
#Set-WindowsUpdate [-DeferFeatureUpdateDays 7] [-DeferQualityUpdateDays 3] [-RecommendedUpdates $true] [-RemovePolicy $false] [-Force $true] [-PolicyRevision 99]

#Defender Settings
#Set-Defender [-ASR $true] [-NetwokProtection $true] [-PUA $true] [-RemovePolicy $false] [-Force $true] [-PolicyRevision 99]

# Prefer IPv4 over IPv6
#if((Test-Path -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters") -ne $true) {  New-Item "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters" -force -ea SilentlyContinue };
#New-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters' -Name 'DisabledComponents' -Value 32 -PropertyType DWord -Force -ea SilentlyContinue;

$global:chk.Add("Computername", $env:computername)
$global:chk | ConvertTo-Json