# Create self-signed code-signing cert for MSIX (ASCII only)
$ErrorActionPreference = "Stop"
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=OpenClaw Console" `
  -KeyUsage DigitalSignature -FriendlyName "OpenClaw Lite Signing" `
  -NotAfter (Get-Date).AddYears(5) `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.4.1.311.10.3.13,1.3.6.1.5.5.7.3.3","2.5.29.19={text}")
$pwd = ConvertTo-SecureString -String "openclaw-lite" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "D:\build\oc-lite\msix\openclaw-lite.pfx" -Password $pwd | Out-Null
Export-Certificate -Cert $cert -FilePath "D:\build\oc-lite\msix\openclaw-lite.cer" | Out-Null
Write-Output ("CERT-THUMBPRINT " + $cert.Thumbprint)
