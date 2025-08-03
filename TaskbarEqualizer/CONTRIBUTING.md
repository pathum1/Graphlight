# Contributing to TaskbarEqualizer

Thank you for your interest in contributing to TaskbarEqualizer! This document provides guidelines for contributing to the project.

## Development Setup

### Prerequisites
- Windows 11 (21H2 or later)
- .NET 8 SDK
- Visual Studio 2022 (recommended) or Visual Studio Code
- Git

### Getting Started
1. Fork the repository
2. Clone your fork: `git clone https://github.com/yourusername/TaskbarEqualizer.git`
3. Create a feature branch: `git checkout -b feature/your-feature-name`
4. Make your changes
5. Test thoroughly
6. Commit with clear messages
7. Push to your fork
8. Create a Pull Request

## Project Structure

```
TaskbarEqualizer/
├── src/
│   ├── TaskbarEqualizer/          # Main WPF application
│   ├── TaskbarEqualizer.Core/     # Core audio processing logic
│   └── TaskbarEqualizer.Tests/    # Unit and integration tests
├── docs/                          # Documentation
├── assets/                        # Images, icons, and resources
├── installer/                     # MSI installer project
└── scripts/                       # Build and utility scripts
```

## Coding Standards

### C# Style Guidelines
- Follow Microsoft C# coding conventions
- Use meaningful variable and method names
- Add XML documentation for public APIs
- Keep methods focused and concise
- Use async/await for I/O operations

### Performance Requirements
- Maintain <50ms audio latency
- Keep CPU usage under 3%
- Memory footprint under 30MB
- Target 60 FPS visualization

### Code Example
```csharp
/// <summary>
/// Processes audio samples and converts them to frequency spectrum data.
/// </summary>
/// <param name="samples">Raw audio samples from WASAPI capture.</param>
/// <returns>Frequency spectrum data for visualization.</returns>
public async Task<double[]> ProcessAudioSamplesAsync(float[] samples)
{
    // Implementation here
}
```

## Testing Guidelines

### Unit Tests
- Write tests for all public methods
- Use meaningful test method names
- Follow Arrange-Act-Assert pattern
- Aim for >90% code coverage

### Performance Tests
- Benchmark critical audio processing paths
- Validate memory usage patterns
- Test under various system loads
- Verify real-time constraints

### Integration Tests
- Test complete audio pipeline
- Validate Windows 11 integration
- Test system tray functionality
- Verify configuration persistence

## Submitting Changes

### Pull Request Process
1. Ensure all tests pass
2. Update documentation if needed
3. Add changelog entry
4. Describe your changes clearly
5. Reference any related issues

### Commit Message Format
```
type(scope): brief description

Detailed explanation of the change, including:
- What was changed
- Why it was changed
- Any breaking changes
- Performance implications

Closes #issue-number
```

Types: feat, fix, docs, style, refactor, test, chore

### Code Review Checklist
- [ ] Code follows project style guidelines
- [ ] All tests pass
- [ ] Performance requirements met
- [ ] Documentation updated
- [ ] No breaking changes (or properly documented)

## Areas for Contribution

### High Priority
- Performance optimizations
- Windows 11 theme integration improvements
- Additional audio device support
- Memory usage optimizations

### Medium Priority
- UI/UX enhancements
- Additional visualization modes
- Configuration options
- Error handling improvements

### Low Priority
- Documentation improvements
- Code cleanup and refactoring
- Additional unit tests
- Build process improvements

## Bug Reports

### Before Submitting
- Check existing issues
- Test with latest version
- Verify on clean Windows 11 installation

### Bug Report Template
```
**Environment:**
- Windows version:
- .NET version:
- Audio device:
- System specifications:

**Steps to Reproduce:**
1. 
2. 
3. 

**Expected Behavior:**

**Actual Behavior:**

**Additional Context:**
- Error messages
- Screenshots
- Performance impact
```

## Feature Requests

### Before Submitting
- Check existing feature requests
- Consider implementation complexity
- Ensure alignment with project goals

### Feature Request Template
```
**Problem Statement:**
What problem does this feature solve?

**Proposed Solution:**
How should this feature work?

**Alternative Solutions:**
What other approaches were considered?

**Implementation Notes:**
- Performance considerations
- Compatibility requirements
- User experience impact
```

## Development Phases

The project follows a structured 4-phase development approach:

### Phase 1: Core Infrastructure (In Progress)
- Focus: Audio capture and FFT processing
- Skills needed: C#, audio programming, performance optimization

### Phase 2: Visualization Engine
- Focus: Real-time graphics and Windows 11 integration
- Skills needed: WPF, GDI+, UI/UX design

### Phase 3: User Experience
- Focus: Configuration, settings, user interaction
- Skills needed: WPF, Windows APIs, UX design

### Phase 4: Testing & Deployment
- Focus: Quality assurance, installer, documentation
- Skills needed: Testing, MSI, documentation

## Questions and Support

- Open an issue for bugs or feature requests
- Use discussions for questions and ideas
- Check documentation first
- Be respectful and constructive

## License

By contributing to TaskbarEqualizer, you agree that your contributions will be licensed under the MIT License.