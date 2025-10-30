# UR.RTDE.Grasshopper Tests

Test suite for the UR.RTDE.Grasshopper plugin using [Rhino.Testing](https://github.com/mcneel/Rhino.Testing).

## Running Tests

### From Visual Studio
1. Build the solution
2. Open Test Explorer (Test → Test Explorer)
3. Run All Tests

### From Command Line
```bash
dotnet test
```

Or use the provided script:
```bash
pwsh run-tests.ps1
```

## Test Structure

### Basic Tests
- **SimpleTests.cs**: Basic functionality tests (math, strings, arrays, exceptions)

### Runtime Tests
- **URSessionTests.cs**: Tests for URSession class, connection handling, and command validation

### Utility Tests
- **PoseUtilsTests.cs**: Tests for coordinate transformation between Rhino Planes and UR poses (requires Rhino dependencies)

## Notes

- Tests that require a connected robot will expect connection failures when no robot is available
- Some tests verify input validation without actual robot connection
- PoseUtilsTests require Rhino dependencies and may need special test configuration
