# Check if the powershell script is running with adminstrative privilleges
# If ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()].IsInRole([Security.Principal.WindowsBuiltInRole]"Administrator") {
	# Write-Host "This will run with adminstrative "
# } else {
	# Write-Host "This script require admin privilleges to run.
	# Exit
# }

$Subject="CN=$([System.Net.Dns]::GetHostByName($env:computerName).HostName)"
$MismatchSubject="CA=sqlclienttest.sqlservercert.com"
$Env:TDS8_Test_Certificate_FriendlyName = "TDS8SqlClientCert"

Write-Host "Make self-signed certificates in the Personal"
New-SelfSignedCertificate -Subject $Subject -KeyAlgorithm RSA -KeyLength 2048 -CertStoreLocation "cert:\LocalMachine\My" -FriendlyName $Env:TDS8_Test_Certificate_FriendlyName -TextExtension @("2.5.29.17={text}DNS=localhost&IPAddress=127.0.0.1&IPAddress=::1") -KeyExportPolicy Exportable -HashAlgorithm "SHA256" -Type SSLServerAuthentication -Provider "Microsoft RSA SChannel Cryptographic Provider" | Select 
# New-SelfSignedCertificate -Subject $MismatchSubject -KeyAlgorithm RSA -KeyLength 2048 -CertStoreLocation "cert:\LocalMachine\My" -FriendlyName $Env:TDS8_Test_Certificate_FriendlyName -TextExtension @("2.5.29.17={text}DNS=localhost&IPAddress=127.0.0.1&IPAddress=::1") -KeyExportPolicy Exportable -HashAlgorithm "SHA256" -Type SSLServerAuthentication -Provider "Microsoft RSA SChannel Cryptographic Provider" | Select 

$thumbprint = (Get-ChildItem Cert:\LocalMachine\My | where-object -Property Subject -eq -Value $Subject).thumbprint
# $mismatchthumbprint = (Get-ChildItem Cert:\LocalMachine\My | where-object -Property Subject -eq -Value $MismatchSubject).thumbprint

$cert = Get-ChildItem Cert:\LocalMachine\My\$thumbprint
# $mismatchcert = Get-ChildItem Cert:\LocalMachine\My\$mismatchthumbprint

$Pwd = ConvertTo-SecureString -String "PLACEHOLDER" -Force -AsPlainText

$Env:TDS8_Test_Certificate_On_FileSystem = "$(pwd)\sqlservercert.cer"
$Env:TDS8_Test_InvalidCertificate_On_FileSystem = "$(pwd)\sqlservercert.pfx"

Write-Host "Export certificate in pfx"
Export-PfxCertificate -Cert "Cert:\LocalMachine\My\$thumbprint" -FilePath $Env:TDS8_Test_InvalidCertificate_On_FileSystem -Password $Pwd -Force

Write-Host "Export certificate in cer"
Export-Certificate -Cert "Cert:\LocalMachine\My\$thumbprint" -FilePath $Env:TDS8_Test_Certificate_On_FileSystem -Force

Write-Host "Set the Sql Server Instance to reference the new certificate"

# Add the new certificate in the trusted root certificate authorities on the local machine
Copy-Item $cert Cert:\LocalMachine\Root

$regKeyName = "Certificate"
$registryPath = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\MSSQLSERVER\MSSQLServer\SuperSocketNetLib"

# On Windows: you may need to set permission of the new certificate so that NT Service\MSSQLSERVER has read permissions; otherwise, when the service restarts, it'll fail.
$permission = "ReadAndExecute", "ReadPermission"
Set-PrivateKeyPermissions -Certificate $cert -User "NT Service\MSSQLSERVER" -Permission $permission

if (Test-Path $registryPath) {
	Set-ItemProperty -Path $registryPath -name $regKeyName -value $thumbprint -PropertyType DWORD -Force
} else {
	New-ItemProperty -Path $registryPath -name $regKeyName -value $thumbprint -PropertyType DWORD -Force
}

# On Windows: you will need to restart the MSSQLSERVER service after setting this value in registry
Restart-Service -Name "MSSQLSERVER"