#Check if PSWindowsUpdate Module is installed
try {
if(Get-InstalledModule -Name PSWindowsUpdate -MinimumVersion "2.0.0.4" -ea SilentlyContinue) {} else 
{
 Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force
 Set-PSRepository -Name PSGallery -InstallationPolicy Trusted 
 Install-Module PSWindowsUpdate -Force
 (Add-WUServiceManager -ServiceID 7971f918-a847-4430-9279-4a52d1efe18d -confirm:$false).Name
}
Install-WindowsUpdate -MicrosoftUpdate -IgnoreReboot -AcceptAll -Install
"Updates installed..."
}
catch { "Error, unable to detect Updates" }