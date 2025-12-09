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

## Test Structure

### Component Tests
- **CommandComponentTests.cs**: Tests for UR_CommandComponent (MoveJ, MoveL, StopJ, StopL, SetDO)
- **ReadComponentTests.cs**: Tests for UR_ReadComponent (Joints, Pose, IO, Modes)
- **SessionComponentTests.cs**: Tests for UR_SessionComponent

### Type Tests
- **URSessionGooTests.cs**: Tests for URSessionGoo wrapper
- **URSessionParamTests.cs**: Tests for URSessionParam

### Utility Tests
- **PoseUtilsTests.cs**: Tests for coordinate transformation between Rhino Planes and UR poses

### Runtime Tests
- **URSessionTests.cs**: Tests for URSession class

### Integration Tests
- **IntegrationTests.cs**: End-to-end component tests

## Notes

- Tests that require a connected robot will expect connection failures when no robot is available
- Some tests verify input validation without actual robot connection
- All command types (MoveJ, MoveL, StopJ, StopL, SetDO) are tested
- All read modes (Joints, Pose, IO, Modes) are tested
