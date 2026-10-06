# Resets the local environment: starts PostgreSQL (docker compose), empties the tables and the folders under data\.
# -FillInbox also copies a few test invoices into the inbox (for the button "Eingangsordner verarbeiten").
param(
    [switch]$FillInbox,
    [string[]]$InboxFiles = @("02_*", "03_*", "05_*")
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$data = Join-Path $root "data"

docker compose -f (Join-Path $root "docker-compose.yml") up -d --wait | Out-Null
docker exec rechnungen-db psql -U rechnungen -d rechnungen -q -c `
    "do `$`$ begin if to_regclass('invoices') is not null then truncate processing_log, invoice_lines, invoices restart identity cascade; end if; end `$`$;"

foreach ($folder in "Eingang", "Archiv", "Fehler", "Duplikate") {
    $path = Join-Path $data $folder
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
    New-Item -ItemType Directory $path | Out-Null
}

if ($FillInbox) {
    foreach ($pattern in $InboxFiles) {
        Copy-Item (Join-Path $root "testdata\$pattern") (Join-Path $data "Eingang")
    }
}

Write-Host "Reset done. Inbox: $((Get-ChildItem (Join-Path $data 'Eingang')).Count) file(s)."
