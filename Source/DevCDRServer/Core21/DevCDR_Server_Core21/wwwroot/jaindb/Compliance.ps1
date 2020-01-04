Import-Module Compliance
Test-OSVersion
Test-Nuget
Test-OneGetProvider("1.7.1.3")
Test-DevCDRAgent("2.0.1.21")
#Test-Administrators 
Test-LocalAdmin
Test-WOL
Test-FastBoot
Test-DeliveryOptimization
Test-Bitlocker
Test-DiskSpace
Test-TPM
Test-SecureBoot 
Test-Office
Test-Firewall
Test-Software
Test-AppLocker

if (Test-locked) {
    $ManagedSW = @("7-Zip", "7-Zip(MSI)", "FileZilla", "Google Chrome", "Firefox" , "Notepad++", "Notepad++(x64)", "Code", "AdobeReader DC MUI", "VSTO2010", "GIMP",
        "AdobeReader DC", "Microsoft Power BI Desktop", "Putty", "WinSCP", "AdobeAir", "ShockwavePlayer", 
        "VCRedist2019x64" , "VCRedist2019x86", "VCRedist2013x64", "VCRedist2013x86", "Slack", "Microsoft OneDrive", "Paint.Net",
        "VCRedist2012x64", "VCRedist2012x86", "VCRedist2010x64" , "VCRedist2010x86", "Office Timeline", "WinRAR", "Viber", "Teams Machine-Wide Installer",
        "VLC", "JavaRuntime8", "JavaRuntime8x64", "FlashPlayerPlugin", "FlashPlayerPPAPI", "Microsoft Azure Information Protection", "KeePass" )

    Update-Software -SWList $ManagedSW -CheckMeteredNetwork $true
    Test-WU
    Test-Temp

	if((Get-WmiObject -Namespace root\SecurityCenter2 -Query "SELECT * FROM AntiVirusProduct" -ea SilentlyContinue).displayName.count -eq 1) {
		try{ Test-Defender(3) } catch{}
	}
}

if((Get-WmiObject -Namespace root\SecurityCenter2 -Query "SELECT * FROM AntiVirusProduct" -ea SilentlyContinue).displayName.count -eq 1) {
    try{ Test-Defender(10) } catch{}
    Test-ASR
    Test-DefenderThreats 
}

$global:chk.Add("Computername", $env:computername)
$global:chk | ConvertTo-Json