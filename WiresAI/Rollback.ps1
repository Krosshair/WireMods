$ErrorActionPreference='Stop'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before rollback'}
$root=Split-Path -Parent $MyInvocation.MyCommand.Path
$dest='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Default\BepInEx\plugins\Wire-WiresEnemyVariation\WiresEnemyVariation.dll'
$previous=Join-Path $root 'backup\WiresEnemyVariation_before_DecisionAI.dll'
if(-not(Test-Path $previous)){throw 'Missing previous WEV backup'}
Copy-Item $previous $dest -Force
Write-Host ('Restored original WEV DLL. SHA256 '+(Get-FileHash $dest -Algorithm SHA256).Hash)
