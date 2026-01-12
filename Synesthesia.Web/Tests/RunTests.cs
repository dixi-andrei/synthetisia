# Script pentru rularea testelor cu coverage
# Rulează din folderul Synesthesia.Web.Tests

Write - Host "🧪 Running tests with coverage..." - ForegroundColor Cyan

# Șterge coverage-ul vechi
Remove-Item -Path "TestResults" -Recurse -ErrorAction SilentlyContinue

# Rulează testele cu coverage
dotnet test `
    --collect:"XPlat Code Coverage" `
    --results - directory:"./TestResults" `
    --logger "console;verbosity=detailed" `
    / p:CollectCoverage = true `
    / p:CoverletOutputFormat = cobertura `
    / p:CoverletOutput = "./TestResults/coverage.cobertura.xml"

if ($LASTEXITCODE - eq 0) {
    Write - Host "`n✅ Tests passed!" - ForegroundColor Green
    
    # Găsește raportul de coverage
    $coverageFile = Get - ChildItem - Path "TestResults" - Filter "coverage.cobertura.xml" - Recurse | Select - Object - First 1


    if ($coverageFile) {
        Write - Host "`n📊 Coverage report: $($coverageFile.FullName)" - ForegroundColor Cyan

        # Instalează ReportGenerator dacă nu există
        if (-not(Get - Command "reportgenerator" - ErrorAction SilentlyContinue))
        {
            Write - Host "`n📦 Installing ReportGenerator..." - ForegroundColor Yellow
            dotnet tool install -g dotnet - reportgenerator - globaltool
        }

# Generează raport HTML
        Write - Host "`n📈 Generating HTML report..." - ForegroundColor Cyan
        reportgenerator `
            "-reports:$($coverageFile.FullName)" `
            "-targetdir:TestResults/CoverageReport" `
            "-reporttypes:Html;HtmlSummary;Badges" `
            "-title:Synesthesia Test Coverage"


        Write - Host "`n✨ Coverage report generated at: TestResults/CoverageReport/index.html" - ForegroundColor Green
        
        # Deschide raportul în browser
        $htmlReport = Join - Path $PSScriptRoot "TestResults\CoverageReport\index.html"
        if (Test - Path $htmlReport) {
            Start - Process $htmlReport
        }
    }
} else
{
    Write - Host "`n❌ Tests failed!" - ForegroundColor Red
    exit 1
}