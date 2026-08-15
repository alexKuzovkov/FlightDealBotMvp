$ErrorActionPreference = 'Stop'

if (-not (Test-Path './appsettings.json')) {
    Copy-Item './appsettings.example.json' './appsettings.json'
    Write-Host 'Created appsettings.json. Fill Telegram and Amadeus credentials, then run this script again.'
    exit 1
}

dotnet restore
dotnet run
