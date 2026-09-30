# Local Testing Guide - SimlifiezYaml Project

Complete guide to build, test, and run the SimlifiezYaml project locally after Phase 1-3 refactoring.

---

## Prerequisites

### Required Software
- **.NET 10 SDK** (10.0.401 or later, see `global.json`) - Download from https://dotnet.microsoft.com/download
- **Git** - For version control
- **PowerShell 5.1+** or **Windows Terminal** - For running commands
- **Visual Studio Code** (optional) - For code editing

### System Requirements
- Windows 10/11 (or WSL2 on Windows with .NET SDK installed)
- At least 2GB free disk space
- Internet connection for NuGet package restoration

### Verify Prerequisites
```powershell
# Check .NET SDK version (must be 10.0+)
dotnet --version

# Check PowerShell version (must be 5.1+)
$PSVersionTable.PSVersion
```

---

## Step 1: Navigate to Project Directory

```powershell
# Navigate to the project root
cd "c:\Users\U39004\Documents\Coding\Yaml\SimlifiezYaml"

# Verify you're in the correct directory
Get-Location
# Should show: C:\Users\U39004\Documents\Coding\Yaml\SimlifiezYaml

# List directory contents to verify structure
Get-ChildItem
# Should show: README.md, SimlifiezYaml.sln, src/, tests/, push-to-github.ps1
```

---

## Step 2: Restore Dependencies

This downloads all NuGet packages required by the project.

```powershell
# Restore all project dependencies
dotnet restore

# Expected output: "Restore completed in X.XXXs"
```

If you encounter issues:
```powershell
# Clear NuGet cache and restore
dotnet nuget locals all --clear
dotnet restore --force
```

---

## Step 3: Build the Solution

This compiles all C# source code.

```powershell
# Build in Debug configuration (recommended for development/testing)
dotnet build

# Expected output: "Build succeeded" at the end

# Build in Release configuration (optimized for production)
dotnet build --configuration Release
```

### Build Troubleshooting

If build fails:
```powershell
# Clean previous build artifacts
dotnet clean

# Restore and rebuild
dotnet restore
dotnet build

# If still failing, check for syntax errors
dotnet build --verbosity diagnostic
```

---

## Step 4: Run Unit Tests

### Run All Tests
```powershell
# Run all unit tests
dotnet test

# Expected output: Shows test run summary
# "Test Run Successful."
# "Total tests: ~60+, Passed: ~60+, Failed: 0"
```

### Run Specific Test Categories

```powershell
# Run only Phase 1 tests (critical fixes)
dotnet test --filter "ClassName~ErrorPathTests"

# Run only Phase 2 tests (code quality)
dotnet test --filter "ClassName~ErrorPathTests"

# Run only Phase 3 tests (architecture)
dotnet test --filter "ClassName~Phase3ArchitectureTests"

# Run only PipelineGenerator tests
dotnet test --filter "ClassName~PipelineGeneratorTests"
```

### Run Tests with Detailed Output
```powershell
# Show full output including test names
dotnet test --verbosity normal

# Show detailed diagnostic information
dotnet test --verbosity detailed

# Show minimal output
dotnet test --verbosity quiet
```

### Run Tests and Generate Coverage Report
```powershell
# Install coverage tools (one-time setup)
dotnet add package coverlet.collector

# Run tests with code coverage
dotnet test /p:CollectCoverage=true

# Run tests with coverage and specify output format
dotnet test /p:CollectCoverage=true /p:CoverageFormat=cobertura
```

### Run Individual Test File
```powershell
# Run specific test class
dotnet test tests/SimlifiezYaml.Core.Tests/ErrorPathTests.cs

# Run specific test method
dotnet test --filter "FullyQualifiedName~SimlifiezYaml.Core.Tests.ErrorPathTests.PipelineDefinitionValidator_RejectsEmptyName"
```

---

## Step 5: Build and Run the Web Application

The web UI is a Blazor Server application.

### Option 1: Run with `dotnet run`
```powershell
# Start the web application in Debug mode
dotnet run --project src/SimlifiezYaml.Web

# Expected output:
# "info: Microsoft.Hosting.Lifetime[14]
#  Now listening on: https://localhost:5001"

# Open browser to: https://localhost:5001
# Press Ctrl+C to stop the server
```

### Option 2: Run with Hot Reload (Recommended for Development)
```powershell
# Enable hot reload - code changes auto-refresh without restart
dotnet watch --project src/SimlifiezYaml.Web run

# Edit a .cs or .razor file and save - page refreshes automatically
# Press Ctrl+C to stop
```

