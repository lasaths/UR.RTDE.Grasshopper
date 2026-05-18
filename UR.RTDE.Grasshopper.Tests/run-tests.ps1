# Run tests from CLI
# Run this from the project root

Write-Host "Building main project (Test configuration)..."
dotnet build UR.RTDE.Grasshopper.csproj -c Test -f net48 -p:BuildYakPackage=false

Write-Host "`nBuilding tests..."
dotnet build UR.RTDE.Grasshopper.Tests/UR.RTDE.Grasshopper.Tests.csproj -p:BuildYakPackage=false

Write-Host "`nCopying Test DLL to test directory..."
Copy-Item "bin/Test/net48/UR.RTDE.Grasshopper.dll" -Destination "UR.RTDE.Grasshopper.Tests/bin/Debug/net48/" -Force

if ($LASTEXITCODE -eq 0) {
    Write-Host "Running all tests (including Integration; requires URSim at 127.0.0.1 for robot tests)..."
    dotnet test UR.RTDE.Grasshopper.Tests/UR.RTDE.Grasshopper.Tests.csproj --verbosity normal
} else {
    Write-Host "Test build failed."
    dotnet build UR.RTDE.Grasshopper.Tests/UR.RTDE.Grasshopper.Tests.csproj 2>&1 | Select-String -Pattern "error CS" | Select-Object -First 5
}

Write-Host "`nUnit tests only (CI default):"
Write-Host "  dotnet test UR.RTDE.Grasshopper.Tests/UR.RTDE.Grasshopper.Tests.csproj --filter `"Category!=Integration`""
