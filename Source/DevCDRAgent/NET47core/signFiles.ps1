$cert = Get-ChildItem cert:\CurrentUser\My -CodeSigningCert | ? { $_.Thumbprint -eq 'FDECFF173C9ECE56047F277E9E5A2D779BF809AC' }
cd $psscriptroot

Set-AuthenticodeSignature PSStatus.ps1 $cert[0]