### Option 3: Release Build
```powershell
# Build optimized release version
dotnet run --project src/SimlifiezYaml.Web --configuration Release

# Same output as Debug mode, but faster execution
```

### Accessing the Application
- **Local URL:** https://localhost:5001
- **HTTPS Warning:** You may see a browser security warning. Click "Advanced" → "Proceed" 
- **Firewall:** Windows may ask for network access - allow it

### Troubleshooting Application Start
```powershell
# If port 5001 is already in use, the app will try 5002, 5003, etc.
# Check current listeners:
netstat -ano | findstr :5001

# Kill process using the port (replace PID with actual number)
Stop-Process -Id <PID> -Force

# Restart the application
dotnet run --project src/SimlifiezYaml.Web
```

---

## Step 6: Verify Specific Improvements

### Verify Phase 1 Fixes (Critical Fixes)
```powershell
# Test DI anti-pattern fix
dotnet test --filter "ClassName~ErrorPathTests"

# Look for test passing that validates PipelineDefinitionValidator
# And PoolConfigurationHelper tests
```

### Verify Phase 2 Improvements (Code Quality)
```powershell
# Test escaping security
dotnet test --filter "ClassName~ErrorPathTests" --verbosity normal
# Look for: "PowerShellStep_EscapesSingleQuotes", "Task_EscapesInputValues"

# Verify logging in application runtime
dotnet run --project src/SimlifiezYaml.Web 2>&1 | findstr "Starting" 

# Check that application logs "Starting pipeline generation" messages
```

### Verify Phase 3 Refactoring (Architecture)
```powershell
# Test YAML builder pattern
dotnet test --filter "ClassName~Phase3ArchitectureTests" --verbosity normal
# Look for: 20+ tests all passing

# Verify assembler tests
dotnet test --filter "FullyQualifiedName~SimlifiezYaml.Core.Tests.Phase3ArchitectureTests.YamlAssembler_BuildsCompleteYaml"

# Verify TriggerConfig tests
dotnet test --filter "FullyQualifiedName~SimlifiezYaml.Core.Tests.Phase3ArchitectureTests.TriggerConfig_MainOnly"
```

---

## Step 7: Code Analysis and Cleanup

### Check Code Quality
```powershell
# Analyze solution for code issues
dotnet build --verbosity diagnostic

# Look for warnings - should have reduced warnings after refactoring
```

### Format Code (Optional)
```powershell
# Format all C# files to standard style
dotnet format

# Analyze without fixing
dotnet format --verify-no-changes
```

---

## Step 8: Integration Test - Generate a Pipeline

### Using the Web UI
1. Start the application: `dotnet run --project src/SimlifiezYaml.Web`
2. Navigate to https://localhost:5001
3. Fill in the 15-step wizard:
   - Pipeline name: "my-test-pipeline"
   - Project type: .NET
   - Build agent: Microsoft-hosted
   - Environments: test, preprod, prod
   - Select desired features (artifacts, health checks, etc.)
4. Click "Generate YAML"
5. Verify the generated YAML contains:
   - Proper escaping (quotes, special characters)
   - Expected stages (Build, Test, Artifact, Deploy)
   - Trigger section with branches
   - Validation results

### Using Unit Tests
```powershell
# Run integration test that generates a full pipeline
dotnet test tests/SimlifiezYaml.Core.Tests/PipelineGeneratorTests.cs --verbosity normal

# Expected output: All generation tests pass
# "Generate_IncludesEnterpriseStages" test confirms YAML generation works
```

---

## Step 9: Comprehensive Test Run

Run the full test suite and verify all improvements:

```powershell
# Run all tests with results summary
dotnet test --logger "console;verbosity=normal"

# Expected output:
# Total tests: ~95+ (Phase 1: ~35 + Phase 2: ~35 + Phase 3: ~20)
# Passed: ~95+
# Failed: 0
# Skipped: 0
# Test run successful

# Generate detailed test results
dotnet test --logger "trx;LogFileName=TestResults.trx"

# Test results saved to TestResults.trx (can open in Visual Studio)
```

---

## Step 10: Performance and Metrics

### Measure Build Time
```powershell
# Build and measure time
Measure-Command { dotnet build } | Select-Object TotalSeconds

# Expected: < 30 seconds for debug build
```

### Measure Test Execution Time
```powershell
# Run tests and measure time
Measure-Command { dotnet test -q } | Select-Object TotalSeconds

# Expected: < 60 seconds for full test suite
```

