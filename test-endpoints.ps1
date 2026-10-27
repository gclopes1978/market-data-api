$BaseUrl = "http://localhost:7013"
$OutputFile = Join-Path $PSScriptRoot "endpoint-test-results.txt"

# Endpoints conhecidos da API
$Endpoints = @(
    @{ Name = "Health";             Path = "/health" },
    @{ Name = "FX EUR/USD";         Path = "/api/fx/EUR/USD" },
    @{ Name = "FX USD/EUR";         Path = "/api/fx/USD/EUR" },
    @{ Name = "Euribor 1M";         Path = "/api/euribor/1M" },
    @{ Name = "Euribor 3M";         Path = "/api/euribor/3M" },
    @{ Name = "Euribor 6M";         Path = "/api/euribor/6M" },
    @{ Name = "Euribor 12M";        Path = "/api/euribor/12M" },
    @{ Name = "Bond US 5Y";         Path = "/api/bonds/US/US5Y" },
    @{ Name = "Stock AAPL";        Path = "/api/stocks/AAPL" },
    @{ Name = "Commodity GOLD";     Path = "/api/commodities/GOLD" },
    @{ Name = "Commodity SILVER";   Path = "/api/commodities/SILVER" },
    @{ Name = "Commodity WTI";      Path = "/api/commodities/WTI" },
    @{ Name = "Commodity BRENT";    Path = "/api/commodities/BRENT" },
    @{ Name = "Commodity NATGAS";   Path = "/api/commodities/NATGAS" }
)

$Results = [System.Collections.Generic.List[object]]::new()
$StartTime = Get-Date

foreach ($Endpoint in $Endpoints) {
    $Url = $BaseUrl + $Endpoint.Path
    $Timer = [System.Diagnostics.Stopwatch]::StartNew()

    Write-Host "A testar $($Endpoint.Name)..." -ForegroundColor Cyan

    try {
        $Response = Invoke-WebRequest `
            -Uri $Url `
            -Method Get `
            -TimeoutSec 30 `
            -UseBasicParsing `
            -ErrorAction Stop

        $Timer.Stop()
        $Body = $Response.Content
        $StatusCode = [int]$Response.StatusCode
        $Success = $StatusCode -ge 200 -and $StatusCode -lt 300

        $Results.Add([PSCustomObject]@{
            Name       = $Endpoint.Name
            Url        = $Url
            StatusCode = $StatusCode
            Result     = if ($Success) { "PASS" } else { "FAIL" }
            DurationMs = $Timer.ElapsedMilliseconds
            Details    = $Body
        })

        if ($Success) {
            Write-Host "  OK ($StatusCode) - $($Timer.ElapsedMilliseconds) ms" `
                -ForegroundColor Green
        } else {
            Write-Host "  ERRO ($StatusCode)" -ForegroundColor Red
        }
    }
    catch {
        $Timer.Stop()

        $StatusCode = $null
        $ErrorDetails = $_.Exception.Message

        # Em versões do PowerShell que expõem o código HTTP na exceção,
        # tenta recuperá-lo.
        if ($_.Exception.Response) {
            try {
                $StatusCode = [int]$_.Exception.Response.StatusCode
            } catch {}
        }

        $Results.Add([PSCustomObject]@{
            Name       = $Endpoint.Name
            Url        = $Url
            StatusCode = $StatusCode
            Result     = "FAIL"
            DurationMs = $Timer.ElapsedMilliseconds
            Details    = $ErrorDetails
        })

        Write-Host "  FALHOU - $ErrorDetails" -ForegroundColor Red
    }
}

$Passed = @($Results | Where-Object Result -eq "PASS").Count
$Failed = @($Results | Where-Object Result -eq "FAIL").Count
$TotalDuration = [math]::Round(((Get-Date) - $StartTime).TotalSeconds, 2)

$Report = @(
    "RELATORIO DE TESTES - MARKET DATA API"
    "Data: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    "Base URL: $BaseUrl"
    "Total: $($Results.Count) | Sucesso: $Passed | Falhas: $Failed"
    "Duracao total: $TotalDuration segundos"
    ""
    "========================================"
    ""
)

foreach ($Result in $Results) {
    $Report += @(
        "Endpoint: $($Result.Name)"
        "URL: $($Result.Url)"
        "Resultado: $($Result.Result)"
        "HTTP: $(if ($null -ne $Result.StatusCode) { $Result.StatusCode } else { 'N/A' })"
        "Tempo: $($Result.DurationMs) ms"
        "Detalhes:"
        [string]$Result.Details
        ""
        "----------------------------------------"
        ""
    )
}

$Report | Out-File -FilePath $OutputFile -Encoding utf8

Write-Host ""
Write-Host "Testes concluídos: $Passed sucesso(s), $Failed falha(s)." `
    -ForegroundColor Yellow
Write-Host "Relatório guardado em: $OutputFile" -ForegroundColor Green