### Check Project Structure
```powershell
# Verify all expected files exist
Test-Path "src/SimlifiezYaml.Core/Services/PipelineYamlAssembler.cs"       # Should be True
Test-Path "src/SimlifiezYaml.Core/Models/TriggerConfig.cs"               # Should be True
Test-Path "tests/SimlifiezYaml.Core.Tests/ErrorPathTests.cs"             # Should be True
Test-Path "tests/SimlifiezYaml.Core.Tests/Phase3ArchitectureTests.cs"     # Should be True
```

---

## Common Commands Reference

```powershell
# Quick build and test
dotnet build && dotnet test -q

# Full build, test, and run web app
dotnet build && dotnet test && dotnet run --project src/SimlifiezYaml.Web

# Clean all build artifacts
dotnet clean

# Run specific test with output
dotnet test --filter "FullyQualifiedName~<namespace>.<ClassName>.<MethodName>" --verbosity normal

# Run tests matching pattern
dotnet test --filter "Name~Validator"
dotnet test --filter "Name~Escaping"
dotnet test --filter "Name~Assembler"
```

---

## Troubleshooting

### Issue: "No .NET SDKs were found"
**Solution:**
```powershell
# Download and install the .NET 10 SDK from https://dotnet.microsoft.com/download
# Restart PowerShell/Terminal after installation
dotnet --version  # Should show 10.0.x
```

### Issue: "dotnet: The term 'dotnet' is not recognized"
**Solution:**
```powershell
# Add .NET to PATH or restart terminal
$env:Path
# If .NET not in path, add: C:\Program Files\dotnet
# Restart PowerShell completely
```

### Issue: "Failed to restore packages"
**Solution:**
```powershell
# Clear cache and retry
dotnet nuget locals all --clear
dotnet restore --force
```

### Issue: Tests fail with "Could not find Microsoft.Extensions.Logging"
**Solution:**
```powershell
# Ensure full restore with dependencies
dotnet restore --force
dotnet clean
dotnet build
```

### Issue: Port 5001 already in use
**Solution:**
```powershell
# Find and stop existing process
$proc = Get-NetTCPConnection -LocalPort 5001 -ErrorAction SilentlyContinue | Select-Object OwningProcess
Stop-Process -Id $proc.OwningProcess -Force

# Or run on different port
dotnet run --project src/SimlifiezYaml.Web -- --urls "https://localhost:5002"
```

### Issue: "Unable to bind to port. Address already in use"
**Solution:**
```powershell
# Application will auto-increment port. Check output for actual port:
# "Now listening on: https://localhost:5003"
# Use that URL in browser
```

---

## Validation Checklist

After completing all steps, verify:

✅ `dotnet build` succeeds with no errors
✅ `dotnet test` shows all ~95+ tests passing
✅ `dotnet run --project src/SimlifiezYaml.Web` starts without errors
✅ Web application opens in browser without security warnings
✅ Can fill wizard and generate YAML successfully
✅ Generated YAML contains proper escaping (Phase 2 improvement)
✅ Generated YAML uses configurable trigger branches (Phase 3 improvement)
✅ Application logs show pipeline generation messages (Phase 2 logging)
✅ All error path tests pass (Phase 1-2 validation)
✅ All architecture tests pass (Phase 3 refactoring)

---

## What to Verify for Each Phase

### Phase 1 (Critical Fixes)
- ✅ No DI anti-patterns (VariableGroupService injected)
- ✅ Pool config centralized in PoolConfigurationHelper
- ✅ Input validation catches invalid configs
- ✅ File operations have error handling

### Phase 2 (Code Quality)
- ✅ PowerShell escaping handles special characters
- ✅ Application logs generation lifecycle
- ✅ Regex patterns validated safely
- ✅ 35+ error path tests all passing

### Phase 3 (Architecture)
- ✅ PipelineYamlAssembler reduces service complexity
- ✅ Hardcoded branches replaced with TriggerConfig
- ✅ Builder pattern for YAML composition
- ✅ 20+ architecture tests all passing
- ✅ Service methods reduced from ~200 to ~120 lines

---

## Next Steps

After successful local testing:
1. Push changes to GitHub with: `push-to-github.ps1`
2. Run tests on CI/CD pipeline
3. Deploy to staging environment
4. Consider Phase 4: Advanced features (templates, validation schema, etc.)

---

**Questions or Issues?** 
- Check troubleshooting section above
- Review error messages carefully
- Run with `--verbosity diagnostic` for more details
- Check application logs in browser console (F12)
